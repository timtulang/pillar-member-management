using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pillar.Data;
using Pillar.Security;

namespace Pillar.Services;

public static class StaffProvisioner
{
    public static async Task Run(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var email = config["Bootstrap:Email"] ?? throw new InvalidOperationException("Set Bootstrap__Email.");
        var password = config["Bootstrap:Password"] ?? throw new InvalidOperationException("Set Bootstrap__Password through a secure environment variable.");
        var role = config["Bootstrap:Role"] ?? "administrator";
        var error = await Create(scope.ServiceProvider, email, password, role, null);
        if (error != null) throw new InvalidOperationException(error);
        Console.WriteLine("Staff account created. Complete authenticator enrollment at first login. Remove bootstrap secrets now.");
    }

    public static async Task<string?> Create(IServiceProvider services, string email, string password, string role, Guid? actor)
    {
        if (!Access.Roles.Contains(role)) return "Choose a valid staff role.";
        var db = services.GetRequiredService<AuthDb>();
        var manager = services.GetRequiredService<UserManager<StaffUser>>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = new StaffUser { Id = Guid.NewGuid(), UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true };
        var created = await manager.CreateAsync(user, password);
        if (!created.Succeeded) return string.Join(" ", created.Errors.Select(e => e.Description));
        var appId = Guid.NewGuid();
        var c = db.Database.GetDbConnection();
        var t = tx.GetDbTransaction();
        await c.ExecuteAsync("""
            INSERT INTO pillar.app_users(id,identity_issuer,identity_subject) VALUES (@appId,'aspnet-identity',@subject);
            INSERT INTO pillar.app_user_roles(app_user_id,role,granted_by_user_id) VALUES (@appId,CAST(@role AS pillar.staff_role),@granter);
            INSERT INTO pillar.audit_events(actor_user_id,system_actor,action,entity_type,entity_id,correlation_id)
            VALUES (@actor,@system,'staff.created','app_users',@appId,@correlation);
            """, new { appId, subject = user.Id.ToString(), role, granter = actor ?? appId, actor, system = actor == null ? "staff-provisioning-cli" : null, correlation = Guid.NewGuid() }, t);
        await tx.CommitAsync();
        return null;
    }
}
