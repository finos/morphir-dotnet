using Microsoft.AspNetCore.SignalR;

namespace Morphir.Server.Hubs;

/// <summary>
/// SignalR hub for Morphir real-time communication
/// </summary>
public class MorphirHub : Hub
{
    /// <summary>
    /// Called when a client connects to the hub
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Caller.SendAsync("Connected", new { Message = "Connected to Morphir hub", Timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Called when a client disconnects from the hub
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

