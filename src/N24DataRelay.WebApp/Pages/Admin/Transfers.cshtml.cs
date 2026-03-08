using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AdminTransfersModel : PageModel
{
    private readonly ITransferTracker _tracker;
    private readonly IOptionsSnapshot<N24DataRelayConfiguration> _config;

    public AdminTransfersModel(ITransferTracker tracker, IOptionsSnapshot<N24DataRelayConfiguration> config)
    {
        _tracker = tracker;
        _config = config;
    }

    public IReadOnlyList<TransferStatusRecord> Recent { get; private set; } = Array.Empty<TransferStatusRecord>();
    public string ScadaSideName => _config.Value.Branding.ScadaSideName;

    public void OnGet()
    {
        Recent = _tracker.GetRecent(500);
    }
}
