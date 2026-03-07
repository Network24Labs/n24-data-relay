using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        var connectionString = configuration.GetSection(sectionName).GetSection("WebPortal:Authentication")["ConnectionString"]
            ?? "Data Source=n24datarelay.db";
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

        var maxUploadBytes = configuration.GetSection(sectionName).GetSection("WebPortal").GetValue<long>("MaxFileSizeBytes", 5L * 1024 * 1024 * 1024);
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);

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

        services.AddScoped<FileUploadService>();
        services.AddSingleton<ITransferTracker, InMemoryTransferTracker>();
        services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddHostedService<TransferStatusBroadcaster>();

        services.AddRazorPages()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }
}
