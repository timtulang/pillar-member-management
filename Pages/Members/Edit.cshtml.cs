using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Pillar.Data;
using Pillar.Models;
using Pillar.Services;
namespace Pillar.Pages.Members;
public class EditModel(Db db, MemberService members) : AppPage
{
    [BindProperty(SupportsGet=true)] public Guid? Id { get; set; }
    [BindProperty] public MemberInput Input { get; set; } = new();
    public async Task<IActionResult> OnGetAsync()
    {
        if(Id==null) return Page();
        var p=await db.One("SELECT *,congregation_status::text status,reported_civil_status::text civil FROM pillar.people WHERE id=@Id",new{Id});
        if(p==null)return NotFound();
        Input=new(){FirstName=p.S("first_name"),MiddleName=p.S("middle_name"),Surname=p.S("surname"),Suffix=p.S("suffix"),PreferredName=p.S("preferred_name"),Gender=MemberInput.NormalizeGender(p.S("gender")),BirthDate=p.T("birth_date"),Address=p.S("residential_address"),Phone=p.S("phone"),Email=p.S("email"),Status=p.S("status"),MembershipDate=p.T("membership_date"),CivilStatus=p.S("civil"),ChildCount=string.IsNullOrEmpty(p.S("reported_child_count"))?null:(int)p.N("reported_child_count"),Inviter=p.S("inviter_name_reported"),DeceasedOn=p.T("deceased_on"),Version=p.N("version")};
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if(!ModelState.IsValid)return Page();
        try { var id=await members.Save(Actor,Id,Input); Notice="Member profile saved.";return RedirectToPage("Details",new{id}); }
        catch(InvalidOperationException e){ModelState.AddModelError("",e.Message);}
        catch(PostgresException e) when(e.SqlState.StartsWith("23")){ModelState.AddModelError("",DbError(e));}
        return Page();
    }
}
