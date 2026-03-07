using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
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
                options.Password.RequiredLength = 6;
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

        services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddHostedService<TransferStatusBroadcaster>();

        services.AddRazorPages()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }
}
