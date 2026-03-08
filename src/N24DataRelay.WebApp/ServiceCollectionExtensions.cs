using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.OpenApi;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Controllers;
using N24DataRelay.WebApp.Data;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp;

/// <summary>Registers web app services (auth, upload, admin) for use in the host.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds web app services. Config is read from the given configuration.</summary>
    public static IServiceCollection AddWebAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        var sectionName = ApplicationConstants.Configuration.SectionName;
        services.Configure<N24DataRelayConfiguration>(configuration.GetSection(sectionName));

        // Data protection is configured in Program.cs (requires IWebHostEnvironment).
        // AddWebAppServices only registers the base service so IDataProtectionProvider is
        // resolvable; Program.cs adds the appropriate key storage for each environment.
        services.AddDataProtection().SetApplicationName("N24DataRelay");

        var authConfig = configuration
            .GetSection(sectionName)
            .GetSection("WebPortal:Authentication");

        var connectionString = authConfig["ConnectionString"] ?? "Data Source=n24datarelay.db";
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

        var maxUploadBytes = configuration
            .GetSection(sectionName)
            .GetSection("WebPortal")
            .GetValue<long>("MaxFileSizeBytes", 5L * 1024 * 1024 * 1024);
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(
            options => options.MultipartBodyLengthLimit = maxUploadBytes);

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Login";
            options.LogoutPath = "/Logout";
            options.AccessDeniedPath = "/AccessDenied";
        });

        // Entra ID (Azure AD) OIDC — wired when EnableEntraId = true in config.
        // Requires an "AzureAd" section with TenantId, ClientId, and ClientSecret.
        var enableEntraId = authConfig.GetValue<bool>("EnableEntraId");
        if (enableEntraId)
        {
            services.AddAuthentication()
                .AddMicrosoftIdentityWebApp(
                    configuration.GetSection("AzureAd"),
                    openIdConnectScheme: "MicrosoftIdentity",
                    cookieScheme: null);

            // Route the OIDC callback through Identity's external login flow
            // so we can create/link users and apply the approval workflow.
            services.Configure<OpenIdConnectOptions>("MicrosoftIdentity", opts =>
                opts.SignInScheme = IdentityConstants.ExternalScheme);
        }

        services.AddScoped<FileUploadService>();
        services.AddSingleton<ConfigWriterService>();
        // Email sender: uses SMTP when enabled+configured, falls back to log-only stub.
        services.AddTransient<LoggingEmailSender>();
        services.AddTransient<SmtpEmailSender>();
        services.AddTransient<IEmailSender>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptionsMonitor<N24DataRelayConfiguration>>().CurrentValue;
            if (cfg.Smtp.Enabled && !string.IsNullOrWhiteSpace(cfg.Smtp.Host))
                return sp.GetRequiredService<SmtpEmailSender>();
            return sp.GetRequiredService<LoggingEmailSender>();
        });

        // SQLite-backed tracker: persists records across restarts (IHostedService for startup load).
        services.AddSingleton<SqliteTransferTracker>();
        services.AddSingleton<ITransferTracker>(sp => sp.GetRequiredService<SqliteTransferTracker>());
        services.AddHostedService(sp => sp.GetRequiredService<SqliteTransferTracker>());

        // SQLite-backed audit logger.
        services.AddSingleton<SqliteAuditLogger>();
        services.AddSingleton<IAuditLogger>(sp => sp.GetRequiredService<SqliteAuditLogger>());
        services.AddHostedService(sp => sp.GetRequiredService<SqliteAuditLogger>());

        services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddHostedService<TransferStatusBroadcaster>();

        services.AddRazorPages()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        services.AddControllers()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        // ── Swagger / OpenAPI ────────────────────────────────────────────────
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title       = "N24 Data Relay — Monitoring API",
                Version     = "v1",
                Description = """
                    Polling API for integration with monitoring tools such as CrowdStrike Next-Gen SIEM,
                    LogScale, Grafana, and Splunk.

                    ---

                    ## How to authenticate

                    The `/transfers` and `/audit` endpoints require a **Bearer API key**.
                    The `/health` endpoint is public (no key needed).

                    ### Step 1 — set an API key

                    Go to **Admin → Settings → Web Portal tab → Monitoring API Key**, enter a long
                    random string (32+ characters), and click **Save Portal Settings**.

                    Alternatively set the environment variable:
                    ```
                    N24DataRelay__WebPortal__Authentication__ApiKey=your-key-here
                    ```

                    ### Step 2 — authorise in this UI

                    Click the **Authorize 🔒** button (top-right of this page), paste your key into
                    the **Value** field exactly as-is (no `Bearer ` prefix — Swagger adds that
                    automatically for HTTP Bearer schemes), then click **Authorize**.

                    ### Step 3 — try an endpoint

                    Expand any endpoint, click **Try it out**, then **Execute**.

                    ---

                    ## Using the key from a script or collector

                    Pass the key in the `Authorization` header:

                    ```
                    curl -H "Authorization: Bearer YOUR_KEY" http://localhost:5000/api/v1/health
                    curl -H "Authorization: Bearer YOUR_KEY" "http://localhost:5000/api/v1/transfers?limit=50"
                    ```

                    For CrowdStrike / LogScale Collector YAML config see
                    **Admin → Settings → Web Portal tab → Monitoring Integration Reference**.
                    """
            });

            // Declare the Bearer API-key security scheme.
            // Per-endpoint requirements are added by BearerSecurityOperationFilter below.
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type        = SecuritySchemeType.Http,
                Scheme      = "bearer",
                In          = ParameterLocation.Header,
                Name        = "Authorization",
                Description = "Paste your monitoring API key: `Bearer <your-api-key>`"
            });

            // Apply lock icon to all endpoints except those with [AllowAnonymous]
            c.OperationFilter<BearerSecurityOperationFilter>();

            // Enable Swashbuckle annotation attributes
            c.EnableAnnotations();

            // Feed XML doc comments (summaries, param descriptions, response codes)
            var xmlPath = Path.Combine(AppContext.BaseDirectory,
                $"{Assembly.GetAssembly(typeof(ServiceCollectionExtensions))!.GetName().Name}.xml");
            if (File.Exists(xmlPath))
                c.IncludeXmlComments(xmlPath);
        });

        return services;
    }
}
