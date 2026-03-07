using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.Watcher;

/// <summary>Creates IFileTransferService from current config (SSH/SCP for Phase 2).</summary>
public interface IFileTransferServiceFactory
{
    IFileTransferService CreateTransferService();
}

public sealed class FileTransferServiceFactory : IFileTransferServiceFactory
{
    private readonly ILogger<ScpFileTransferService> _logger;
    private readonly N24DataRelayConfiguration _config;
    private readonly ICredentialProvider _credentialProvider;

    public FileTransferServiceFactory(
        ILogger<ScpFileTransferService> logger,
        IOptions<N24DataRelayConfiguration> options,
        ICredentialProvider credentialProvider)
    {
        _logger = logger;
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _credentialProvider = credentialProvider;
    }

    public IFileTransferService CreateTransferService()
    {
        if (string.Equals(_config.Service.TransferMethod, "ssh", StringComparison.OrdinalIgnoreCase))
            return new ScpFileTransferService(_logger, _config, _credentialProvider);
        throw new NotSupportedException($"Transfer method '{_config.Service.TransferMethod}' is not supported. Use 'ssh'.");
    }
}
