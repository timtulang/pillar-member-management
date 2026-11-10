using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Pillar.Data;

namespace Pillar.Pages.Account;

[EnableRateLimiting("login")]
public class LoginModel(SignInManager<StaffUser> signIn, IConfiguration config) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [BindProperty, Required, StringLength(256), DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await signIn.PasswordSignInAsync(Email.Trim(), Password, false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor) return RedirectToPage("Verify", new { ReturnUrl });
        if (result.Succeeded)
        {
            if (config.GetValue("Security:RequireMfa", true)) return RedirectToPage("Mfa");
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Events");
        }
        ModelState.AddModelError("", "Unable to sign in. Check your details or try again later.");
        return Page();
    }
}
