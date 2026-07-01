# api - QuibbleStone backend

The single **ASP.NET Core** application that hosts **both** the REST API and the
**SignalR** hub (README section 4). One project, one deploy, one debugging story.
We carve workloads out into Azure Functions later, only when a real reason
appears (async AI generation, Stripe webhooks).

## Layout

```
api/
  QuibbleStone.Api.csproj      project + content root (net10.0)
  appsettings*.json        configuration (CORS allowlist, logging)
  Properties/              launchSettings.json (dev ports)
  src/                     C# source
    Program.cs             composition root: controllers + SignalR + CORS
    Controllers/           REST surface (HealthController)
    Hubs/                  real-time surface (GameHub)
```

## Run

```bash
dotnet run --project api/QuibbleStone.Api.csproj
```

Dev URLs (see `Properties/launchSettings.json`):

- REST:    `http://localhost:5180/health`
- SignalR: `http://localhost:5180/hubs/game`

The web client points at these via `web/.env.development`.

## Surface

- `GET /health` -> `{ status, service, version, utc }`
- `GameHub` (real-time, session-engine): `CreateRoom()`, `JoinRoom(code,
  displayName, variant)`, `LeaveRoom(code)`, and `OnDisconnectedAsync` - rooms
  are ephemeral in-memory (RoomRegistry, no DB); the hub broadcasts
  `"RosterChanged"` to a room's group on every roster change.

More game hubs / methods (word collection, reveal) grow from this same
connection.
