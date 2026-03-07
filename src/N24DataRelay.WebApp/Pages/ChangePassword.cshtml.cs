using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Pages;

[Authorize]
public class ChangePasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ChangePasswordModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Set when the user is being forced to change an expired/required-reset password.</summary>
    public bool IsForced { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(bool forced = false)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        IsForced = forced || user.MustChangePassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(bool forced = false)
    {
        if (!ModelState.IsValid)
        {
            IsForced = forced;
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _userManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            IsForced = forced;
            return Page();
        }

        user.PasswordLastChangedAt = DateTime.UtcNow;
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);

        // Refresh the auth cookie so the session stays valid after the password change
        await _signInManager.RefreshSignInAsync(user);

        StatusMessage = "Your password has been changed successfully.";
        return RedirectToPage("/Index");
    }
}
