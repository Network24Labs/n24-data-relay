using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Hubs;

/// <summary>
/// SignalR hub for real-time transfer status updates.
///
/// Group membership (assigned on connect):
///   "user:{username}"  — every authenticated user joins their own group
///   "role:Admin"       — admin users also join this group
///
/// Regular users only receive updates for their own transfers.
/// Admins receive all updates via the role group.
/// </summary>
[Authorize]
public class TransferHub : Hub
{
    private readonly ITransferTracker _tracker;

    public TransferHub(ITransferTracker tracker)
    {
        _tracker = tracker;
    }

    public override async Task OnConnectedAsync()
    {
        var username = Context.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(username))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(username));

            if (Context.User?.IsInRole("Admin") == true)
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        }
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Returns recent records for the calling user.
    /// Admins receive all records; regular users see only their own.
    /// </summary>
    public IReadOnlyList<TransferStatusRecord> GetRecent()
    {
        var username = Context.User?.Identity?.Name;
        var all = _tracker.GetRecent(100);

        if (Context.User?.IsInRole("Admin") == true)
            return all;

        return all
            .Where(r => string.Equals(r.UploadedBy, username, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string UserGroup(string username) => $"user:{username}";
    public const string AdminGroup = "role:Admin";
}
