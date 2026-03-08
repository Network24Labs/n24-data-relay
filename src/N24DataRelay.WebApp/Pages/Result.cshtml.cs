using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Pages;

public class ResultModel : PageModel
{
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;

    public ResultModel(IOptionsMonitor<N24DataRelayConfiguration> config)
    {
        _config = config;
    }

    public List<UploadResult> Results { get; set; } = new();
    public bool HasTransferUploads => Results.Any(r => r.Success && r.RequiresTransfer);
    public string DmzSideName => _config.CurrentValue.Branding.DmzSideName;

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (1.0 * (1L << 30)):F1} GB";
        if (bytes >= 1 << 20) return $"{bytes / (1.0 * (1 << 20)):F1} MB";
        if (bytes >= 1 << 10) return $"{bytes / (1.0 * (1 << 10)):F1} KB";
        return $"{bytes} B";
    }

    public void OnGet()
    {
        var json = TempData["UploadResults"] as string;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<UploadResult>>(json);
                if (list != null)
                    Results = list;
            }
            catch { /* ignore */ }
        }
    }
}
