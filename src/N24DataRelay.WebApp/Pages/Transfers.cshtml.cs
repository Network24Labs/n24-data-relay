using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Pages;

[Authorize]
public class TransfersModel : PageModel
{
    private readonly ITransferTracker _tracker;
    private readonly IOptionsSnapshot<N24DataRelayConfiguration> _config;

    public TransfersModel(ITransferTracker tracker, IOptionsSnapshot<N24DataRelayConfiguration> config)
    {
        _tracker = tracker;
        _config = config;
    }

    public IReadOnlyList<TransferStatusRecord> Recent { get; private set; } = Array.Empty<TransferStatusRecord>();
    public string ScadaSideName => _config.Value.Branding.ScadaSideName;

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
