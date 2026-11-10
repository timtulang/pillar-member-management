using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pillar.Data;
using QRCoder;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pillar.Security;

namespace Pillar.Pages.Account;
public class MfaModel(UserManager<StaffUser> users, SignInManager<StaffUser> signIn, AuthDb auth) : PageModel
{
    [BindProperty, Required, StringLength(10)] public string Code { get; set; } = "";
    public bool Enabled { get; set; }
    public string? Key { get; set; }
    public string? Qr { get; set; }
    public IEnumerable<string>? RecoveryCodes { get; set; }
    public async Task<IActionResult> OnGetAsync() { await Load(); return Page(); }
    private async Task Load()
    {
        var user = (await users.GetUserAsync(User))!;
        Enabled = await users.GetTwoFactorEnabledAsync(user);
        if (Enabled) return;
        Key = await users.GetAuthenticatorKeyAsync(user);
        if (Key == null) { await users.ResetAuthenticatorKeyAsync(user); Key = await users.GetAuthenticatorKeyAsync(user); }
        var uri = $"otpauth://totp/Pillar:{Uri.EscapeDataString(user.Email!)}?secret={Key}&issuer=Pillar&digits=6";
        Qr = Convert.ToBase64String(PngByteQRCodeHelper.GetQRCode(uri, QRCodeGenerator.ECCLevel.Q, 6));
    }
    public async Task<IActionResult> OnPostAsync()
    {
        var user = (await users.GetUserAsync(User))!;
        if (await users.GetTwoFactorEnabledAsync(user)) return RedirectToPage();
        if (await users.IsLockedOutAsync(user)) { ModelState.AddModelError("", "Too many attempts. Try again later."); await Load(); return Page(); }
        if (!ModelState.IsValid || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Code.Replace(" ", "").Replace("-", "")))
        { await users.AccessFailedAsync(user); ModelState.AddModelError("", "Check your authenticator code."); await Load(); return Page(); }
        await using (var tx = await auth.Database.BeginTransactionAsync())
        {
            await users.ResetAccessFailedCountAsync(user);
            var enabled = await users.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded) throw new InvalidOperationException("Authenticator enrollment could not be completed.");
            RecoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            await auth.Database.GetDbConnection().ExecuteAsync("INSERT INTO pillar.audit_events(actor_user_id,action,entity_type,entity_id,correlation_id) VALUES(@actor,'staff.mfa_enabled','app_users',@actor,@correlation)",new{actor=User.Actor(),correlation=Guid.NewGuid()},tx.GetDbTransaction());
            await tx.CommitAsync();
        }
        await signIn.SignInWithClaimsAsync(user, false, [new Claim("amr", "mfa")]);
        Enabled = true;
        return Page();
    }
}
