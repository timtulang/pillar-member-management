using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
namespace Pillar.Pages.Events;
public class IndexModel(Db db,IConfiguration config):AppPage
{
    public List<Row> Events{get;set;}=[];
    public string Zone=>config["Pillar:TimeZone"]??"Asia/Manila";
    public async Task OnGetAsync()=>Events=await db.Read("""
        SELECT e.*,count(a.id) attendance FROM pillar.event_occurrences e
        LEFT JOIN pillar.attendance_records a ON a.event_occurrence_id=e.id AND a.voided_at IS NULL
        GROUP BY e.id ORDER BY e.starts_at DESC LIMIT 100
        """);
    public async Task<IActionResult> OnPostAsync(string title,string? location,DateTime startsAt,DateTime endsAt,string timeZone,int minutesBefore=60,int minutesAfter=30)
    {
        if(!(await CanManage()).Succeeded)return Forbid();
        return await Change(async()=>
        {
            if(string.IsNullOrWhiteSpace(title)||title.Length>200||location?.Length>250||startsAt.Year<2000||endsAt<=startsAt||minutesBefore<0||minutesBefore>1440||minutesAfter<0||minutesAfter>1440)throw new InvalidOperationException("Check the event title, times, and check-in window.");
            TimeZoneInfo zone;try{zone=TimeZoneInfo.FindSystemTimeZoneById(timeZone);}catch(TimeZoneNotFoundException){throw new InvalidOperationException("Enter a valid IANA timezone, such as Asia/Manila.");}
            if(zone.IsInvalidTime(startsAt)||zone.IsInvalidTime(endsAt)||zone.IsAmbiguousTime(startsAt)||zone.IsAmbiguousTime(endsAt))throw new InvalidOperationException("The selected time is ambiguous or invalid during a daylight-saving change. Choose another time.");
            var start=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startsAt,DateTimeKind.Unspecified),zone);var end=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endsAt,DateTimeKind.Unspecified),zone);
            var id=Guid.NewGuid();await db.Execute(Actor,"event.created","event_occurrences",id,"""
                INSERT INTO pillar.event_occurrences(id,title,location,starts_at,ends_at,time_zone,check_in_opens_at,check_in_closes_at,created_by_user_id)
                VALUES(@id,trim(@title),@location,@start,@end,@timeZone,@opens,@closes,@actor)
                """,new{id,title,location,start,end,timeZone,opens=start.AddMinutes(-minutesBefore),closes=end.AddMinutes(minutesAfter),actor=Actor});
        },"Index",notice:"Event created. Open it to start checking people in.");
    }
}
