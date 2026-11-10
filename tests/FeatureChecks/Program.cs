using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Pillar.Data;
using Pillar.Models;
using Pillar.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Reject(EventInput input, string message)
{
    try { EventSchedule.Build(input); }
    catch (InvalidOperationException) { checks++; return; }
    throw new Exception(message);
}
bool Valid(object model) => Validator.TryValidateObject(model, new ValidationContext(model), [], true);
EventInput Event(string repeat = "none") => new() { Title = "Test gathering", StartsAt = new(2026, 1, 31, 9, 0, 0), EndsAt = new(2026, 1, 31, 10, 30, 0), Repeat = repeat, Occurrences = 3 };
MemberInput Member() => new() { FirstName = "Test", Surname = "Person", Gender = "female", BirthDate = new(2000, 1, 1) };

var single = EventSchedule.Build(Event());
Check(single.Count == 1 && single[0].Start == new DateTime(2026, 1, 31, 1, 0, 0, DateTimeKind.Utc), "Single event must convert Manila time to UTC.");
Check(single[0].Opens == single[0].Start.AddHours(-1) && single[0].Closes == single[0].End.AddMinutes(30), "Check-in windows must follow event times.");
var monthly = EventSchedule.Build(Event("monthly"));
Check(monthly.Select(e => e.Start.Day).SequenceEqual(new[] { 31, 28, 31 }), "Monthly repeats must clamp short months without drifting.");
var leap = Event("monthly"); leap.StartsAt = new(2028, 1, 31, 23, 0, 0); leap.EndsAt = new(2028, 2, 1, 1, 0, 0);
var leapEvents = EventSchedule.Build(leap);
Check(leapEvents[1].Start.Day == 29 && leapEvents[1].End - leapEvents[1].Start == TimeSpan.FromHours(2), "Leap years and overnight duration must be preserved.");
var weekly = Event("weekly"); weekly.Interval = 2;
Check(EventSchedule.Build(weekly)[2].Start - single[0].Start == TimeSpan.FromDays(28), "Every-other-week schedule is wrong.");
var daily = Event("daily"); daily.Occurrences = 52;
Check(EventSchedule.Build(daily).Count == 52, "Maximum bounded recurrence must be accepted.");
var dst = Event("weekly"); dst.TimeZone = "America/New_York"; dst.StartsAt = new(2026, 3, 1, 9, 0, 0); dst.EndsAt = new(2026, 3, 1, 10, 0, 0);
var dstEvents = EventSchedule.Build(dst);
Check(dstEvents[0].Start.Hour == 14 && dstEvents[1].Start.Hour == 13, "Repeats must preserve local time across daylight saving.");
dst.StartsAt = new(2026, 3, 1, 2, 30, 0); dst.EndsAt = new(2026, 3, 1, 3, 30, 0);
Reject(dst, "A later nonexistent local time must reject the entire schedule.");
dst.StartsAt = new(2026, 10, 25, 1, 30, 0); dst.EndsAt = new(2026, 10, 25, 2, 30, 0);
Reject(dst, "A later ambiguous local time must reject the entire schedule.");
var invalid = Event("weekly"); invalid.Occurrences = 53; Reject(invalid, "Unbounded recurrence accepted.");
invalid = Event("weekly"); invalid.Interval = 0; Reject(invalid, "Zero interval accepted.");
invalid = Event("weekly"); invalid.Occurrences = 1; Reject(invalid, "Recurring count must be at least two.");
invalid = Event(); invalid.TimeZone = "Not/AZone"; Reject(invalid, "Invalid timezone accepted.");
invalid = Event(); invalid.EndsAt = invalid.StartsAt; Reject(invalid, "Zero duration accepted.");
invalid = Event(); invalid.StartsAt = null; Reject(invalid, "Missing start accepted.");
invalid = Event(); invalid.Title = "  "; Reject(invalid, "Blank title accepted.");
invalid = Event("yearly"); Reject(invalid, "Unsupported frequency accepted.");
invalid = Event(); invalid.MinutesBefore = -1; Reject(invalid, "Negative check-in window accepted.");
invalid = Event("monthly"); invalid.StartsAt = new(9999, 12, 30); invalid.EndsAt = new(9999, 12, 31); Reject(invalid, "Date overflow must give a validation error.");
Check(Valid(Member()), "Valid member rejected.");
var member = Member(); member.Gender = "arbitrary"; Check(!Valid(member), "Unlisted gender accepted.");
member = Member(); member.Gender = null; Check(Valid(member), "Optional gender should accept missing data.");
member = Member(); member.Gender = "Female"; Check(Valid(member) && MemberInput.NormalizeGender(member.Gender) == "female", "Legacy capitalization must normalize.");
member = Member(); member.Phone = "not a phone"; Check(!Valid(member), "Invalid phone accepted.");
member = Member(); member.Email = "not an email"; Check(!Valid(member), "Invalid email accepted.");
member = Member(); member.BirthDate = DateTime.UtcNow.AddDays(1); Check(!Valid(member), "Future birthday accepted.");
member = Member(); member.MembershipDate = new(1999, 1, 1); Check(!Valid(member), "Membership before birthday accepted.");
member = Member(); member.DeceasedOn = new(1999, 1, 1); Check(!Valid(member), "Death before birthday accepted.");
member = Member(); member.ChildCount = -1; Check(!Valid(member), "Negative child count accepted.");
member = Member(); member.Status = "made_up"; Check(!Valid(member), "Unlisted membership status accepted.");
Check(DisplayLabels.Text("regular_attendee") == "Regular attendee" && DisplayLabels.Text("non_binary") == "Non-binary", "Labels should be readable without changing stored values.");
var empty = DemographicSummary.Build([]);
Check(empty.All(g => g.Total == 0 && g.Percentage(0) == "0%"), "Empty demographics must avoid division by zero.");
Row Row(params (string Key, object Value)[] fields) => new(fields.ToDictionary(x => x.Key, x => x.Value));
var demo = DemographicSummary.Build([
    Row(("dimension", "gender"), ("category", "female"), ("people", 2L)),
    Row(("dimension", "gender"), ("category", "not_provided"), ("people", 1L)),
    Row(("dimension", "gender"), ("category", "other_recorded"), ("people", 1L))
]);
var genderGroup = demo.Single(g => g.Title == "Gender");
Check(genderGroup.Total == 4 && genderGroup.Buckets.Any(b => b.Label == "Other recorded values" && b.Count == 1), "Legacy and missing genders must not disappear from totals.");
Check(genderGroup.Percentage(2) == "50%", "Demographic percentages must use the full population.");

// Render the real compiled Razor pages in-process, without a web server or database.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(Pillar.Pages.IndexModel).Assembly.FullName });
builder.Services.AddRazorPages();
var endpointSources = new List<Microsoft.AspNetCore.Routing.EndpointDataSource>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Routing.EndpointDataSource>(_ => new Microsoft.AspNetCore.Routing.CompositeEndpointDataSource(endpointSources));
await using var app = builder.Build();
app.MapRazorPages();
endpointSources.AddRange(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources);
using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;
async Task<string> Render<T>(string path, T model)
{
    var http = new DefaultHttpContext { RequestServices = services };
    http.User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "synthetic-user"), new(ClaimTypes.Role, "administrator")], "test"));
    http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "render"));
    var routeData = new Microsoft.AspNetCore.Routing.RouteData();
    routeData.Values["page"] = path.Replace("/Pages", "").Replace(".cshtml", "");
    var action = new ActionContext(http, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
    var metadata = services.GetRequiredService<IModelMetadataProvider>();
    var data = new ViewDataDictionary<T>(metadata, new ModelStateDictionary()) { Model = model };
    var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, path, true).View ?? throw new Exception("View missing: " + path);
    ((Page)((RazorView)view).RazorPage).PageContext = new PageContext(action) { ViewData = data };
    using var writer = new StringWriter();
    await view.RenderAsync(new ViewContext(action, view, data, new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions()));
    return writer.ToString();
}
var eventPage = new Pillar.Pages.Events.IndexModel(null!, builder.Configuration) { Input = Event("weekly") };
var eventHtml = await Render("/Pages/Events/Index.cshtml", eventPage);
Check(eventHtml.Contains("name=\"Input.Repeat\"") && eventHtml.Contains("value=\"weekly\" selected=\"selected\""), "Recurring event controls must bind and retain selections.");
var memberHtml = await Render("/Pages/Members/Edit.cshtml", new Pillar.Pages.Members.EditModel(null!, null!) { Input = Member() });
Check(memberHtml.Contains("<select") && memberHtml.Contains("name=\"Input.Gender\"") && memberHtml.Contains(">Member</option>"), "Member form must render gender choices and readable statuses.");
var overview = new Pillar.Pages.IndexModel(null!, builder.Configuration) { Stats = Row(), Demographics = demo };
// Ensure a nonzero population is represented in the age group too.
overview.Demographics = [new("Age groups", [new("18–24", 4)]), genderGroup];
var overviewHtml = await Render("/Pages/Index.cshtml", overview);
Check(overviewHtml.Contains("<meter") && overviewHtml.Contains("Other recorded values"), "Overview must render demographic bars and legacy data.");
var ministriesHtml = await Render("/Pages/Ministries/Index.cshtml", new Pillar.Pages.Ministries.IndexModel(null!));
Check(ministriesHtml.Contains("A place for every gift."), "Empty ministry dashboard must render useful guidance.");
var ministryId = Guid.NewGuid();
var programId = Guid.NewGuid();
var personId = Guid.NewGuid();
var servingPerson = Row(("ministry_id", ministryId), ("person_id", personId), ("first_name", "Synthetic"), ("surname", "Volunteer"), ("member_code", "PF-TEST"), ("role_name", "Leader"), ("is_main", true));
var learningPerson = Row(("program_id", programId), ("person_id", personId), ("first_name", "Synthetic"), ("surname", "Volunteer"), ("member_code", "PF-TEST"), ("status", "in_progress"));
var rosterModel = new Pillar.Pages.Ministries.IndexModel(null!) {
    Ministries = [Row(("id", ministryId), ("name", "Music"))], Programs = [Row(("id", programId), ("name", "Growth"))],
    Serving = [servingPerson], Enrollments = [learningPerson]
};
typeof(Pillar.Pages.Ministries.IndexModel).GetProperty("MinistryRosters")!.SetValue(rosterModel, rosterModel.Serving.ToLookup(p => p.G("ministry_id")));
typeof(Pillar.Pages.Ministries.IndexModel).GetProperty("ProgramRosters")!.SetValue(rosterModel, rosterModel.Enrollments.ToLookup(p => p.G("program_id")));
var rosterHtml = await Render("/Pages/Ministries/Index.cshtml", rosterModel);
Check(rosterHtml.Contains("Synthetic Volunteer") && rosterHtml.Contains("Leader") && rosterHtml.Contains("In progress"), "Rosters must render member names, roles, and participation status.");
Check(rosterHtml.Contains("data-roster-search") && rosterHtml.Contains(personId.ToString()), "Rosters must include searchable lists and member profile links.");
Check(rosterModel.ServingPeople == 1 && rosterModel.LearningPeople == 1, "Roster summary counts must count unique people.");
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    await File.WriteAllTextAsync(Path.Combine(args[0], "demographics.sql"), DemographicSummary.Query.Replace("@zone", "'Asia/Manila'"));
    await File.WriteAllTextAsync(Path.Combine(args[0], "events.html"), eventHtml);
    await File.WriteAllTextAsync(Path.Combine(args[0], "member.html"), memberHtml);
    await File.WriteAllTextAsync(Path.Combine(args[0], "overview.html"), overviewHtml);
    await File.WriteAllTextAsync(Path.Combine(args[0], "rosters.html"), rosterHtml);
}
Console.WriteLine($"PASS {checks} scheduling, validation, demographic, and Razor rendering checks.");
