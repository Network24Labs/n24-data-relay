using Microsoft.AspNetCore.DataProtection;
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

// In Production, persist data-protection keys to a dedicated directory protected by
// filesystem permissions (chmod 700, owned by the service user).
// In Development, use ephemeral (in-memory) keys — no file persistence, no framework warning.
if (!builder.Environment.IsDevelopment())
{
    var keysPath = builder.Configuration
        .GetSection(ApplicationConstants.Configuration.SectionName)
        .GetSection("Paths")["ConfigDirectory"]
        ?? ApplicationConstants.Configuration.DefaultConfigDirectory;
    builder.Services
        .AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(keysPath, "keys")))
        .SetApplicationName("N24DataRelay");
}

// Apply Kestrel HTTPS/HTTP port settings from N24DataRelay:WebPortal:Kestrel config.
// In Development, leave Kestrel alone — launchSettings.json / ASPNETCORE_URLS controls the port.
if (!builder.Environment.IsDevelopment())
{
    var kestrelSection = builder.Configuration
        .GetSection(ApplicationConstants.Configuration.SectionName)
        .GetSection("WebPortal:Kestrel");
    var httpPort     = kestrelSection.GetValue<int>("HttpPort",     8080);
    var httpsPort    = kestrelSection.GetValue<int>("HttpsPort",    8443);
    var enableHttps  = kestrelSection.GetValue<bool>("EnableHttps", false);
    var certPath     = kestrelSection["CertificatePath"];
    var certPassword = Environment.GetEnvironmentVariable("N24_CERT_PASSWORD");

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(httpPort);
        if (enableHttps && !string.IsNullOrEmpty(certPath) && File.Exists(certPath))
        {
            options.ListenAnyIP(httpsPort, listenOptions =>
            {
                if (!string.IsNullOrEmpty(certPassword))
                    listenOptions.UseHttps(certPath, certPassword);
                else
                    listenOptions.UseHttps(certPath);
            });
        }
    });
}

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

    // Additive column migrations — only ALTER if the column doesn't already exist,
    // so startup logs stay clean on every run after the first.
    // Table and column names are all compile-time literals — no injection risk.
#pragma warning disable EF1002
    static bool ColumnExists(ApplicationDbContext ctx, string table, string column)
    {
        var count = ctx.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'")
            .AsEnumerable().FirstOrDefault();
        return count > 0;
    }

    static void AddColumnIfMissing(ApplicationDbContext ctx, string table, string column, string columnDef)
    {
        if (!ColumnExists(ctx, table, column))
            ctx.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {columnDef}");
    }
#pragma warning restore EF1002

    // AspNetUsers — password lifecycle columns (Phase 2)
    AddColumnIfMissing(db, "AspNetUsers", "PasswordLastChangedAt", "TEXT");
    AddColumnIfMissing(db, "AspNetUsers", "MustChangePassword",    "INTEGER NOT NULL DEFAULT 0");

    // TransferRecords — observability columns (Phase 3)
    AddColumnIfMissing(db, "TransferRecords", "RetryCount",           "INTEGER NOT NULL DEFAULT 0");
    AddColumnIfMissing(db, "TransferRecords", "ThroughputBytesPerSec","REAL");
    AddColumnIfMissing(db, "TransferRecords", "Verified",             "INTEGER NOT NULL DEFAULT 0");
    AddColumnIfMissing(db, "TransferRecords", "ErrorDetails",         "TEXT");
    AddColumnIfMissing(db, "TransferRecords", "TransferMethod",       "TEXT");
    AddColumnIfMissing(db, "TransferRecords", "RemoteHost",           "TEXT");

    // Phase 3: AuditEvents table.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "AuditEvents" (
            "Id"        TEXT NOT NULL CONSTRAINT "PK_AuditEvents" PRIMARY KEY,
            "Timestamp" TEXT NOT NULL,
            "EventType" TEXT NOT NULL,
            "Actor"     TEXT,
            "Subject"   TEXT,
            "Details"   TEXT,
            "IpAddress" TEXT
        )
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_AuditEvents_Timestamp"
        ON "AuditEvents" ("Timestamp")
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_AuditEvents_EventType"
        ON "AuditEvents" ("EventType")
        """);

    // Seed the Admin role so it is available for the first registered user.
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
}

app.UseWebApp();

// Warn if the production config file is world-readable (credentials at rest would be exposed).
CheckConfigFilePermissions(app.Logger, app.Environment);

await app.RunAsync();

static void CheckConfigFilePermissions(ILogger logger, IWebHostEnvironment env)
{
    if (env.IsDevelopment()) return;

    try
    {
        var configPath = ApplicationConstants.Configuration.SharedConfigPath;
        if (!File.Exists(configPath)) return;

        var info = new FileInfo(configPath);
        // UnixFileMode is available on .NET 7+; permissions are only meaningful on non-Windows.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = (int)info.UnixFileMode;
            // World-readable: mode & 0o004 != 0
            if ((mode & 0b000_000_100) != 0)
            {
                logger.LogWarning(
                    "CONFIG SECURITY WARNING: {Path} is world-readable (octal mode {Mode}). " +
                    "Restrict with: chmod 640 <path> && chown root:n24-data-relay <path> (see path above)",
                    configPath, Convert.ToString(mode, 8));
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "Could not check config file permissions.");
    }
}
