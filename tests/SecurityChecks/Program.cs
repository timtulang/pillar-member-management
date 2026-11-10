using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pillar.Security;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
(string Certificate, string Password) Certificate()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=Synthetic key encryption test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    var password = Guid.NewGuid().ToString();
    return (Convert.ToBase64String(cert.Export(X509ContentType.Pfx, password)), password);
}
IConfiguration Config((string Certificate, string Password) cert, string store = "Database", string platform = "Vercel", (string Certificate, string Password)? previous = null) =>
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Security:DataProtectionStore"] = store, ["Security:HostingPlatform"] = platform,
        ["Security:DataProtectionCertificate"] = cert.Certificate, ["Security:DataProtectionCertificatePassword"] = cert.Password,
        ["Security:DataProtectionPreviousCertificates:0:Certificate"] = previous?.Certificate,
        ["Security:DataProtectionPreviousCertificates:0:Password"] = previous?.Password
    }.Where(kv => kv.Value != null)).Build();
ServiceProvider Provider(IConfiguration config, MemoryRepository repository)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Database=unused;Username=unused;Pooling=false"));
    HostingSecurity.AddKeyStorage(services, config, false);
    services.PostConfigure<KeyManagementOptions>(o => o.XmlRepository = repository);
    return services.BuildServiceProvider();
}
void Reject(IConfiguration config)
{
    try { HostingSecurity.AddKeyStorage(new ServiceCollection(), config, false); }
    catch (InvalidOperationException) { checks++; return; }
    throw new Exception("Invalid hosting configuration was accepted.");
}
var original = Certificate();
var repo = new MemoryRepository();
string token;
using (var first = Provider(Config(original), repo))
    token = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-cookie").Protect("synthetic-session");
Check(repo.GetAllElements().Count > 0, "No key persisted.");
Check(repo.GetAllElements().All(e => e.Descendants().Any(d => d.Name.LocalName == "encryptedSecret")), "Persisted keys must be certificate-encrypted.");
Check(repo.GetAllElements().All(e => !e.Descendants().Any(d => d.Name.LocalName == "masterKey")), "Plaintext master keys must never be stored.");
using (var restarted = Provider(Config((original.Certificate, original.Password + "\n")), repo))
{
    Check(restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-cookie").Unprotect(token) == "synthetic-session", "A new instance must read the first instance's session.");
    var keyCount = repo.GetAllElements().Count;
    restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-cookie").Protect("second-session");
    Check(repo.GetAllElements().Count == keyCount, "Restarting must reuse persisted keys.");
}
var replacement = Certificate();
using (var rotated = Provider(Config(replacement, previous: original), repo))
{
    Check(rotated.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-cookie").Unprotect(token) == "synthetic-session", "Certificate rotation must preserve access to older keys.");
}
using (var wrong = Provider(Config(replacement), repo))
{
    var rejected = false;
    try { wrong.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-cookie").Unprotect(token); }
    catch (CryptographicException) { rejected = true; }
    Check(rejected, "An unrelated certificate must not decrypt existing sessions.");
}
Reject(Config(original, store: "FileSystem"));
Reject(Config(original, store: "Unsupported", platform: "Standard"));
Reject(Config(("not a certificate", "incorrect")));
Reject(Config(("", "")));
var standard = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Security:HostingPlatform"] = "Standard" }).Build();
Reject(standard); // Production still requires persistent storage outside Vercel too.
var dev = new ServiceCollection();
HostingSecurity.AddKeyStorage(dev, standard, true);
checks++;

async Task<HttpContext> Forward(IConfiguration config)
{
    var options = new ForwardedHeadersOptions();
    HostingSecurity.ConfigureProxy(options, config);
    using var loggerFactory = LoggerFactory.Create(_ => { });
    var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(options));
    var ctx = new DefaultHttpContext();
    ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
    ctx.Request.Scheme = "http";
    ctx.Request.Host = new HostString("members.example.org");
    ctx.Request.Headers["X-Forwarded-Proto"] = "https";
    ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
    ctx.Request.Headers["X-Forwarded-Host"] = "untrusted.example";
    await middleware.Invoke(ctx);
    return ctx;
}
var forwarded = await Forward(Config(original));
Check(forwarded.Request.Scheme == "https", "Vercel's HTTPS scheme must be recognized.");
Check(forwarded.Connection.RemoteIpAddress!.ToString() == "203.0.113.10", "Vercel's client IP must be applied for rate limiting.");
Check(forwarded.Request.Host.Host == "members.example.org", "Forwarded host must not override the validated host.");
var untrusted = await Forward(standard);
Check(untrusted.Request.Scheme == "http" && untrusted.Connection.RemoteIpAddress!.ToString() == "10.1.2.3", "Ordinary deployments must continue rejecting unknown proxies.");
Console.WriteLine($"PASS {checks} encryption, restart, certificate rotation, configuration, and proxy checks.");

sealed class MemoryRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];
    public IReadOnlyCollection<XElement> GetAllElements() { lock (elements) return elements.Select(e => new XElement(e)).ToArray(); }
    public void StoreElement(XElement element, string friendlyName) { lock (elements) elements.Add(new XElement(element)); }
}
