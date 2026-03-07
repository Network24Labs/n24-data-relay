using Microsoft.AspNetCore.Mvc.RazorPages;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Pages;

public class ResultModel : PageModel
{
    public List<UploadResult> Results { get; set; } = new();

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
