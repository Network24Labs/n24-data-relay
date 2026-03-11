using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IDataProtector _dataProtector;

    public FileTransferServiceFactory(
        ILoggerFactory loggerFactory,
        IOptionsMonitor<N24DataRelayConfiguration> options,
        ICredentialProvider credentialProvider,
        IDataProtectionProvider dataProtectionProvider)
    {
        _loggerFactory      = loggerFactory;
        _options            = options ?? throw new ArgumentNullException(nameof(options));
        _credentialProvider = credentialProvider;
        _dataProtector      = dataProtectionProvider.CreateProtector("N24DataRelay.Credentials");
    }

    public IFileTransferService CreateTransferService()
    {
        var config = _options.CurrentValue;
        var method = config.Service.TransferMethod;

        if (string.Equals(method, "ssh", StringComparison.OrdinalIgnoreCase))
            return new ScpFileTransferService(
                _loggerFactory.CreateLogger<ScpFileTransferService>(), config, _credentialProvider, _dataProtector);

        if (string.Equals(method, "smb", StringComparison.OrdinalIgnoreCase))
            return new SmbFileTransferService(
                _loggerFactory.CreateLogger<SmbFileTransferService>(), config, _credentialProvider);

        throw new NotSupportedException(
            $"Transfer method '{method}' is not supported. Valid values: 'ssh', 'smb'.");
    }

    public IFileTransferService CreateTransferServiceForRoute(TransferRouteSettings route)
    {
        var config = _options.CurrentValue;
        var clone = JsonSerializer.Deserialize<N24DataRelayConfiguration>(JsonSerializer.Serialize(config))
            ?? new N24DataRelayConfiguration();
        clone.Transfer.Ssh = route.Ssh;
        clone.Transfer.Smb = route.Smb;
        clone.Service.TransferMethod = route.TransferMethod;
        var method = route.TransferMethod;
        if (string.Equals(method, "ssh", StringComparison.OrdinalIgnoreCase))
            return new ScpFileTransferService(
                _loggerFactory.CreateLogger<ScpFileTransferService>(), clone, _credentialProvider, _dataProtector);
        if (string.Equals(method, "smb", StringComparison.OrdinalIgnoreCase))
            return new SmbFileTransferService(
                _loggerFactory.CreateLogger<SmbFileTransferService>(), clone, _credentialProvider);
        throw new NotSupportedException(
            $"Transfer method '{method}' is not supported. Valid values: 'ssh', 'smb'.");
    }

    public async Task<(bool Success, string? ErrorMessage)> TestSshConnectionToPathAsync(string host, int port, string destinationPath, string? knownHostFingerprint, CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        var baseSsh = config.Transfer.Ssh;
        var ssh = new SshSettings
        {
            Host = host,
            Port = port,
            Username = baseSsh.Username,
            AuthMethod = baseSsh.AuthMethod,
            PrivateKeyPath = baseSsh.PrivateKeyPath,
            PrivateKeyPassphrase = baseSsh.PrivateKeyPassphrase,
            PasswordEncrypted = baseSsh.PasswordEncrypted,
            DestinationPath = destinationPath,
            ConnectionTimeout = baseSsh.ConnectionTimeout,
            OperationTimeout = baseSsh.OperationTimeout,
            StrictHostKeyChecking = baseSsh.StrictHostKeyChecking,
            KnownHostFingerprint = knownHostFingerprint ?? baseSsh.KnownHostFingerprint
        };
        var scp = new ScpFileTransferService(
            _loggerFactory.CreateLogger<ScpFileTransferService>(),
            config,
            _credentialProvider,
            _dataProtector);
        return await scp.TestConnectionToPathAsync(ssh, destinationPath, cancellationToken).ConfigureAwait(false);
    }
}
