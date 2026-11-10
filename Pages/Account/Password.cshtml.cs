using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Pillar.Pages.Account;
[Authorize(Policy="Attend")]
public class PasswordModel(UserManager<StaffUser> users,SignInManager<StaffUser> signIn,AuthDb auth):AppPage
{
    [BindProperty,Required,StringLength(256),DataType(DataType.Password),Display(Name="Current password")]public string CurrentPassword{get;set;}="";
    [BindProperty,Required,StringLength(256,MinimumLength=14),DataType(DataType.Password),Display(Name="New password")]public string NewPassword{get;set;}="";
    [BindProperty,Required,Compare(nameof(NewPassword)),DataType(DataType.Password),Display(Name="Confirm new password")]public string ConfirmPassword{get;set;}="";
    public async Task<IActionResult> OnPostAsync()
    {
        if(!ModelState.IsValid)return Page();
        var user=(await users.GetUserAsync(User))!;
        await using var tx=await auth.Database.BeginTransactionAsync();
        var result=await users.ChangePasswordAsync(user,CurrentPassword,NewPassword);
        if(!result.Succeeded){foreach(var error in result.Errors)ModelState.AddModelError("",error.Description);return Page();}
        await auth.Database.GetDbConnection().ExecuteAsync("INSERT INTO pillar.audit_events(actor_user_id,action,entity_type,entity_id,correlation_id) VALUES(@actor,'staff.password_changed','app_users',@actor,@correlation)",new{actor=Actor,correlation=Guid.NewGuid()},tx.GetDbTransaction());
        await tx.CommitAsync();
        await signIn.SignOutAsync();
        Notice="Password updated. Sign in with your new password.";
        return RedirectToPage("Login");
    }
}
