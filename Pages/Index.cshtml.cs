using Pillar.Data;
using Pillar.Models;
namespace Pillar.Pages;
public class IndexModel(Db db, IConfiguration config) : AppPage
{
    public IReadOnlyList<DemographicGroup> Demographics { get; set; } = [];
    public long DemographicPeople => Demographics.FirstOrDefault()?.Total ?? 0;
    public Row Stats { get; set; } = null!;
    public List<Row> RecentMembers { get; set; } = [];
    public List<Row> Events { get; set; } = [];
    public async Task OnGetAsync()
    {
        Stats = (await db.One("""
            SELECT (SELECT count(*) FROM pillar.people WHERE archived_at IS NULL) people,
            (SELECT count(*) FROM pillar.households WHERE archived_at IS NULL) households,
            (SELECT count(*) FROM pillar.attendance_records WHERE voided_at IS NULL AND checked_in_at>=now()-interval '30 days') attendance,
            (SELECT count(*) FROM pillar.ministries WHERE is_active) ministries
            """))!;
        Demographics = DemographicSummary.Build(await db.Read(DemographicSummary.Query, new { zone = config["Pillar:TimeZone"] ?? "Asia/Manila" }));
        RecentMembers = await db.Read("SELECT id,first_name,surname,member_code,congregation_status::text FROM pillar.people WHERE archived_at IS NULL ORDER BY created_at DESC LIMIT 6");
        Events = await db.Read("""
            SELECT e.id,e.title,e.starts_at,e.time_zone,count(a.id) attendance FROM pillar.event_occurrences e
            LEFT JOIN pillar.attendance_records a ON a.event_occurrence_id=e.id AND a.voided_at IS NULL
            WHERE e.cancelled_at IS NULL GROUP BY e.id ORDER BY e.starts_at DESC LIMIT 5
            """);
    }
}
