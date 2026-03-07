using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages;

[Authorize]
public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditLogger _audit;

    public LogoutModel(SignInManager<ApplicationUser> signInManager, IAuditLogger audit)
    {
        _signInManager = signInManager;
        _audit = audit;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var actor = User.Identity?.Name;
        await _signInManager.SignOutAsync().ConfigureAwait(false);
        _audit.Log(AuditEventTypes.UserLogout, actor, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
        return RedirectToPage("/Index");
    }
}
