using System.IO;
using Microsoft.Extensions.Logging;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.Watcher;

/// <summary>
/// SMB file transfer via a locally-mounted share (Linux <c>cifs</c> mount or any other UNC-mapped path).
/// The target directory (<c>Transfer.Smb.SharePath</c>) must already be mounted and accessible before
/// the service starts. No external library is required; the implementation uses standard file I/O.
/// </summary>
public sealed class SmbFileTransferService : IFileTransferService
{
    private readonly ILogger<SmbFileTransferService> _logger;
    private readonly N24DataRelayConfiguration _config;
    private readonly ICredentialProvider _credentialProvider;

    public SmbFileTransferService(
        ILogger<SmbFileTransferService> logger,
        N24DataRelayConfiguration config,
        ICredentialProvider credentialProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
    }

    public string GetTransferMethod() => "SMB";

    public async Task<TransferResult> TransferFileAsync(
        string sourceFilePath, string? destinationPath, CancellationToken cancellationToken = default)
    {
        var result = new TransferResult
        {
            SourcePath     = sourceFilePath,
            FileName       = Path.GetFileName(sourceFilePath),
            StartTime      = DateTime.UtcNow,
            TransferMethod = GetTransferMethod(),
            RemoteHost     = _config.Transfer.Smb.Server
        };

        try
        {
            var smb = _config.Transfer.Smb;
            if (string.IsNullOrWhiteSpace(smb.SharePath))
            {
                result.Success       = false;
                result.ErrorMessage  = "SMB SharePath is not configured.";
                result.EndTime       = DateTime.UtcNow;
                return result;
            }

            if (!Directory.Exists(smb.SharePath))
            {
                result.Success      = false;
                result.ErrorMessage = $"SMB share path '{smb.SharePath}' does not exist or is not mounted.";
                result.EndTime      = DateTime.UtcNow;
                return result;
            }

            var destDir  = destinationPath ?? smb.SharePath;
            var destFile = Path.Combine(destDir, result.FileName);

            var fileInfo = new FileInfo(sourceFilePath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Source file not found.", sourceFilePath);

            result.FileSize = fileInfo.Length;

            // Copy is synchronous; run on a thread-pool thread to keep the calling thread responsive.
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(sourceFilePath, destFile, overwrite: true);
            }, cancellationToken).ConfigureAwait(false);

            result.Success         = true;
            result.DestinationPath = destFile;
            result.EndTime         = DateTime.UtcNow;

            if (_config.Service.VerifyTransfer)
            {
                var destInfo = new FileInfo(destFile);
                result.Verified = destInfo.Exists && destInfo.Length == fileInfo.Length;
            }

            _logger.LogInformation("SMB transfer complete: {File} → {Dest}", result.FileName, destFile);
        }
        catch (Exception ex)
        {
            result.Success      = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetails = ex.ToString();
            result.EndTime      = DateTime.UtcNow;
            _logger.LogError(ex, "SMB transfer failed: {Source}", sourceFilePath);
        }

        return result;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var path = _config.Transfer.Smb.SharePath;
        return Task.FromResult(!string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }

    public async Task<(bool Success, string? ErrorMessage)> TestConnectionToPathAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var path = _config.Transfer.Smb.SharePath;
        if (string.IsNullOrWhiteSpace(path))
            return (false, "SMB SharePath is not configured.");
        if (!Directory.Exists(path))
            return (false, $"Path does not exist or is not mounted: {path}");
        try
        {
            var probeName = ".n24-relay-test-" + DateTime.UtcNow.Ticks;
            var probePath = Path.Combine(path, probeName);
            await File.WriteAllTextAsync(probePath, "n24-relay-path-test", cancellationToken).ConfigureAwait(false);
            var content = await File.ReadAllTextAsync(probePath, cancellationToken).ConfigureAwait(false);
            File.Delete(probePath);
            return content == "n24-relay-path-test" ? (true, null) : (false, "Probe file verification failed.");
        }
        catch (Exception ex)
        {
            return (false, $"Path not writable or readable: {ex.Message}");
        }
    }

    public Task<bool> VerifyTransferAsync(string sourceFilePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var src  = new FileInfo(sourceFilePath);
            var dest = new FileInfo(destinationPath);
            return Task.FromResult(dest.Exists && dest.Length == src.Length);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
