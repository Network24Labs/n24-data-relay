using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Pages;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _configMonitor;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IOptionsMonitor<N24DataRelayConfiguration> configMonitor)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _configMonitor = configMonitor;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? InfoMessage { get; set; }

    public bool EnableEntraId => _configMonitor.CurrentValue.WebPortal.Authentication.EnableEntraId;
    public bool EnableLocalAccounts => _configMonitor.CurrentValue.WebPortal.Authentication.EnableLocalAccounts;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        if (!EnableLocalAccounts)
        {
            ErrorMessage = "Local account sign-in is not enabled.";
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Enforce the approval gate — sign out immediately if not yet approved.
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null && !user.IsApproved)
            {
                await _signInManager.SignOutAsync();
                ErrorMessage = "Your account is pending approval by an administrator.";
                return Page();
            }
            return LocalRedirect(returnUrl);
        }

        if (result.IsLockedOut)
            ErrorMessage = "Account locked. Try again later.";
        else
            ErrorMessage = "Invalid email or password.";

        return Page();
    }
}
