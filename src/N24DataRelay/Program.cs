using Microsoft.EntityFrameworkCore;
using N24DataRelay.Core.Constants;
using N24DataRelay.Watcher;
using N24DataRelay.WebApp;
using N24DataRelay.WebApp.Data;

var builder = WebApplication.CreateBuilder(args);

var sharedConfigPath = ApplicationConstants.Configuration.SharedConfigPath;
if (File.Exists(sharedConfigPath))
{
    builder.Configuration.AddJsonFile(sharedConfigPath, optional: true, reloadOnChange: true);
}

builder.Services.AddWatcherServices(builder.Configuration);
builder.Services.AddWebAppServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Use content root (host project dir) so "data" lives in one place regardless of process CWD
    var baseDir = app.Environment.ContentRootPath;
    foreach (var rel in new[] { "data", "data/uploads", "data/uploads/transfer", "data/archive", "data/logs", "data/temp" })
    {
        Directory.CreateDirectory(Path.Combine(baseDir, rel));
    }
    // Resolve relative paths (e.g. config "data/uploads/transfer") from content root
    Environment.CurrentDirectory = baseDir;
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.UseWebApp();

await app.RunAsync();
