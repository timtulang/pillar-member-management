using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Pillar.Data;

namespace Pillar.Security;

public static class Access
{
    public const string Manage = "Manage";
    public const string Attend = "Attend";
    public const string Admin = "Admin";
    public static readonly string[] Roles = ["administrator", "membership_manager", "attendance_operator"];
    public static Guid Actor(this ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("app_user_id")!);

    public static async Task Validate(CookieValidatePrincipalContext context)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true) return;
        var db = context.HttpContext.RequestServices.GetRequiredService<Db>();
        var subject = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.One("SELECT id,disabled_at FROM pillar.app_users WHERE identity_issuer='aspnet-identity' AND identity_subject=@subject", new { subject });
        if (user == null || user.T("disabled_at") != null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        var identity = (ClaimsIdentity)context.Principal.Identity;
        foreach (var claim in identity.FindAll(identity.RoleClaimType).Concat(identity.FindAll("app_user_id")).ToArray()) identity.RemoveClaim(claim);
        identity.AddClaim(new("app_user_id", user.G("id").ToString()));
        foreach (var role in await db.Read("SELECT role::text FROM pillar.app_user_roles WHERE app_user_id=@id", new { id = user.G("id") }))
            identity.AddClaim(new(identity.RoleClaimType, role.S("role")));
    }
}
