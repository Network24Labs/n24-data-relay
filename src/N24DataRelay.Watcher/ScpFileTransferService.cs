using System.IO;
using Microsoft.Extensions.Logging;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using Renci.SshNet;

namespace N24DataRelay.Watcher;

/// <summary>SCP file transfer using SSH.NET.</summary>
public sealed class ScpFileTransferService : IFileTransferService
{
    private readonly ILogger<ScpFileTransferService> _logger;
    private readonly N24DataRelayConfiguration _config;
    private readonly ICredentialProvider _credentialProvider;

    public ScpFileTransferService(
        ILogger<ScpFileTransferService> logger,
        N24DataRelayConfiguration config,
        ICredentialProvider credentialProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
    }

    public string GetTransferMethod() => "SSH/SCP";

    public async Task<TransferResult> TransferFileAsync(string sourceFilePath, string? destinationPath, CancellationToken cancellationToken = default)
    {
        var result = new TransferResult
        {
            SourcePath = sourceFilePath,
            FileName = Path.GetFileName(sourceFilePath),
            StartTime = DateTime.UtcNow,
            TransferMethod = GetTransferMethod(),
            RemoteHost = _config.Transfer.Ssh.Host
        };

        try
        {
            var ssh = _config.Transfer.Ssh;
            if (string.IsNullOrEmpty(ssh.Host) || string.IsNullOrEmpty(ssh.Username))
            {
                result.Success = false;
                result.ErrorMessage = "SSH Host and Username must be configured.";
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            var destPath = destinationPath ?? Path.Combine(ssh.DestinationPath, result.FileName).Replace('\\', '/');
            await Task.Run(() => TransferFileInternal(sourceFilePath, destPath, cancellationToken), cancellationToken).ConfigureAwait(false);

            result.Success = true;
            result.DestinationPath = destPath;
            result.EndTime = DateTime.UtcNow;
            if (new FileInfo(sourceFilePath).Exists)
                result.FileSize = new FileInfo(sourceFilePath).Length;

            if (_config.Service.VerifyTransfer)
                result.Verified = await VerifyTransferAsync(sourceFilePath, destPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetails = ex.ToString();
            result.EndTime = DateTime.UtcNow;
            _logger.LogError(ex, "Failed to transfer file: {SourcePath}", sourceFilePath);
        }

        return result;
    }

    private void TransferFileInternal(string sourceFilePath, string destinationPath, CancellationToken cancellationToken)
    {
        var ssh = _config.Transfer.Ssh;
        var connectionInfo = CreateConnectionInfo(ssh);
        if (connectionInfo == null)
            throw new InvalidOperationException("Could not create SSH connection (check key or password).");

        using var client = new ScpClient(connectionInfo);
        client.Connect();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(sourceFilePath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Source file not found.", sourceFilePath);

            client.Upload(fileInfo, destinationPath);
        }
        finally
        {
            client.Disconnect();
        }
    }

    private ConnectionInfo? CreateConnectionInfo(SshSettings ssh)
    {
        var authMethods = new List<AuthenticationMethod>();
        if (string.Equals(ssh.AuthMethod, "PublicKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ssh.PrivateKeyPath))
        {
            var keyFile = ssh.PrivateKeyPath;
            if (!Path.IsPathRooted(keyFile) && !string.IsNullOrEmpty(_config.Paths.ConfigDirectory))
                keyFile = Path.Combine(_config.Paths.ConfigDirectory, keyFile);
            if (!File.Exists(keyFile))
            {
                _logger.LogWarning("Private key file not found: {Path}", keyFile);
                return null;
            }
            var passphrase = _credentialProvider.GetCredential("SshKeyPassphrase");
            try
            {
                var keyAuth = new PrivateKeyFile(keyFile, passphrase);
                authMethods.Add(new PrivateKeyAuthenticationMethod(ssh.Username, keyAuth));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load private key: {Path}", keyFile);
                return null;
            }
        }
        else
        {
            var password = _credentialProvider.GetCredential("SshPassword") ?? ssh.PasswordEncrypted;
            if (!string.IsNullOrEmpty(password))
                authMethods.Add(new PasswordAuthenticationMethod(ssh.Username, password));
        }

        if (authMethods.Count == 0)
        {
            _logger.LogWarning("No SSH authentication method available.");
            return null;
        }

        return new ConnectionInfo(
            ssh.Host,
            ssh.Port,
            ssh.Username,
            authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(ssh.ConnectionTimeout),
            RetryAttempts = 3
        };
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var ssh = _config.Transfer.Ssh;
        if (string.IsNullOrEmpty(ssh.Host)) return false;
        var connectionInfo = CreateConnectionInfo(ssh);
        if (connectionInfo == null) return false;
        try
        {
            await Task.Run(() =>
            {
                using var client = new SshClient(connectionInfo);
                client.Connect();
                client.Disconnect();
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSH connection test failed.");
            return false;
        }
    }

    public async Task<bool> VerifyTransferAsync(string sourceFilePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var localInfo = new FileInfo(sourceFilePath);
            if (!localInfo.Exists) return false;
            var remoteSize = await GetRemoteFileSizeAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            return remoteSize >= 0 && remoteSize == localInfo.Length;
        }
        catch
        {
            return false;
        }
    }

    private async Task<long> GetRemoteFileSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        var ssh = _config.Transfer.Ssh;
        var connectionInfo = CreateConnectionInfo(ssh);
        if (connectionInfo == null) return -1;
        try
        {
            return await Task.Run(() =>
            {
                using var client = new SftpClient(connectionInfo);
                client.Connect();
                try
                {
                    var attrs = client.GetAttributes(remotePath);
                    return attrs?.Size ?? -1;
                }
                finally
                {
                    client.Disconnect();
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return -1;
        }
    }
}
