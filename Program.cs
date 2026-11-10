using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pillar.Data;
using Pillar.Security;
using Pillar.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables();
    if (args.Length > 0) builder.Configuration.AddCommandLine(args);
}
builder.Services.Configure<ForwardedHeadersOptions>(o => HostingSecurity.ConfigureProxy(o, builder.Configuration));
builder.Services.AddHttpsRedirection(o => o.HttpsPort = 443);
var connection = builder.Configuration.GetConnectionString("Pillar");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Set ConnectionStrings__Pillar to your PostgreSQL connection string. See README.md.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (!builder.Environment.IsDevelopment() && cs.SslMode != SslMode.VerifyFull)
    throw new InvalidOperationException("Production PostgreSQL connections require SSL Mode=VerifyFull.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddScoped<Db>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddDbContext<AuthDb>(o => o.UseNpgsql(connection));
builder.Services.AddIdentity<StaffUser, IdentityRole<Guid>>(o =>
{
    o.Password.RequiredLength = 14;
    o.User.RequireUniqueEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.SignIn.RequireConfirmedEmail = true;
}).AddEntityFrameworkStores<AuthDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Denied";
    o.Cookie.Name = "Pillar.Staff";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = false;
    o.Events.OnValidatePrincipal = Access.Validate;
});
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(5);
    o.OnRefreshingPrincipal = context =>
    {
        if (context.CurrentPrincipal?.HasClaim("amr", "mfa") == true && context.NewPrincipal?.Identity is System.Security.Claims.ClaimsIdentity identity)
            identity.AddClaim(new("amr", "mfa"));
        return Task.CompletedTask;
    };
});
var requireMfa = builder.Configuration.GetValue("Security:RequireMfa", true);
if (!builder.Environment.IsDevelopment() && !requireMfa) throw new InvalidOperationException("Staff MFA must be enabled in production.");
builder.Services.AddAuthorization(o =>
{
    foreach (var (name, roles) in new[] {
        (Access.Manage, new[] { "administrator", "membership_manager" }),
        (Access.Attend, Access.Roles), (Access.Admin, new[] { "administrator" }) })
        o.AddPolicy(name, p => { p.RequireAuthenticatedUser().RequireRole(roles); if (requireMfa) p.RequireClaim("amr", "mfa"); });
});
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Members", Access.Manage);
    o.Conventions.AuthorizeFolder("/Families", Access.Manage);
    o.Conventions.AuthorizeFolder("/Ministries", Access.Manage);
    o.Conventions.AuthorizeFolder("/Events", Access.Attend);
    o.Conventions.AuthorizeFolder("/Staff", Access.Admin);
    o.Conventions.AuthorizePage("/Index", Access.Manage);
    o.Conventions.AuthorizePage("/Account/Mfa");
    o.Conventions.AuthorizePage("/Account/Logout");
});
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.IsAuthenticated == true ? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 150, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddFixedWindowLimiter("login", o => { o.PermitLimit = 30; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
});
HostingSecurity.AddKeyStorage(builder.Services, builder.Configuration, builder.Environment.IsDevelopment());

var app = builder.Build();
if (args.Contains("--identity-sql"))
{
    using var scope = app.Services.CreateScope();
    Console.WriteLine(scope.ServiceProvider.GetRequiredService<AuthDb>().Database.GenerateCreateScript());
    return;
}
if (args.Contains("--create-staff")) { await StaffProvisioner.Run(app.Services, builder.Configuration); return; }
if (string.Equals(builder.Configuration["Security:DataProtectionStore"], "Database", StringComparison.OrdinalIgnoreCase))
{
    // Fail during startup if the schema, permissions, or encryption certificate are wrong.
    // This also initializes the shared encrypted key ring before accepting requests.
    var probe = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Pillar.StartupCheck");
    const string expected = "key-storage-ready";
    if (probe.Unprotect(probe.Protect(expected)) != expected) throw new InvalidOperationException("Data-protection startup check failed.");
}
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); app.UseHttpsRedirection(); }
else app.UseExceptionHandler("/Error");
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    ctx.Response.Headers.CacheControl = "no-cache, no-store";
    ctx.Response.Headers.Pragma = "no-cache";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapRazorPages();
app.Run();
