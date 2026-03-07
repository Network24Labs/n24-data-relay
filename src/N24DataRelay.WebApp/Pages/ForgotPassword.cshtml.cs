using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogger _audit;

    public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender, IAuditLogger audit)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Submitted { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);

        // Always show the same response regardless of whether the account exists
        // (prevents user-enumeration).
        if (user != null && user.IsApproved)
        {
            _audit.Log(AuditEventTypes.PasswordResetRequested, Input.Email, subject: Input.Email,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var resetLink = Url.Page(
                "/ResetPassword",
                pageHandler: null,
                values: new { token = encodedToken, email = Input.Email },
                protocol: Request.Scheme)!;

            await _emailSender.SendAsync(
                to: Input.Email,
                subject: "N24 Data Relay — Reset your password",
                htmlBody: $"""
                    <p>You requested a password reset for your N24 Data Relay account.</p>
                    <p><a href="{HtmlEncoder.Default.Encode(resetLink)}">Click here to reset your password</a></p>
                    <p>This link expires in 24 hours. If you did not request a reset, ignore this email.</p>
                    <p>Direct link: {HtmlEncoder.Default.Encode(resetLink)}</p>
                    """);
        }

        Submitted = true;
        return Page();
    }
}
