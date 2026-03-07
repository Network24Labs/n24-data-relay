using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Hubs;

/// <summary>SignalR hub: clients connect to receive real-time transfer status updates.</summary>
[Authorize]
public class TransferHub : Hub
{
    private readonly ITransferTracker _tracker;

    public TransferHub(ITransferTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Returns recent transfer records directly as a hub method return value.</summary>
    public IReadOnlyList<TransferStatusRecord> GetRecent()
    {
        return _tracker.GetRecent(50);
    }
}
