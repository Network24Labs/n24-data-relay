using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.Watcher;

public sealed class FileTransferServiceFactory : IFileTransferServiceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _options;
    private readonly ICredentialProvider _credentialProvider;

    public FileTransferServiceFactory(
        ILoggerFactory loggerFactory,
        IOptionsMonitor<N24DataRelayConfiguration> options,
        ICredentialProvider credentialProvider)
    {
        _loggerFactory       = loggerFactory;
        _options             = options ?? throw new ArgumentNullException(nameof(options));
        _credentialProvider  = credentialProvider;
    }

    public IFileTransferService CreateTransferService()
    {
        var config = _options.CurrentValue;
        var method = config.Service.TransferMethod;

        if (string.Equals(method, "ssh", StringComparison.OrdinalIgnoreCase))
            return new ScpFileTransferService(
                _loggerFactory.CreateLogger<ScpFileTransferService>(), config, _credentialProvider);

        if (string.Equals(method, "smb", StringComparison.OrdinalIgnoreCase))
            return new SmbFileTransferService(
                _loggerFactory.CreateLogger<SmbFileTransferService>(), config, _credentialProvider);

        throw new NotSupportedException(
            $"Transfer method '{method}' is not supported. Valid values: 'ssh', 'smb'.");
    }
}
