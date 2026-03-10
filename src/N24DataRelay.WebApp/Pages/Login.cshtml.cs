using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _configMonitor;
    private readonly LdapAuthenticationService _ldapService;
    private readonly IAuditLogger _audit;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IOptionsMonitor<N24DataRelayConfiguration> configMonitor,
        LdapAuthenticationService ldapService,
        IAuditLogger audit)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _configMonitor = configMonitor;
        _ldapService = ldapService;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public LdapInputModel LdapInput { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LdapErrorMessage { get; set; }

    [TempData]
    public string? InfoMessage { get; set; }

    private AuthenticationSettings AuthSettings => _configMonitor.CurrentValue.WebPortal.Authentication;
    public bool EnableEntraId => AuthSettings.EnableEntraId;
    public bool EnableLocalAccounts => AuthSettings.EnableLocalAccounts;
    public bool EnableSelfRegistration => AuthSettings.EnableSelfRegistration;
    public bool EnableLdap => AuthSettings.EnableLdap;
    public string DefaultLoginMethod => AuthSettings.DefaultLoginMethod;
    public string LdapDomainHint => AuthSettings.Ldap.DomainHint;

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

    public class LdapInputModel
    {
        [Required(ErrorMessage = "Username is required.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        ReturnUrl = returnUrl;

        if (!EnableLocalAccounts)
        {
            ErrorMessage = "Local account sign-in is not enabled.";
            return Page();
        }

        KeepOnlyModelState("Input");
        if (!ModelState.IsValid)
            return Page();

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(Input.Email);

            if (user != null && !user.IsApproved)
            {
                await _signInManager.SignOutAsync();
                ErrorMessage = "Your account is pending approval by an administrator.";
                return Page();
            }

            if (user != null)
            {
                _audit.Log(AuditEventTypes.UserLogin, Input.Email, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

                if (user.MustChangePassword)
                    return RedirectToPage("/ChangePassword", new { forced = true });

                var expiryDays = AuthSettings.PasswordExpiryDays;
                if (expiryDays > 0 && user.PasswordLastChangedAt.HasValue)
                {
                    var expiredAt = user.PasswordLastChangedAt.Value.AddDays(expiryDays);
                    if (DateTime.UtcNow >= expiredAt)
                        return RedirectToPage("/ChangePassword", new { forced = true });
                }
            }

            return LocalRedirect(returnUrl);
        }

        _audit.Log(AuditEventTypes.UserLoginFailed, Input.Email, details: new { result.IsLockedOut }, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        if (result.IsLockedOut)
            ErrorMessage = "Account locked. Try again later.";
        else
            ErrorMessage = "Invalid email or password.";

        return Page();
    }

    public async Task<IActionResult> OnPostLdapAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        ReturnUrl = returnUrl;

        if (!EnableLdap)
        {
            LdapErrorMessage = "AD/LDAP sign-in is not enabled.";
            return Page();
        }

        KeepOnlyModelState("LdapInput");
        if (!ModelState.IsValid)
            return Page();

        var ldapResult = await _ldapService.AuthenticateAsync(LdapInput.Username, LdapInput.Password);
        if (!ldapResult.Success || ldapResult.User == null)
        {
            _audit.Log(AuditEventTypes.UserLoginFailed, LdapInput.Username,
                details: new { method = "LDAP", reason = ldapResult.ErrorMessage },
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
            LdapErrorMessage = ldapResult.ErrorMessage ?? "AD/LDAP sign-in failed.";
            return Page();
        }

        var ldapUser = ldapResult.User;
        var user = await _userManager.FindByEmailAsync(ldapUser.Email);
        var isFirstUser = user == null && !_userManager.Users.Any();

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = ldapUser.Email,
                Email = ldapUser.Email,
                EmailConfirmed = true,
                RegistrationDate = DateTime.UtcNow,
                IsApproved = true
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                LdapErrorMessage = "Failed to create account. Please contact an administrator.";
                return Page();
            }

            if (isFirstUser)
                await _userManager.AddToRoleAsync(user, "Admin");
        }

        // Link the LDAP external login if not already linked
        var existingLogins = await _userManager.GetLoginsAsync(user);
        if (!existingLogins.Any(l => l.LoginProvider == "LDAP" && l.ProviderKey == ldapUser.Dn))
            await _userManager.AddLoginAsync(user, new UserLoginInfo("LDAP", ldapUser.Dn, "AD/LDAP"));

        // Directory users are auto-approved
        if (!user.IsApproved)
        {
            user.IsApproved = true;
            user.ApprovedDate = DateTime.UtcNow;
            user.ApprovedBy = "LDAP (auto)";
            await _userManager.UpdateAsync(user);
        }

        _audit.Log(AuditEventTypes.UserLogin, ldapUser.Email,
            details: new { method = "LDAP", dn = ldapUser.Dn },
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        await _signInManager.SignInAsync(user, isPersistent: false, "LDAP");
        return LocalRedirect(returnUrl);
    }

    /// <summary>Remove all model-state entries except those belonging to the given bound-property prefix.</summary>
    private void KeepOnlyModelState(string prefix)
    {
        foreach (var key in ModelState.Keys.ToList())
            if (!key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase) && key != "")
                ModelState.Remove(key);
    }
}
