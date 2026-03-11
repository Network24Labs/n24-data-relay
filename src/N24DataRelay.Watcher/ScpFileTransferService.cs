using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace N24DataRelay.Watcher;

/// <summary>SCP file transfer using SSH.NET.</summary>
public sealed class ScpFileTransferService : IFileTransferService
{
    private readonly ILogger<ScpFileTransferService> _logger;
    private readonly N24DataRelayConfiguration _config;
    private readonly ICredentialProvider _credentialProvider;
    private readonly IDataProtector? _dataProtector;

    private static readonly string EncPrefix = ApplicationConstants.Security.EncryptedValuePrefix;

    public ScpFileTransferService(
        ILogger<ScpFileTransferService> logger,
        N24DataRelayConfiguration config,
        ICredentialProvider credentialProvider,
        IDataProtector? dataProtector = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _dataProtector = dataProtector;
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
        client.HostKeyReceived += (_, args) => HandleHostKey(args, ssh);
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
            var password = _credentialProvider.GetCredential("SshPassword")
                        ?? DecryptIfProtected(ssh.PasswordEncrypted);
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
                client.HostKeyReceived += (_, args) => HandleHostKey(args, ssh);
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

    /// <summary>Writes a small probe file to the destination path and verifies it (v1). Fail loudly on wrong path.</summary>
    public async Task<(bool Success, string? ErrorMessage)> TestConnectionToPathAsync(CancellationToken cancellationToken = default)
        => await TestConnectionToPathAsync(_config.Transfer.Ssh, _config.Transfer.Ssh.DestinationPath, cancellationToken).ConfigureAwait(false);

    /// <summary>Tests SSH connection and path writability using the given settings (e.g. for a linked peer).</summary>
    public async Task<(bool Success, string? ErrorMessage)> TestConnectionToPathAsync(SshSettings ssh, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ssh.Host))
            return (false, "Host is not configured.");
        if (string.IsNullOrWhiteSpace(destinationPath))
            return (false, "Destination path is not configured.");
        var connectionInfo = CreateConnectionInfo(ssh);
        if (connectionInfo == null)
            return (false, "Could not create SSH connection (check key or password).");

        var probeName = ".n24-relay-test-" + DateTime.UtcNow.Ticks;
        var remotePath = destinationPath.TrimEnd('/', '\\');
        var probeFullPath = remotePath + "/" + probeName;

        try
        {
            await Task.Run(() =>
            {
                using var client = new SftpClient(connectionInfo);
                client.HostKeyReceived += (_, args) => HandleHostKey(args, ssh);
                client.Connect();

                try
                {
                    // Ensure directory exists (SFTP will fail upload if not)
                    if (!client.Exists(remotePath))
                    {
                        try
                        {
                            client.CreateDirectory(remotePath);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Path not found and could not create directory: {remotePath}. {ex.Message}", ex);
                        }
                    }

                    // Write probe file
                    using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("n24-relay-path-test")))
                    {
                        client.UploadFile(stream, probeFullPath);
                    }

                    // Verify: stat the file
                    var attrs = client.GetAttributes(probeFullPath);
                    if (attrs == null || !attrs.IsRegularFile)
                        throw new InvalidOperationException("Probe file was written but could not be verified (stat failed).");

                    // Remove probe file
                    try { client.DeleteFile(probeFullPath); } catch { /* best effort */ }
                }
                finally
                {
                    client.Disconnect();
                }
            }, cancellationToken).ConfigureAwait(false);

            return (true, null);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (msg.Contains("No such file", StringComparison.OrdinalIgnoreCase) || msg.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return (false, $"Path not found or not accessible: {destinationPath}. Check that the path exists and the SSH user can write to it.");
            if (msg.Contains("permission", StringComparison.OrdinalIgnoreCase) || msg.Contains("denied", StringComparison.OrdinalIgnoreCase))
                return (false, "Permission denied writing to the destination path. Check SSH user and directory permissions.");
            return (false, $"Connection or path test failed: {msg}");
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
                client.HostKeyReceived += (_, args) => HandleHostKey(args, ssh);
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

    /// <summary>
    /// Decrypts a value that was encrypted by <c>ConfigWriterService</c> (prefixed with <c>ENC:</c>).
    /// Returns the raw value unchanged if it is not prefixed (plaintext / legacy config).
    /// </summary>
    private string? DecryptIfProtected(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith(EncPrefix, StringComparison.Ordinal)) return value;

        if (_dataProtector is null)
        {
            _logger.LogWarning("SSH password value is encrypted (ENC: prefix) but no data protector is available. Cannot decrypt.");
            return null;
        }

        try
        {
            return _dataProtector.Unprotect(value[EncPrefix.Length..]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt SSH PasswordEncrypted value. The data-protection key may have changed.");
            return null;
        }
    }

    /// <summary>
    /// Handles the SSH.NET HostKeyReceived event.
    /// When <see cref="SshSettings.StrictHostKeyChecking"/> is true, the connection is only
    /// trusted if the presented SHA-256 fingerprint matches <see cref="SshSettings.KnownHostFingerprint"/>.
    /// When strict checking is disabled, the connection is allowed with a warning.
    /// </summary>
    private void HandleHostKey(HostKeyEventArgs args, SshSettings ssh)
    {
        var presented = Convert.ToHexString(SHA256.HashData(args.HostKey)).ToLowerInvariant();

        if (!ssh.StrictHostKeyChecking)
        {
            _logger.LogWarning(
                "SSH strict host key checking is disabled. Host: {Host}, Algorithm: {Algo}, SHA256: {Fp}. " +
                "To enforce, set StrictHostKeyChecking=true and KnownHostFingerprint={Fp} in SSH settings.",
                ssh.Host, args.HostKeyName, presented, presented);
            args.CanTrust = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(ssh.KnownHostFingerprint))
        {
            _logger.LogError(
                "SSH host key verification failed: StrictHostKeyChecking is enabled but KnownHostFingerprint " +
                "is not configured. Rejecting connection to {Host}. " +
                "Set KnownHostFingerprint to the following SHA-256 fingerprint to allow: {Fp}",
                ssh.Host, presented);
            args.CanTrust = false;
            return;
        }

        // Normalise: strip colons/spaces, lowercase — accept both "ab:cd:ef" and "abcdef" formats
        var stored = ssh.KnownHostFingerprint
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        if (!string.Equals(presented, stored, StringComparison.Ordinal))
        {
            _logger.LogError(
                "SSH host key MISMATCH — possible man-in-the-middle attack. " +
                "Host: {Host}, Expected: {Expected}, Received: {Presented}. Rejecting connection.",
                ssh.Host, stored, presented);
            args.CanTrust = false;
            return;
        }

        args.CanTrust = true;
    }
}
