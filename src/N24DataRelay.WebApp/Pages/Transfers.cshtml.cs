using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Pages;

[Authorize]
public class TransfersModel : PageModel
{
    private readonly ITransferTracker _tracker;

    public TransfersModel(ITransferTracker tracker)
    {
        _tracker = tracker;
    }

    public IReadOnlyList<TransferStatusRecord> Recent { get; private set; } = Array.Empty<TransferStatusRecord>();

    public void OnGet()
    {
        var username = User.Identity?.Name;
        var all = _tracker.GetRecent(100);

        Recent = User.IsInRole("Admin")
            ? all
            : all.Where(r => string.Equals(r.UploadedBy, username, StringComparison.OrdinalIgnoreCase))
                 .ToList();
    }
}
