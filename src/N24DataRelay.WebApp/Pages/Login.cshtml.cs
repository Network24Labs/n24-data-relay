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
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _configMonitor;

    public LoginModel(SignInManager<ApplicationUser> signInManager, IOptionsMonitor<N24DataRelayConfiguration> configMonitor)
    {
        _signInManager = signInManager;
        _configMonitor = configMonitor;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

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
        if (!_configMonitor.CurrentValue.WebPortal.Authentication.EnableLocalAccounts)
        {
            ErrorMessage = "Local account sign-in is not enabled.";
            return Page();
        }
        if (ModelState.IsValid)
        {
            var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
                return LocalRedirect(returnUrl);
            if (result.IsLockedOut)
                ErrorMessage = "Account locked. Try again later.";
            else
                ErrorMessage = "Invalid email or password.";
        }
        return Page();
    }
}
