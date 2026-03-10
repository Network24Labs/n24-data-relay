using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages;

public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _configMonitor;
    private readonly IAuditLogger _audit;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptionsMonitor<N24DataRelayConfiguration> configMonitor,
        IAuditLogger audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configMonitor = configMonitor;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        var auth = _configMonitor.CurrentValue.WebPortal.Authentication;
        if (!auth.EnableLocalAccounts || !auth.EnableSelfRegistration)
            return RedirectToPage("/Login");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        var auth = _configMonitor.CurrentValue.WebPortal.Authentication;
        if (!auth.EnableLocalAccounts || !auth.EnableSelfRegistration)
            return RedirectToPage("/Login");

        if (!ModelState.IsValid)
            return Page();

        var authConfig = _configMonitor.CurrentValue.WebPortal.Authentication;
        var requireApproval = authConfig.RequireApproval;

        // First registered user is automatically approved and assigned the Admin role,
        // regardless of the RequireApproval setting.
        var isFirstUser = !_userManager.Users.Any();

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            RegistrationDate = DateTime.UtcNow,
            IsApproved = isFirstUser || !requireApproval,
            PasswordLastChangedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        _audit.Log(AuditEventTypes.UserRegistered, Input.Email, subject: Input.Email,
            details: new { isFirstUser, requireApproval = !isFirstUser && _configMonitor.CurrentValue.WebPortal.Authentication.RequireApproval },
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        if (isFirstUser)
        {
            await _userManager.AddToRoleAsync(user, "Admin");
            _audit.Log(AuditEventTypes.RoleGranted, "system", subject: Input.Email, details: new { role = "Admin" });
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        if (!requireApproval)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        // Approval required and not the first user — inform them and send to login page.
        TempData["InfoMessage"] = "Your account has been created and is awaiting approval by an administrator.";
        return RedirectToPage("/Login");
    }
}
