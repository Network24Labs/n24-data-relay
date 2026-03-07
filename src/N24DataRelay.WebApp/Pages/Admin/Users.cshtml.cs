using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;
    private readonly IAuditLogger _audit;

    public UsersModel(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptionsMonitor<N24DataRelayConfiguration> config,
        IAuditLogger audit)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _config = config;
        _audit = audit;
    }

    public List<UserViewModel> PendingUsers { get; set; } = new();
    public List<UserViewModel> AllUsers { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>A generated reset link shown to the admin after OnPostGenerateResetLinkAsync.</summary>
    [TempData]
    public string? ResetLink { get; set; }

    public class UserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedBy { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsCurrentUser { get; set; }
        public DateTime? PasswordLastChangedAt { get; set; }
        public bool MustChangePassword { get; set; }
        /// <summary>"ok" | "warning" | "expired" | "never"</summary>
        public string PasswordStatus { get; set; } = "never";
        public int? DaysUntilExpiry { get; set; }
    }

    public async Task OnGetAsync()
    {
        var currentUserId = _userManager.GetUserId(User);
        var expiryDays = _config.CurrentValue.WebPortal.Authentication.PasswordExpiryDays;
        var warningDays = _config.CurrentValue.WebPortal.Authentication.PasswordExpiryWarningDays;

        var users = _userManager.Users
            .OrderByDescending(u => u.RegistrationDate)
            .ToList();

        AllUsers = new();
        PendingUsers = new();

        foreach (var user in users)
        {
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            string pwStatus = "never";
            int? daysUntil = null;
            if (user.MustChangePassword)
            {
                pwStatus = "expired";
            }
            else if (expiryDays > 0 && user.PasswordLastChangedAt.HasValue)
            {
                var expiredAt = user.PasswordLastChangedAt.Value.AddDays(expiryDays);
                daysUntil = (int)Math.Ceiling((expiredAt - DateTime.UtcNow).TotalDays);
                if (daysUntil <= 0)
                    pwStatus = "expired";
                else if (warningDays > 0 && daysUntil <= warningDays)
                    pwStatus = "warning";
                else
                    pwStatus = "ok";
            }
            else if (expiryDays == 0)
            {
                pwStatus = "ok"; // expiry disabled globally
            }

            var vm = new UserViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? "(unknown)",
                IsApproved = user.IsApproved,
                RegistrationDate = user.RegistrationDate,
                ApprovedDate = user.ApprovedDate,
                ApprovedBy = user.ApprovedBy,
                IsAdmin = isAdmin,
                IsCurrentUser = user.Id == currentUserId,
                PasswordLastChangedAt = user.PasswordLastChangedAt,
                MustChangePassword = user.MustChangePassword,
                PasswordStatus = pwStatus,
                DaysUntilExpiry = daysUntil
            };
            AllUsers.Add(vm);
            if (!user.IsApproved)
                PendingUsers.Add(vm);
        }
    }

    public async Task<IActionResult> OnPostApproveAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        user.IsApproved = true;
        user.ApprovedDate = DateTime.UtcNow;
        user.ApprovedBy = User.Identity?.Name;
        await _userManager.UpdateAsync(user);

        _audit.Log(AuditEventTypes.UserApproved, User.Identity?.Name, subject: user.Email);
        StatusMessage = $"User {user.Email} has been approved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var email = user.Email ?? user.UserName;
        await _userManager.DeleteAsync(user);

        _audit.Log(AuditEventTypes.UserRejected, User.Identity?.Name, subject: email);
        StatusMessage = $"User {email} has been removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(string userId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (userId == currentUserId)
        {
            StatusMessage = "You cannot modify your own admin role.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            await _userManager.RemoveFromRoleAsync(user, "Admin");
            _audit.Log(AuditEventTypes.RoleRevoked, User.Identity?.Name, subject: user.Email, details: new { role = "Admin" });
            StatusMessage = $"{user.Email} removed from Admin role.";
        }
        else
        {
            await _userManager.AddToRoleAsync(user, "Admin");
            _audit.Log(AuditEventTypes.RoleGranted, User.Identity?.Name, subject: user.Email, details: new { role = "Admin" });
            StatusMessage = $"{user.Email} added to Admin role.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string userId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (userId == currentUserId)
        {
            StatusMessage = "You cannot revoke your own account.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        user.IsApproved = false;
        await _userManager.UpdateAsync(user);

        _audit.Log(AuditEventTypes.UserRevoked, User.Identity?.Name, subject: user.Email);
        StatusMessage = $"Access revoked for {user.Email}.";
        return RedirectToPage();
    }

    /// <summary>
    /// Flags MustChangePassword and generates a one-time reset link for the admin to share manually.
    /// </summary>
    public async Task<IActionResult> OnPostForceResetAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        user.MustChangePassword = true;
        await _userManager.UpdateAsync(user);

        _audit.Log(AuditEventTypes.PasswordResetRequested, User.Identity?.Name, subject: user.Email,
            details: new { forced = true });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page(
            "/ResetPassword",
            pageHandler: null,
            values: new { token = encodedToken, email = user.Email },
            protocol: Request.Scheme)!;

        ResetLink = link;
        StatusMessage = $"Password reset required for {user.Email}. Copy the link below and share it with the user.";
        return RedirectToPage();
    }

    /// <summary>
    /// Generates a fresh reset link without flagging MustChangePassword — for use when
    /// the user has already requested a reset but needs the link resent manually.
    /// </summary>
    public async Task<IActionResult> OnPostGenerateResetLinkAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page(
            "/ResetPassword",
            pageHandler: null,
            values: new { token = encodedToken, email = user.Email },
            protocol: Request.Scheme)!;

        ResetLink = link;
        StatusMessage = $"Reset link generated for {user.Email}. Copy and send it to the user — it expires in 24 hours.";
        return RedirectToPage();
    }
}
