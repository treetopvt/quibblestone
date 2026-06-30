// ----------------------------------------------------------------------------
//  GameHub - the single SignalR hub for the walking skeleton.
//
//  Real-time is first-class in Quibbler: lobby, presence, live session state,
//  and reveal broadcast all ride on SignalR (README section 4). This skeleton
//  hub exposes ONE method, Ping, so the web client can prove an end-to-end
//  real-time round trip: client invokes Ping -> server echoes back.
//
//  Future game hubs (rooms, rosters, word collection, reveal) grow from this
//  same connection. Nothing game-specific lives here yet.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;

namespace Quibbler.Api.Hubs;

public sealed class GameHub : Hub
{
    // Invoked by the client; returns the echoed message to the calling client.
    // A real hub would also broadcast to a room via Clients.Group(roomCode).
    public Task<string> Ping(string message)
    {
        return Task.FromResult($"pong: {message}");
    }
}
