using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace N24DataRelay.WebApp.Pages;

public class AboutModel : PageModel
{
    public string AppName { get; } = "N24 Data Relay";
    public string Version { get; }
    public string? BuildDate { get; }
    public string LicenseNotice { get; } = "This software is licensed under the GNU Lesser General Public License v3.0 (LGPL-3.0).";

    public AboutModel()
    {
        var entry = Assembly.GetEntryAssembly();
        var version = entry?.GetName().Version;
        Version = version?.ToString(3) ?? version?.ToString() ?? "0.2.0";

        var location = entry?.Location;
        if (!string.IsNullOrEmpty(location) && System.IO.File.Exists(location))
            BuildDate = new System.IO.FileInfo(location).LastWriteTime.ToString("MMMM dd, yyyy");
        else
            BuildDate = null;
    }

    public void OnGet()
    {
    }
}
