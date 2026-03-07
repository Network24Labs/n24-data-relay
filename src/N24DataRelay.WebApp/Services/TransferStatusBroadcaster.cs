using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Hubs;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Hosted service that subscribes to ITransferTracker.StatusChanged and pushes
/// updates to all connected SignalR clients via TransferHub.
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
        _ = _hub.Clients.All.SendAsync("transferStatus", record);
    }
}
