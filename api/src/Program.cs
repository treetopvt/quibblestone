// ----------------------------------------------------------------------------
//  Program.cs - QuibbleStone API composition root.
//
//  This is the SINGLE ASP.NET Core application that hosts BOTH the request/
//  response REST API (controllers) AND the real-time SignalR hub, per the
//  project charter (README section 4). We deliberately do NOT use Azure
//  Functions yet: one project, one deploy, one debugging story.
//
//  What this file wires up, in order:
//    1. Controllers  - the REST surface (see Controllers/HealthController.cs).
//    2. SignalR      - the real-time surface (see Hubs/GameHub.cs).
//    3. CORS         - lets the Vite web app (a different origin in dev) call
//                      the API and open a hub connection.
//    4. The pipeline - the controller routes and the hub route.
//
//  Walking-skeleton scope: this proves the pieces connect. There is no game
//  logic here. Game features land later behind the "one engine, many thin
//  modes" abstraction (README section 4).
//
//  Production scale-out note: for multi-instance hosting, real-time fan-out
//  moves to the provisioned Azure SignalR Service by chaining .AddAzureSignalR()
//  onto AddSignalR() and supplying its connection string from Key Vault (see
//  /infra). The local skeleton uses the in-process hub so it runs with zero
//  Azure setup.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// --- Services (dependency injection container) -------------------------------

// REST controllers (attribute-routed). See Controllers/HealthController.cs.
builder.Services.AddControllers();

// Real-time hub. For production scale-out, chain .AddAzureSignalR(...):
//   builder.Services.AddSignalR()
//       .AddAzureSignalR(builder.Configuration["AzureSignalR:ConnectionString"]);
builder.Services.AddSignalR();

// CORS: the web client runs on its own origin in dev (Vite, http://localhost:5173).
// Allowed origins come from configuration so each environment sets its own.
// AllowCredentials is required for SignalR's WebSocket / long-polling transports.
const string webClientCorsPolicy = "WebClient";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(webClientCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// --- HTTP request pipeline ----------------------------------------------------

app.UseCors(webClientCorsPolicy);

// REST endpoints (e.g. GET /health from HealthController).
app.MapControllers();

// Real-time endpoint. The web client connects to this URL (see web/src/signalr).
app.MapHub<GameHub>("/hubs/game");

app.Run();
