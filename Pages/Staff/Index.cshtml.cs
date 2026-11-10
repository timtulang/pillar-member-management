using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using Pillar.Data;
using Pillar.Security;
using Pillar.Services;
namespace Pillar.Pages.Staff;
public class IndexModel(Db db,IServiceProvider services):AppPage
{
    public List<Row> Staff{get;set;}=[];
    public async Task OnGetAsync()=>Staff=await db.Read("""
        SELECT a.id,a.disabled_at,u."Email" email,string_agg(r.role::text,', ') roles
        FROM pillar.app_users a JOIN pillar_auth."AspNetUsers" u ON u."Id"::text=a.identity_subject AND a.identity_issuer='aspnet-identity'
        LEFT JOIN pillar.app_user_roles r ON r.app_user_id=a.id GROUP BY a.id,u."Email" ORDER BY u."Email"
        """);
    public async Task<IActionResult> OnPostCreateAsync(string email,string password,string role)
    {
        return await Change(async()=>
        {
            if(string.IsNullOrWhiteSpace(email)||email.Length>254||!new EmailAddressAttribute().IsValid(email)||password==null||password.Length>256)throw new InvalidOperationException("Enter a valid email address and a password of 14–256 characters.");
            var error=await StaffProvisioner.Create(services,email,password,role,Actor);
            if(error!=null)throw new InvalidOperationException(error);
        },"Index",notice:"Staff account created. Share access privately; authenticator enrollment is required at first login.");
    }
    public Task<IActionResult> OnPostAccessAsync(Guid userId,string role,bool disabled)=>Change(async()=>
    {
        if(userId==Actor)throw new InvalidOperationException("Another administrator must change your own access.");
        if(!Access.Roles.Contains(role))throw new InvalidOperationException("Choose a valid role.");
        await db.Write(Actor,"staff.access_changed","app_users",userId,async(c,t)=>
        {
            var remaining=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM pillar.app_users u JOIN pillar.app_user_roles r ON r.app_user_id=u.id WHERE u.id<>@userId AND u.disabled_at IS NULL AND r.role='administrator'",new{userId},t);
            if(remaining==0&&(disabled||role!="administrator"))throw new InvalidOperationException("At least one active administrator must remain.");
            await c.ExecuteAsync("""
                UPDATE pillar.app_users SET disabled_at=CASE WHEN @disabled THEN now() ELSE NULL END WHERE id=@userId;
                DELETE FROM pillar.app_user_roles WHERE app_user_id=@userId;
                INSERT INTO pillar.app_user_roles(app_user_id,role,granted_by_user_id) VALUES(@userId,CAST(@role AS pillar.staff_role),@actor)
                """,new{disabled,userId,role,actor=Actor},t);
            return 1;
        },["role","disabled_at"]);
    },"Index");
}
