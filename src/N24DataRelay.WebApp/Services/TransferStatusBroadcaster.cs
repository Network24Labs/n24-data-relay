using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Hubs;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Hosted service that subscribes to ITransferTracker.StatusChanged and pushes
/// updates to the relevant SignalR groups:
///   - The uploader's own group ("user:{username}") — so they see their own transfers live
///   - The admin group ("role:Admin")               — so admins see all transfers live
/// </summary>
public sealed class TransferStatusBroadcaster : IHostedService
{
    private readonly ITransferTracker _tracker;
    private readonly IHubContext<TransferHub> _hub;

    public TransferStatusBroadcaster(ITransferTracker tracker, IHubContext<TransferHub> hub)
    {
        _tracker = tracker;
        _hub = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tracker.StatusChanged += OnStatusChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _tracker.StatusChanged -= OnStatusChanged;
        return Task.CompletedTask;
    }

    private void OnStatusChanged(object? sender, TransferStatusRecord record)
    {
        var userGroup = TransferHub.UserGroup(record.UploadedBy);

        // Send to the uploader's group; admins get it via their separate group.
        // If the uploader is themselves an admin both sends are no-ops for duplicate
        // connections — SignalR deduplicates within a single connection.
        _ = _hub.Clients.Group(userGroup).SendAsync("transferStatus", record);
        _ = _hub.Clients.Group(TransferHub.AdminGroup).SendAsync("transferStatus", record);
    }
}
