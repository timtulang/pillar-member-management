using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
namespace Pillar.Pages.Families;
public class IndexModel(Db db):AppPage
{
    public List<Row> Families{get;set;}=[];
    public List<Row> People{get;set;}=[];
    public async Task OnGetAsync()
    {
        Families=await db.Read("SELECT id,display_name FROM pillar.households WHERE archived_at IS NULL ORDER BY display_name");
        People=await db.Read("""
            SELECT h.household_id,h.role::text,p.id,p.first_name,p.surname,p.member_code FROM pillar.household_memberships h
            JOIN pillar.people p ON p.id=h.person_id WHERE (h.valid_from IS NULL OR h.valid_from<=CURRENT_DATE)
            AND (h.valid_to IS NULL OR h.valid_to>CURRENT_DATE) ORDER BY p.surname,p.first_name
            """);
    }
    public Task<IActionResult> OnPostAsync(string name)=>Change(async()=>
    {if(string.IsNullOrWhiteSpace(name)||name.Length>200)throw new InvalidOperationException("Enter a household name of up to 200 characters.");var id=Guid.NewGuid();await db.Execute(Actor,"household.created","households",id,"INSERT INTO pillar.households(id,display_name) VALUES(@id,trim(@name))",new{id,name});},"Index",notice:"Household created. Add people from their member profiles.");
}
