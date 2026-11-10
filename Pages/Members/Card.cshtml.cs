using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
using Pillar.Services;
using QRCoder;
namespace Pillar.Pages.Members;
public class CardModel(Db db,AttendanceService attendance,IConfiguration config):AppPage
{
    [BindProperty(SupportsGet=true)]public Guid Id{get;set;}
    public Row Person{get;set;}=null!;
    public string? Qr{get;set;}
    public DateTime? Expires{get;set;}
    public string ChurchName=>config["Pillar:ChurchName"]??"Pillar Family";
    public async Task<IActionResult> OnGetAsync(){var p=await db.One("SELECT id,first_name,surname,member_code FROM pillar.people WHERE id=@Id",new{Id});if(p==null)return NotFound();Person=p;return Page();}
    public async Task<IActionResult> OnPostAsync()
    {
        var loaded=await OnGetAsync();if(loaded is NotFoundResult)return loaded;
        try{var token=await attendance.Issue(Actor,Id);Qr=Convert.ToBase64String(PngByteQRCodeHelper.GetQRCode(token,QRCodeGenerator.ECCLevel.Q,10));Expires=(await db.One("SELECT expires_at FROM pillar.member_cards WHERE person_id=@Id AND revoked_at IS NULL",new{Id}))!.T("expires_at");}
        catch(InvalidOperationException e){ModelState.AddModelError("",e.Message);}
        return Page();
    }
}
