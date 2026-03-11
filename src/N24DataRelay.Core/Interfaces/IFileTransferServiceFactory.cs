using N24DataRelay.Core.Models;

namespace N24DataRelay.Core.Interfaces;

/// <summary>Creates IFileTransferService instances based on the current configuration.</summary>
public interface IFileTransferServiceFactory
{
    IFileTransferService CreateTransferService();
    /// <summary>Creates a transfer service for a specific route (multi-target / 3-hop).</summary>
    IFileTransferService CreateTransferServiceForRoute(TransferRouteSettings route);
    /// <summary>Tests SSH connection and path writability for a peer (host, port, path). Uses current SSH credentials.</summary>
    Task<(bool Success, string? ErrorMessage)> TestSshConnectionToPathAsync(string host, int port, string destinationPath, string? knownHostFingerprint, CancellationToken cancellationToken = default);
}
