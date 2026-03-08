using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Pages;

public class IndexModel : PageModel
{
    private readonly IOptionsSnapshot<N24DataRelayConfiguration> _config;

    public IndexModel(IOptionsSnapshot<N24DataRelayConfiguration> config)
    {
        _config = config;
    }

    public N24DataRelayConfiguration Config => _config.Value;

    public bool ShowEntraId       => Config.WebPortal.Authentication.EnableEntraId;
    public bool ShowLocalAccounts => Config.WebPortal.Authentication.EnableLocalAccounts;
    public bool IsAuthenticated   => User.Identity?.IsAuthenticated == true;

    public string FormattedMaxFileSize => FormatBytes(Config.WebPortal.MaxFileSizeBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (1.0 * (1L << 30)):F1} GB";
        if (bytes >= 1 << 20)  return $"{bytes / (1.0 * (1 << 20)):F1} MB";
        if (bytes >= 1 << 10)  return $"{bytes / (1.0 * (1 << 10)):F1} KB";
        return $"{bytes} B";
    }

    public void OnGet() { }
}
