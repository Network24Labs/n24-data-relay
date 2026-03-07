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
        Recent = _tracker.GetRecent(50);
    }
}
