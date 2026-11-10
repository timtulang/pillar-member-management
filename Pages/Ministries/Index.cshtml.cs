using Microsoft.AspNetCore.Mvc;
using Pillar.Data;

namespace Pillar.Pages.Ministries;

public class IndexModel(Db db) : AppPage
{
    public List<Row> Ministries { get; set; } = [];
    public List<Row> Programs { get; set; } = [];
    public List<Row> Serving { get; set; } = [];
    public List<Row> Enrollments { get; set; } = [];
    public ILookup<Guid, Row> MinistryRosters { get; private set; } = Array.Empty<Row>().ToLookup(r => r.G("ministry_id"));
    public ILookup<Guid, Row> ProgramRosters { get; private set; } = Array.Empty<Row>().ToLookup(r => r.G("program_id"));
    public int ServingPeople => Serving.Select(p => p.G("person_id")).Distinct().Count();
    public int LearningPeople => Enrollments.Where(e => e.S("status") is "in_progress" or "reported_participation").Select(p => p.G("person_id")).Distinct().Count();
    public async Task OnGetAsync()
    {
        Ministries = await db.Read("SELECT id,name FROM pillar.ministries WHERE is_active ORDER BY name");
        Programs = await db.Read("SELECT id,name FROM pillar.discipleship_programs WHERE is_active ORDER BY name");
        Serving = await db.Read("""
            SELECT a.ministry_id,a.person_id,a.is_main,a.valid_from,p.first_name,p.surname,p.member_code,r.name role_name
            FROM pillar.ministry_assignments a
            JOIN pillar.ministries m ON m.id=a.ministry_id AND m.is_active
            JOIN pillar.people p ON p.id=a.person_id AND p.archived_at IS NULL AND p.deceased_on IS NULL
            LEFT JOIN pillar.ministry_roles r ON r.id=a.ministry_role_id
            WHERE (a.valid_from IS NULL OR a.valid_from<=CURRENT_DATE) AND (a.valid_to IS NULL OR a.valid_to>CURRENT_DATE)
            ORDER BY p.surname,p.first_name,p.id
            """);
        Enrollments = await db.Read("""
            SELECT e.program_id,e.person_id,e.status::text,e.started_on,e.completed_on,p.first_name,p.surname,p.member_code
            FROM pillar.discipleship_enrollments e
            JOIN pillar.discipleship_programs d ON d.id=e.program_id AND d.is_active
            JOIN pillar.people p ON p.id=e.person_id AND p.archived_at IS NULL AND p.deceased_on IS NULL
            ORDER BY p.surname,p.first_name,p.id
            """);
        MinistryRosters = Serving.ToLookup(p => p.G("ministry_id"));
        ProgramRosters = Enrollments.ToLookup(p => p.G("program_id"));
    }
    public Task<IActionResult> OnPostAsync(string kind, string name) => Change(async () =>
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150) throw new InvalidOperationException("Enter a name of up to 150 characters.");
        var table = kind switch { "ministry" => "ministries", "program" => "discipleship_programs", "role" => "ministry_roles", _ => throw new InvalidOperationException("Choose a valid category.") };
        if (kind == "role" && name.Length > 100) throw new InvalidOperationException("Role names may contain up to 100 characters.");
        var id = Guid.NewGuid();
        await db.Execute(Actor, "catalog.created", table, id, $"INSERT INTO pillar.{table}(id,name) VALUES(@id,trim(@name))", new { id, name });
    }, "Index");
}
