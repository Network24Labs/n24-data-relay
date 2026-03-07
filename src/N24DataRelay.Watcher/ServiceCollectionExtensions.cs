using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Services;

namespace N24DataRelay.Watcher;

/// <summary>Registers watcher (file watch + transfer) services for use in the host.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the watcher hosted service and its dependencies. Config is read from the given configuration.</summary>
    public static IServiceCollection AddWatcherServices(this IServiceCollection services, IConfiguration configuration)
    {
        var sectionName = ApplicationConstants.Configuration.SectionName;
        services.Configure<N24DataRelay.Core.Models.N24DataRelayConfiguration>(
            configuration.GetSection(sectionName));

        services.AddSingleton<ICredentialProvider, ConfigOrEnvCredentialProvider>();
        services.AddSingleton<IFileWatcher, FileWatcher>();
        services.AddSingleton<IFileQueue, FileQueue>();
        services.AddSingleton<IFileTransferServiceFactory, FileTransferServiceFactory>();
        services.AddHostedService<TransferWorker>();

        return services;
    }
}
