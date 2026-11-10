using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pillar.Data;
namespace Pillar.Pages.Account;
public class LogoutModel(SignInManager<StaffUser> signIn) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("Login");
    public async Task<IActionResult> OnPostAsync() { await signIn.SignOutAsync(); return RedirectToPage("Login"); }
}
