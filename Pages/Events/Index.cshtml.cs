using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Pillar.Data;
using Pillar.Models;
using Pillar.Services;

namespace Pillar.Pages.Events;

public class IndexModel(Db db, IConfiguration config) : AppPage
{
    public List<Row> Events { get; set; } = [];
    [BindProperty] public EventInput Input { get; set; } = new();
    public async Task OnGetAsync()
    {
        Input.TimeZone = config["Pillar:TimeZone"] ?? "Asia/Manila";
        await Load();
    }

    private async Task Load() => Events = await db.Read("""
        SELECT e.*,count(a.id) attendance FROM pillar.event_occurrences e
        LEFT JOIN pillar.attendance_records a ON a.event_occurrence_id=e.id AND a.voided_at IS NULL
        GROUP BY e.id
        ORDER BY CASE WHEN e.cancelled_at IS NULL AND e.ends_at>=now() THEN 0 ELSE 1 END,
            CASE WHEN e.cancelled_at IS NULL AND e.ends_at>=now() THEN e.starts_at END ASC,
            e.starts_at DESC LIMIT 100
        """);

    public async Task<IActionResult> OnPostAsync()
    {
        if (!(await CanManage()).Succeeded) return Forbid();
        if (ModelState.IsValid)
        {
            try
            {
                var schedule = EventSchedule.Build(Input);
                var events = schedule.Select(e => new { id = Guid.NewGuid(), title = Input.Title.Trim(), location = Input.Location?.Trim(),
                    e.Start, e.End, e.Opens, e.Closes, timeZone = Input.TimeZone, actor = Actor }).ToArray();
                await db.Write(Actor, "event.created", "event_occurrences", events[0].id, async (c, t) =>
                {
                    var count = await c.ExecuteAsync("""
                        INSERT INTO pillar.event_occurrences(id,title,location,starts_at,ends_at,time_zone,check_in_opens_at,check_in_closes_at,created_by_user_id)
                        VALUES(@id,@title,@location,@Start,@End,@timeZone,@Opens,@Closes,@actor)
                        """, events, t);
                    // Every occurrence has its own audit record, in the same transaction.
                    foreach (var e in events.Skip(1))
                        await c.ExecuteAsync("""
                            INSERT INTO pillar.audit_events(actor_user_id,action,entity_type,entity_id,correlation_id)
                            VALUES(@actor,'event.created','event_occurrences',@id,@correlation)
                            """, new { actor = Actor, e.id, correlation = events[0].id }, t);
                    return count;
                });
                Notice = events.Length == 1 ? "Event created. Open it to start checking people in."
                    : $"{events.Length} events created. Each occurrence has its own attendance list.";
                return RedirectToPage();
            }
            catch (InvalidOperationException e) { ModelState.AddModelError("", e.Message); }
            catch (PostgresException e) when (e.SqlState.StartsWith("23")) { ModelState.AddModelError("", DbError(e)); }
        }
        await Load();
        return Page();
    }
}
