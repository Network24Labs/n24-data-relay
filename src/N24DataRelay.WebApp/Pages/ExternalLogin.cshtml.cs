using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Pages;

/// <summary>
/// Handles the Entra ID (Azure AD) external login flow.
/// GET /ExternalLogin?provider=MicrosoftIdentity  → issues OIDC challenge
/// GET /ExternalLogin?handler=Callback            → processes the callback from Azure AD
/// </summary>
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("/ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");

        if (remoteError != null)
        {
            _logger.LogWarning("External login error from provider: {Error}", remoteError);
            TempData["InfoMessage"] = $"Sign-in error: {remoteError}";
            return RedirectToPage("/Login");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            _logger.LogWarning("Could not retrieve external login info.");
            TempData["InfoMessage"] = "External sign-in failed. Please try again.";
            return RedirectToPage("/Login");
        }

        // Try to sign in with an existing external login link.
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (result.Succeeded)
        {
            // Directory-backed users are auto-approved; upgrade any legacy unapproved records.
            var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existingUser != null && !existingUser.IsApproved)
            {
                existingUser.IsApproved = true;
                existingUser.ApprovedDate = DateTime.UtcNow;
                existingUser.ApprovedBy = "Entra ID (auto)";
                await _userManager.UpdateAsync(existingUser);
            }
            return LocalRedirect(returnUrl);
        }

        if (result.IsLockedOut)
        {
            TempData["InfoMessage"] = "Account locked. Try again later.";
            return RedirectToPage("/Login");
        }

        // No existing external login — create a new local user from the external claims.
        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? info.Principal.FindFirst("preferred_username")?.Value
            ?? info.Principal.FindFirst("email")?.Value;

        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("No email claim in external login from {Provider}", info.LoginProvider);
            TempData["InfoMessage"] = "Sign-in failed: no email address was returned by the identity provider.";
            return RedirectToPage("/Login");
        }

        // If a local account with the same email already exists, link the external login to it.
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            var isFirstUser = !_userManager.Users.Any();
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                RegistrationDate = DateTime.UtcNow,
                IsApproved = true
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                _logger.LogError("Failed to create user from external login: {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
                TempData["InfoMessage"] = "Failed to create account. Please contact an administrator.";
                return RedirectToPage("/Login");
            }

            if (isFirstUser)
                await _userManager.AddToRoleAsync(user, "Admin");
        }

        await _userManager.AddLoginAsync(user, info);

        // Directory-backed users are auto-approved; upgrade any legacy unapproved records.
        if (!user.IsApproved)
        {
            user.IsApproved = true;
            user.ApprovedDate = DateTime.UtcNow;
            user.ApprovedBy = "Entra ID (auto)";
            await _userManager.UpdateAsync(user);
        }

        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return LocalRedirect(returnUrl);
    }
}
