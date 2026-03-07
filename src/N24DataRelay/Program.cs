using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using N24DataRelay.Core.Constants;
using N24DataRelay.Watcher;
using N24DataRelay.WebApp;
using N24DataRelay.WebApp.Data;

var builder = WebApplication.CreateBuilder(args);

// Integrate with systemd (sd-notify ready/stopping signals); no-op outside systemd.
builder.Host.UseSystemd();

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

    // EnsureCreated creates all tables for a new database.
    // For existing databases it is a no-op, so we apply any additive schema changes
    // below using CREATE TABLE/INDEX IF NOT EXISTS (safe to re-run on every startup).
    db.Database.EnsureCreated();

    // Phase 2: TransferRecords table (added after initial Identity-only schema).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TransferRecords" (
            "Id"                TEXT NOT NULL CONSTRAINT "PK_TransferRecords" PRIMARY KEY,
            "FileName"          TEXT NOT NULL,
            "SourcePath"        TEXT NOT NULL,
            "UploadedBy"        TEXT NOT NULL,
            "Status"            TEXT NOT NULL,
            "QueuedAt"          TEXT NOT NULL,
            "TransferStartedAt" TEXT,
            "CompletedAt"       TEXT,
            "ErrorMessage"      TEXT,
            "FileSize"          INTEGER,
            "DestinationPath"   TEXT
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_TransferRecords_QueuedAt"
        ON "TransferRecords" ("QueuedAt")
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_TransferRecords_SourcePath"
        ON "TransferRecords" ("SourcePath")
        """);

    // Additive columns on AspNetUsers (ALTER TABLE errors on duplicate columns are silently ignored).
    try { db.Database.ExecuteSqlRaw("ALTER TABLE \"AspNetUsers\" ADD COLUMN \"PasswordLastChangedAt\" TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE \"AspNetUsers\" ADD COLUMN \"MustChangePassword\" INTEGER NOT NULL DEFAULT 0"); } catch { }

    // Seed the Admin role so it is available for the first registered user.
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
}

app.UseWebApp();

await app.RunAsync();
