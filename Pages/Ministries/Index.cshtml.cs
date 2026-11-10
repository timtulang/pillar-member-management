using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
namespace Pillar.Pages.Ministries;
public class IndexModel(Db db):AppPage
{
    public List<Row> Ministries{get;set;}=[];public List<Row> Programs{get;set;}=[];
    public async Task OnGetAsync()
    {
        Ministries=await db.Read("SELECT m.id,m.name,count(a.id) people FROM pillar.ministries m LEFT JOIN pillar.ministry_assignments a ON a.ministry_id=m.id AND (a.valid_from IS NULL OR a.valid_from<=CURRENT_DATE) AND (a.valid_to IS NULL OR a.valid_to>CURRENT_DATE) WHERE m.is_active GROUP BY m.id ORDER BY m.name");
        Programs=await db.Read("SELECT p.id,p.name,count(e.id) people FROM pillar.discipleship_programs p LEFT JOIN pillar.discipleship_enrollments e ON e.program_id=p.id WHERE p.is_active GROUP BY p.id ORDER BY p.name");
    }
    public Task<IActionResult> OnPostAsync(string kind,string name)=>Change(async()=>
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>150)throw new InvalidOperationException("Enter a name of up to 150 characters.");
        var table=kind switch{"ministry"=>"ministries","program"=>"discipleship_programs","role"=>"ministry_roles",_=>throw new InvalidOperationException("Choose a valid category.")};
        if(kind=="role"&&name.Length>100)throw new InvalidOperationException("Role names may contain up to 100 characters.");
        var id=Guid.NewGuid();await db.Execute(Actor,"catalog.created",table,id,$"INSERT INTO pillar.{table}(id,name) VALUES(@id,trim(@name))",new{id,name});
    },"Index");
}
