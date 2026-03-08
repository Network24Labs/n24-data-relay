using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages;

[Authorize]
[RequestFormLimits(MultipartBodyLengthLimit = 5_368_709_120)] // 5 GB default
[RequestSizeLimit(5_368_709_120)]
public class UploadModel : PageModel
{
    private readonly FileUploadService _uploadService;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _configMonitor;

    public UploadModel(FileUploadService uploadService, IOptionsMonitor<N24DataRelayConfiguration> configMonitor)
    {
        _uploadService = uploadService;
        _configMonitor = configMonitor;
    }

    [BindProperty]
    public bool RequiresTransfer { get; set; } = true;

    [BindProperty]
    public string? Notes { get; set; }

    public bool ShowTransferOption => _configMonitor.CurrentValue.WebPortal.EnableUploadToTransfer;
    public string MaxFileSizeBytesDisplay => FormatBytes(_configMonitor.CurrentValue.WebPortal.MaxFileSizeBytes);
    public string BlockedExtensionsDisplay => string.Join(", ", _configMonitor.CurrentValue.WebPortal.BlockedFileExtensions ?? new List<string>());
    public string DmzSideName => _configMonitor.CurrentValue.Branding.DmzSideName;
    public string ScadaSideName => _configMonitor.CurrentValue.Branding.ScadaSideName;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFileCollection? files)
    {
        if (files == null || files.Count == 0)
            return new JsonResult(new { error = "Please select at least one file." }) { StatusCode = 400 };

        var user = User.Identity?.Name ?? "unknown";
        var results = await _uploadService.UploadFormFilesAsync(files.ToList(), user, RequiresTransfer, Notes).ConfigureAwait(false);
        return new JsonResult(results);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (1.0 * (1L << 30)):F1} GB";
        if (bytes >= 1 << 20) return $"{bytes / (1.0 * (1 << 20)):F1} MB";
        if (bytes >= 1 << 10) return $"{bytes / (1.0 * (1 << 10)):F1} KB";
        return $"{bytes} B";
    }
}
