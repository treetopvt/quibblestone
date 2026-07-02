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

using QuibbleStone.Api.Content;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// --- Services (dependency injection container) -------------------------------

// REST controllers (attribute-routed). See Controllers/HealthController.cs.
builder.Services.AddControllers();

// Child safety (README section 6, non-negotiable). The ONE server-side content
// safety filter: every free-text surface (nicknames, blank answers) resolves
// IContentSafetyFilter and routes player text through it BEFORE storing or showing
// it (child-safety/01). Registered as a singleton because the implementation is
// stateless after construction and loads its bundled baseline blocklist once - so
// the hub and any future REST controller share ONE instance and ONE blocklist
// (AC-05). The check is authoritative / server-side (AC-04). Consuming surfaces
// (nicknames, answers) call it in their own later stories; this story owns the
// contract + the single registration.
builder.Services.AddSingleton<IContentSafetyFilter, ContentSafetyFilter>();

// group-play/01: the minimal server-side template catalog (mirrors
// web/src/content/seedLibrary.ts by id - kept in sync BY HAND) and the
// server-side family-safe content gate. Both are PURE + stateless, so they are
// singletons shared by every transient GameHub instance. The catalog holds only
// { Id, FamilySafe, BlankCount } - never the template prose (that stays
// client-side; clients resolve full content by id). The selector is the server
// analog of the web's selectTemplates gate (child-safety/02) and is
// authoritative for which templates a family-safe round may offer (AC-04); its
// only consumer is the host's StartRound template selection. Neither ever
// relaxes the profanity filter above.
builder.Services.AddSingleton<TemplateCatalog>();
builder.Services.AddSingleton<FamilySafeContentSelector>();

// story-selection/01: the server-side story-LENGTH content stage - the second
// stage of the ONE selection pipeline, sitting NEXT TO the family-safe gate. It
// classifies a template as quick (<= 6 blanks) or full (>= 7) purely from
// TemplateCatalogEntry.BlankCount (length is DERIVED, never authored) and applies
// the empty-pool fallback that degrades to the family-safe pool rather than
// failing a round (AC-06). Pure + stateless, so it is a singleton like the
// family-safe selector. It is the server mirror of web/src/content/length.ts
// (kept in sync BY HAND) and NEVER runs before or around the family-safe gate.
builder.Services.AddSingleton<LengthContentSelector>();

// story-selection/03: the server-side freshness-ROTATION content stage - the
// THIRD and LAST stage of the ONE selection pipeline, running immediately after
// the length stage and immediately before the random pick. It excludes
// templates already played in the CURRENT room (Room.PlayedTemplateIds) and
// recycles (reopens) the whole pool once every template has been played rather
// than failing a round (AC-03). Pure + stateless, so it is a singleton like the
// other two selectors. It is the server mirror of web/src/content/fresh.ts
// (kept in sync BY HAND) and NEVER runs before or around the family-safe /
// length gates.
builder.Services.AddSingleton<FreshnessContentSelector>();

// story-selection/04 (anonymous serve log): the ONE telemetry sink, chosen at
// STARTUP by whether a storage connection string is configured. With a connection
// string (supplied per-environment from Key Vault / an app setting, NEVER a
// committed literal - see appsettings.json's Telemetry section), it writes one
// tiny, PII-free "template served" entity per round start to Azure Table Storage
// (AC-01, AC-06). WITHOUT one (local dev, CI, a fresh clone), it degrades to the
// NoOp sink so the app runs EXACTLY as today with ZERO Azure setup (AC-05). A
// singleton: the implementation is stateless after construction (a TableClient or
// a logger), so the hub and the TelemetryController share ONE instance. Either
// way the write is fire-and-forget and never gates gameplay (AC-03).
var telemetryConnectionString = builder.Configuration["Telemetry:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(telemetryConnectionString))
{
    builder.Services.AddSingleton<ITelemetrySink, NoOpTelemetrySink>();
}
else
{
    builder.Services.AddSingleton<ITelemetrySink>(sp =>
        new TableStorageTelemetrySink(
            telemetryConnectionString,
            sp.GetRequiredService<ILogger<TableStorageTelemetrySink>>()));
}

// Ephemeral in-memory room store (session-engine/01). Registered as a SINGLETON
// so every transient GameHub instance (SignalR builds a fresh hub per
// invocation) shares the SAME set of active rooms. This is the toy's ONLY room
// state - there is no database (CLAUDE.md section 10); rooms live in memory for
// the length of a play session and expire when idle (AC-05).
builder.Services.AddSingleton<RoomRegistry>();

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
