using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using Pillar.Security;

namespace Pillar.Pages;

public abstract class AppPage : PageModel
{
    public Guid Actor => User.Actor();
    [TempData] public string? Notice { get; set; }
    [TempData] public string? Problem { get; set; }
    protected Task<AuthorizationResult> CanManage() => HttpContext.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(User, Access.Manage);
    protected async Task<IActionResult> Change(Func<Task> work, string page, object? route = null, string notice = "Changes saved.")
    {
        if (!ModelState.IsValid) { Problem = "Check the submitted values and try again."; return RedirectToPage(page, route); }
        try { await work(); Notice = notice; }
        catch (InvalidOperationException e) { Problem = e.Message; }
        catch (PostgresException e) when (e.SqlState is "23503" or "23505" or "23514" or "23P01" or "P0001") { Problem = DbError(e); }
        return RedirectToPage(page, route);
    }
    public static string DbError(PostgresException e) => e.SqlState switch
    {
        "23P01" => "Those dates overlap an existing record. Review the history and use a different period.",
        "23505" => "That record already exists. Refresh the page and try again.",
        "23503" => "A selected record no longer exists or is still referenced by another record.",
        _ => "That change conflicts with a family or date rule. Check the people, relationships, and dates."
    };
}
