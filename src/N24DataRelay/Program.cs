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
    foreach (var dir in new[] { "data", "data/uploads", "data/uploads/transfer", "data/archive", "data/logs", "data/temp" })
    {
        Directory.CreateDirectory(dir);
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.UseWebApp();

await app.RunAsync();
