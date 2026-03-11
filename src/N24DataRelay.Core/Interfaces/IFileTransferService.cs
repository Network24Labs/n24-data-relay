using N24DataRelay.Core.Models;

namespace N24DataRelay.Core.Interfaces;

/// <summary>Interface for file transfer services (SSH/SCP, SMB, etc.).</summary>
public interface IFileTransferService
{
    Task<TransferResult> TransferFileAsync(string sourceFilePath, string? destinationPath = null, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes a small probe file to the configured destination path and verifies it (v1 requirement). Returns (success, errorMessage).</summary>
    Task<(bool Success, string? ErrorMessage)> TestConnectionToPathAsync(CancellationToken cancellationToken = default);
    string GetTransferMethod();
    Task<bool> VerifyTransferAsync(string sourceFilePath, string destinationPath, CancellationToken cancellationToken = default);
}
