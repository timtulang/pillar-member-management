using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Pillar.Data;

public class StaffUser : IdentityUser<Guid> { }

public class AuthDb(DbContextOptions<AuthDb> options) : IdentityDbContext<StaffUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("pillar_auth");
    }
}
