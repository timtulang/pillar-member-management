using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Pillar.Data;

namespace Pillar.Pages.Account;
[EnableRateLimiting("login")]
public class VerifyModel(SignInManager<StaffUser> signIn) : PageModel
{
    [BindProperty, Required, StringLength(30)] public string Code { get; set; } = "";
    [BindProperty] public bool Recovery { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public async Task<IActionResult> OnGetAsync() => await signIn.GetTwoFactorAuthenticationUserAsync() == null ? RedirectToPage("Login") : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = Recovery ? await signIn.TwoFactorRecoveryCodeSignInAsync(Code.Trim()) : await signIn.TwoFactorAuthenticatorSignInAsync(Code.Replace(" ", "").Replace("-", ""), false, false);
        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Events");
        ModelState.AddModelError("", "Unable to verify this code. Try a current code or a recovery code."); return Page();
    }
}
