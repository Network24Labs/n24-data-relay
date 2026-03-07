using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages;

public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogger _audit;

    public ResetPasswordModel(UserManager<ApplicationUser> userManager, IAuditLogger audit)
    {
        _userManager = userManager;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public bool Succeeded { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? token, string? email)
    {
        if (token == null || email == null)
            return RedirectToPage("/Login");

        Input = new InputModel
        {
            Email = email,
            Token = token
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            // Don't reveal whether the user exists
            Succeeded = true;
            return Page();
        }

        byte[] tokenBytes;
        try { tokenBytes = WebEncoders.Base64UrlDecode(Input.Token); }
        catch { ErrorMessage = "The reset link is invalid or has expired."; return Page(); }

        var decodedToken = Encoding.UTF8.GetString(tokenBytes);
        var result = await _userManager.ResetPasswordAsync(user, decodedToken, Input.Password);

        if (result.Succeeded)
        {
            user.PasswordLastChangedAt = DateTime.UtcNow;
            user.MustChangePassword = false;
            await _userManager.UpdateAsync(user);
            _audit.Log(AuditEventTypes.PasswordReset, Input.Email, subject: Input.Email,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
            Succeeded = true;
            return Page();
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }
}
