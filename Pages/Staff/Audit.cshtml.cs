using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
namespace Pillar.Pages.Staff;
public class AuditModel(Db db):AppPage
{
    [BindProperty(SupportsGet=true)]public int PageNumber{get;set;}=1;
    public List<Row> Events{get;set;}=[];
    public async Task OnGetAsync(){PageNumber=Math.Clamp(PageNumber,1,100000);Events=await db.Read("""
        SELECT a.*,coalesce(u."Email",a.system_actor) actor FROM pillar.audit_events a
        LEFT JOIN pillar.app_users p ON p.id=a.actor_user_id
        LEFT JOIN pillar_auth."AspNetUsers" u ON u."Id"::text=p.identity_subject
        ORDER BY a.occurred_at DESC,a.id LIMIT 50 OFFSET @offset
        """,new{offset=(PageNumber-1)*50});}
}
