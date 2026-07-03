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

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// --- Services (dependency injection container) -------------------------------

// REST controllers (attribute-routed). See Controllers/HealthController.cs.
builder.Services.AddControllers();

// platform-devops/04 (operational observability): the OPERATIONAL Application
// Insights pipeline - unhandled exceptions, failed requests, request rate +
// duration, and outbound dependency calls (AC-02). This is DISTINCT from the
// anonymous serve-log sink below (ITelemetrySink / Table Storage,
// story-selection/04): that is content-curation telemetry, this is operational
// health; they live side by side, neither replaces the other.
//
// ALWAYS registered (AC-02/AC-05): AddApplicationInsightsTelemetry reads
// APPLICATIONINSIGHTS_CONNECTION_STRING from config/env automatically and cleanly
// NO-OPS when it is absent (local dev, CI, a fresh clone) - it emits nothing and
// errors nothing. Registering it unconditionally also always registers
// TelemetryClient in DI, so the hub filter, the client-error controller, and
// story-05's future usage events can depend on it without branching. The
// connection string comes from Key Vault via an App Service app setting in a
// deployed environment - NEVER a committed literal, NEVER a VITE_ var (AC-01/AC-05).
builder.Services.AddApplicationInsightsTelemetry();

// AC-04 (child-safety / PII, NON-NEGOTIABLE): the ONE choke point every App
// Insights telemetry item flows through before send. Registered as a SINGLETON
// ITelemetryInitializer so the SDK runs EVERY item (requests, exceptions,
// dependencies, custom events - including story-05's future anonymous usage
// events and the client-error beacon) through it: it zeroes the client IP, strips
// request query strings (route/path only), and defensively drops any
// known-sensitive custom-property key. Only anonymous operational data leaves the
// process (README section 6). See PiiScrubbingTelemetryInitializer for the full rationale.
builder.Services.AddSingleton<Microsoft.ApplicationInsights.Extensibility.ITelemetryInitializer, PiiScrubbingTelemetryInitializer>();

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

// ai-cost-gate/01 (server-side AI proxy): the ONE server-side AI transport,
// chosen at STARTUP by whether `Ai:Endpoint` is configured - EXACTLY the
// ITelemetrySink config-presence idiom above. WITH an endpoint (supplied per-
// environment; the optional key is Key Vault-backed, NEVER a committed literal and
// NEVER a VITE_* var, AC-03), the Foundry-backed client talks to Azure OpenAI
// (gpt-5-mini; ADR 0001 picked gpt-4o-mini, superseded by availability) using the
// App Service managed identity (preferred) or the
// key fallback. WITHOUT one (local dev, CI, a fresh clone, before provisioning), it
// degrades to the no-op client that reports "AI unavailable" cleanly, so the app
// builds + runs with ZERO AI config and every consumer falls back deterministically
// (AC-04). A singleton either way - the client is stateless after construction. The
// AiOptions (model id + per-model $/1M rate constants together, so a model swap is
// one change - story 04 reads the rates) is registered for the whole gate to inject.
var aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
builder.Services.AddSingleton(aiOptions);
if (string.IsNullOrWhiteSpace(aiOptions.Endpoint))
{
    builder.Services.AddSingleton<IAiCompletionClient, NoOpAiCompletionClient>();
}
else
{
    builder.Services.AddSingleton<IAiCompletionClient>(sp =>
        new FoundryAiCompletionClient(
            aiOptions,
            sp.GetRequiredService<ILogger<FoundryAiCompletionClient>>()));
}

// ai-cost-gate: the ordered gate PIPELINE seam every AI feature routes through
// (entitlement@session-creation [02] -> quota [03] -> spend-ceiling [04] ->
// transport [01] -> spend-record/attribution [04] -> moderate [05]). Story 01
// establishes the seam + ordering + envelope; the three stage services are
// registered here with their SEAM defaults (no-op / pass-through) so the app runs
// green with zero config. Wave-2 leaf builders REPLACE these registrations with the
// real per-session quota (03), the monthly-total spend breaker + attribution (04),
// and the IContentSafetyFilter + family-safe moderation composition (05). The
// pass-through moderator default is NEVER shipped to a real AI consumer - story 05
// lands before ai-on-demand-generation/05 goes live. Every future AI feature is a
// CONSUMER of GatedAiCompletionClient, never a new gate.
// ai-cost-gate/03 (#122): the REAL per-anonymous-session quota replaces the story-01
// pass-through (UnlimitedAiQuota). It enforces a per-session AI call allowance keyed
// on the anonymous Room.InstanceId (never PII) BEFORE the transport, and reports the
// server-authoritative "N Fresh Runes left" count for the client meter. In-memory +
// thread-safe + fail-safe (deny on any error, never unlimited). Singleton so every
// transient GameHub invocation shares the one counter (mirrors RoomRegistry). The
// per-IP abuse guard (AC-03) is the SEPARATE named rate-limiter registered below.
builder.Services.AddSingleton<IAiQuota, AiQuota>();

// ai-cost-gate/04 (spend circuit-breaker + attribution): swap the story-01
// NoOpAiSpendGuard for the REAL breaker when a storage connection string is present
// (MIRRORS the ITelemetrySink config-presence idiom above and REUSES the same
// Telemetry:StorageConnectionString account - NO new resource). WITH storage, the
// AiSpendBreaker persists the running UTC-month total in Table Storage (survives a
// recycle), opens at 100% of AiOptions.MonthlyCeilingUsd, and emits one anonymous
// attribution event per call through the injected TelemetryClient + the single PII
// scrubber. Reuses the telemetryConnectionString read above.
//
// WITHOUT storage there are two cases (Gate-1 review WARN-001, closing the fail-OPEN
// money hole): if a REAL AI endpoint is ALSO configured, billable calls would ship,
// so we must NOT leave them behind the always-open NoOp guard - we register the
// always-CLOSED guard so the gate degrades to the deterministic fallback rather than
// call AI with no ceiling (the charter's load-bearing rule: no ungated AI, ever). If
// no AI endpoint is configured either (local dev, CI, a fresh clone), AI is a no-op
// anyway, so the harmless NoOp guard keeps the app building + running with ZERO Azure
// config (story 01 AC-04).
if (string.IsNullOrWhiteSpace(telemetryConnectionString))
{
    if (string.IsNullOrWhiteSpace(aiOptions.Endpoint))
    {
        builder.Services.AddSingleton<IAiSpendGuard, NoOpAiSpendGuard>();
    }
    else
    {
        builder.Services.AddSingleton<IAiSpendGuard, ClosedAiSpendGuard>();
    }
}
else
{
    builder.Services.AddSingleton<IMonthlySpendStore>(sp =>
        new TableStorageMonthlySpendStore(
            telemetryConnectionString,
            sp.GetRequiredService<ILogger<TableStorageMonthlySpendStore>>()));
    builder.Services.AddSingleton<IAiSpendGuard>(sp =>
        new AiSpendBreaker(
            sp.GetRequiredService<IMonthlySpendStore>(),
            aiOptions,
            sp.GetRequiredService<Microsoft.ApplicationInsights.TelemetryClient>(),
            sp.GetRequiredService<ILogger<AiSpendBreaker>>()));
}

// ai-cost-gate/05 (moderate-before-display): the OPTIONAL Azure AI Content Safety
// second layer, chosen at STARTUP by whether `ContentSafety:Endpoint` is configured -
// EXACTLY the ITelemetrySink / IAiCompletionClient config-presence idiom above. WITHOUT
// it (local dev, CI, a fresh clone, today's deployed footprint) the NoOp screen allows
// everything, so the moderator runs the always-on hard filter + family-safe ONLY and
// behaves identically to before this seam existed (AC-05). WITH it, the real Azure AI
// Content Safety screen is a documented DROP-IN registered in the else branch - that
// concrete screen + its Azure.AI.ContentSafety SDK land with story 06's optional Bicep
// resource (not added now, so the app takes on no heavy, unvalidatable dependency and
// restores cleanly on net10.0). Turning the layer on is a config flip, not a code change.
var contentSafetyEndpoint = builder.Configuration["ContentSafety:Endpoint"];
if (string.IsNullOrWhiteSpace(contentSafetyEndpoint))
{
    builder.Services.AddSingleton<IAiContentSafetyScreen, NoOpAiContentSafetyScreen>();
}
else
{
    // Story 06 drop-in point: swap the real AzureAiContentSafetyScreen (endpoint +
    // Key Vault-backed key / managed identity) in here. Until it lands, keep the hard
    // filter + family-safe as the whole gate (fail to the safe, no-second-layer side)
    // rather than reference an SDK that is not yet validated on net10.0.
    builder.Services.AddSingleton<IAiContentSafetyScreen, NoOpAiContentSafetyScreen>();
}

// ai-cost-gate/05 (moderate-before-display): the REAL last-stage moderator replaces
// the pass-through seam. It composes the always-on hard gate (IContentSafetyFilter,
// child-safety/01) + the family-safe rule (FamilySafeContentSelector, child-safety/02)
// + the optional Content Safety screen above over EVERY AI item before any child sees
// it (AC-01/02/05), drops the unsafe and signals whether enough safe items survived
// (AC-04). Stateless after construction, so a singleton like its dependencies. There is
// no code path returning AI output without this stage (AC-07); curated content still
// skips the filter unchanged (game-modes/04).
builder.Services.AddSingleton<IAiOutputModerator, AiOutputModerator>();
builder.Services.AddSingleton<GatedAiCompletionClient>();

// ai-cost-gate/03 (#122) AC-03: the per-IP abuse guard - a COARSE cap on the
// aggregate AI call rate from one origin, on top of the per-session quota above, so
// spinning up many anonymous sessions from one client cannot multiply spend without
// bound. This is a NAMED, endpoint-scoped policy (AiPerIpRateLimitPolicy) partitioned
// by the remote IP - it is deliberately NOT a GLOBAL limiter, so it never throttles
// the existing REST / SignalR surface. The future AI consumer (the Fresh Runes jumble,
// ai-on-demand-generation/05) OPTS IN by decorating its endpoint with
// [EnableRateLimiting(Program.AiPerIpRateLimitPolicy)] (or the equivalent on its hub
// path); no AI HTTP endpoint exists in THIS feature yet, so the policy is registered
// and inert until a consumer applies it. The IP is used TRANSIENTLY as the partition
// key ONLY (AC-03/AC-04): it is never persisted here, never logged, and never attached
// to telemetry (the PiiScrubbingTelemetryInitializer already zeroes client IP on the
// telemetry path). app.UseRateLimiter() (below) adds the middleware that enforces the
// policy where an endpoint opts in.
builder.Services.AddRateLimiter(options =>
{
    // A rejected request degrades gracefully (AC-05): 429 rather than an error page.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Fixed window per remote IP. The limits are coarse abuse-guard values (a family
    // toy, not a hardened public API): generous for real play, low enough to blunt a
    // scripted spin-up. Kept as local consts (one place), not scattered literals.
    const int aiPerIpPermitPerWindow = 30;
    var aiPerIpWindow = TimeSpan.FromMinutes(1);

    options.AddPolicy(AiPerIpRateLimitPolicy, httpContext =>
    {
        // Partition on the remote IP (transient key only - never stored/logged). A
        // missing IP (unusual) folds into one shared "unknown" bucket rather than
        // bypassing the guard.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiPerIpPermitPerWindow,
                Window = aiPerIpWindow,
                QueueLimit = 0,
            });
    });

    // keepsake-gallery/04 (security review W-001): the OPEN, anonymous public publish
    // endpoint's per-IP guard, folded into this SAME AddRateLimiter registration (there
    // can be only ONE) alongside the AI per-IP policy above. Only POST /api/tales opts
    // in (via [EnableRateLimiting(PublishTalesRateLimit.PolicyName)]); the rest of the
    // API (hub, health, moderation) is untouched. A rejected caller gets 429. Per-IP
    // behind App Service is honored by the ForwardedHeaders config registered below.
    options.AddPolicy(PublishTalesRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PublishTalesRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PublishTalesRateLimit.PermitLimit,
                Window = PublishTalesRateLimit.Window,
                QueueLimit = 0,
            }));
});

// keepsake-gallery/04 (shareable tale link): the published-tale store, chosen at
// STARTUP by whether a storage connection string is configured - EXACTLY the
// NoOp-when-absent posture of the telemetry sink above. This is the feature's ONE
// server surface (a public read-only tale page + a stored tale), kept isolated in
// its own thin controller (PublishedTalesController) - it never touches GameHub or
// the round lifecycle. WITH a connection string (supplied per-environment from the
// SAME storage account, NEVER a committed literal), it writes each host-published,
// already-filtered tale to the "PublishedTales" table under an unguessable slug
// with a TTL (AC-03/AC-05). WITHOUT one (local dev, CI, a fresh clone), it degrades
// to the disabled store so the app runs with the feature simply OFF and ZERO Azure
// setup: publish returns a clear "not available" and the public GET 404s (AC-05).
// A singleton: stateless after construction (a TableClient or nothing). The share
// link is FREE - no entitlement check anywhere on this path (AC-04).
var talesConnectionString = builder.Configuration["PublishedTales:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(talesConnectionString))
{
    builder.Services.AddSingleton<IPublishedTaleStore, DisabledPublishedTaleStore>();
}
else
{
    builder.Services.AddSingleton<IPublishedTaleStore>(sp =>
        new TableStoragePublishedTaleStore(
            talesConnectionString,
            sp.GetRequiredService<ILogger<TableStoragePublishedTaleStore>>()));
}

// keepsake-gallery/04 (W-001, deployment hardening): make the per-IP publish
// limiter actually per-IP behind App Service. The platform terminates TLS at its
// front end, so Connection.RemoteIpAddress is the load balancer unless we honor
// the forwarded header - without this the limiter (PublishTalesRateLimit.PartitionKey
// reads RemoteIpAddress) would collapse every caller into ONE bucket. Trust only
// X-Forwarded-For; the App Service edge is the one hop (default ForwardLimit = 1
// takes the client IP it appends), and KnownNetworks/Proxies are cleared because
// that edge IP is not fixed. The PII scrubber still zeroes the IP before any
// telemetry leaves the process (README section 6), so this stays no-PII.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Ephemeral in-memory room store (session-engine/01). Registered as a SINGLETON
// so every transient GameHub instance (SignalR builds a fresh hub per
// invocation) shares the SAME set of active rooms. This is the toy's ONLY room
// state - there is no database (CLAUDE.md section 10); rooms live in memory for
// the length of a play session and expire when idle (AC-05).
builder.Services.AddSingleton<RoomRegistry>();

// ai-cost-gate/02 (entitlement at session-creation, #121): the thin, #70-shaped,
// DEFAULT-UNLOCKED entitlement seam. Registered here beside the room/session
// domain services (NOT in the AI-cost-gate/01 pipeline block above, to keep this
// edit off the lines the AI-pipeline builders touch). GameHub.CreateRoom evaluates
// it EXACTLY ONCE per session and captures the result on the Room; nothing is
// re-evaluated per AI call (AC-01). In alpha every reserved ai.* capability is
// unlocked, so shipping this changes zero observed behavior (ADR 0001 decision C)
// and the AI jumble stays reachable by every session. The real charging /
// entitlement chain (billing-entitlements/01, #70) later SUBSUMES this SAME
// contract without any consumer refactor. Singleton: the impl is stateless.
builder.Services.AddSingleton<IEntitlementService, DefaultUnlockedEntitlementService>();

// accounts-identity/02 (lightweight purchaser account, #68): the purchaser-account
// store, chosen at STARTUP by whether a storage connection string is configured -
// EXACTLY the config-presence idiom of the telemetry sink / published-tale store
// above. WITH a connection string (supplied per-environment, NEVER a committed
// literal), it persists accounts (email + created-at ONLY, AC-01) to Azure Table
// Storage keyed by a SHA-256 hash of the normalized email (AC-06 spirit). WITHOUT
// one (local dev, CI, a fresh clone), it degrades to a WORKING in-memory store -
// NOT a no-op - so accounts-identity/03's sign-in / restore is exercisable with
// ZERO Azure setup. A singleton either way (stateless past construction / holds
// the process-local dictionary). This store carries NO room / player reference
// (AC-03), so billing-entitlements/01's session gate can read "is there an entitled
// purchaser?" from it without touching gameplay state (AC-04).
var accountsConnectionString = builder.Configuration["Accounts:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(accountsConnectionString))
{
    builder.Services.AddSingleton<IAccountStore, InMemoryAccountStore>();
}
else
{
    builder.Services.AddSingleton<IAccountStore>(sp =>
        new TableStorageAccountStore(
            accountsConnectionString,
            sp.GetRequiredService<ILogger<TableStorageAccountStore>>()));
}

// accounts-identity/02 (#68): the REUSABLE magic-link token service - a SINGLETON
// because it owns the process-wide single-use nonce set AND the signing key
// (regenerated per process only when Accounts:TokenSigningKey is absent, so all
// callers must share the ONE instance or tokens would not verify across it). The
// signing secret is read from configuration (Key Vault-backed when deployed, NEVER
// a committed literal and NEVER a VITE_* var, AC-06); the service NEVER logs the
// token or the key. It is deliberately identity-neutral (subject is opaque), so
// sysadmin-console/01's operator login reuses this SAME registration against a
// SEPARATE allowlist - purchaser and admin stay structurally distinct here.
builder.Services.AddSingleton<IMagicLinkTokenService>(sp =>
    new MagicLinkTokenService(sp.GetRequiredService<IConfiguration>()[MagicLinkTokenService.ConfigKeyName]));

// accounts-identity/03 (sign-in / restore on a new device, #69): ASP.NET Core
// Data Protection, used by AccountsController to mint the SHORT-LIVED, purchaser-
// scoped sign-in credential (a time-limited protector under a dedicated purpose
// string - see AccountsController.PurchaserSessionPurpose). This is built INTO
// the framework, so it adds NO NuGet dependency and NO hand-rolled crypto, and no
// key material is ever a committed literal or a VITE_* var (AC-06).
//
// KEY RING SCOPE (deliberately the framework DEFAULT for now): this bare
// registration uses the default key ring (local key store), which is NOT shared
// across App Service scale-out and does NOT survive an app restart. That is fine
// for this thin slice - the credential TTL is short (12h) and re-signing-in is a
// cheap magic link, and the consumer (billing-entitlements/05) is still future.
// A durable, shared key ring (`.PersistKeysToAzureBlobStorage(...)` +
// `.ProtectKeysWithAzureKeyVault(...)`) is a FOLLOW-UP for the billing-entitlements
// deployment, alongside its Stripe / Key Vault wiring - NOT pulled in now so this
// slice takes on no unvalidatable Azure config.
//
// Registered here beside the account / token domain services it serves. CRITICAL
// boundary (AC-03/AC-04): this credential is consumed ONLY by the purchaser
// sign-in / restore surface - it is never required by, nor checked in, GameHub or
// any player-facing endpoint, so free play stays 100% login-free.
builder.Services.AddDataProtection();

// Real-time hub. For production scale-out, chain .AddAzureSignalR(...):
//   builder.Services.AddSignalR()
//       .AddAzureSignalR(builder.Configuration["AzureSignalR:ConnectionString"]);
//
// platform-devops/04 (AC-03): register the HubTelemetryFilter so a hub METHOD
// exception surfaces in App Insights carrying ONLY the anonymous method name -
// never the arguments (nicknames / words / codes, AC-04). The abnormal-DISCONNECT
// half of AC-03 lives in GameHub.OnDisconnectedAsync. Both resolve TelemetryClient
// from DI and no-op cleanly with no connection string (AC-05). The filter is a
// SINGLETON so SignalR resolves the one instance from DI rather than reconstructing
// it (via ActivatorUtilities) on every hub invocation - it is stateless past its
// injected TelemetryClient.
builder.Services.AddSingleton<HubTelemetryFilter>();
builder.Services.AddSignalR(options => options.AddFilter<HubTelemetryFilter>());

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

// FIRST: honor X-Forwarded-For so Connection.RemoteIpAddress is the real client IP
// before anything reads it (the publish rate limiter partitions on it). No-op with
// no proxy (local dev), so it is safe everywhere.
app.UseForwardedHeaders();

app.UseCors(webClientCorsPolicy);

// Activate the rate-limiter middleware for BOTH named policies registered above
// (ai-cost-gate/03's per-IP AI guard + keepsake-gallery/04's publish guard). It is
// endpoint-aware with no GLOBAL limiter, so it is INERT for every current REST / hub
// route and only ever engages where an endpoint opts in via [EnableRateLimiting] -
// the publish action today, the future AI consumer (AiPerIpRateLimitPolicy) later.
app.UseRateLimiter();

// REST endpoints (e.g. GET /health from HealthController).
app.MapControllers();

// Real-time endpoint. The web client connects to this URL (see web/src/signalr).
app.MapHub<GameHub>("/hubs/game");

app.Run();

// ai-cost-gate/03 (#122) AC-03: the shared name of the per-IP AI rate-limit policy,
// exposed on the top-level Program class so the future AI consumer can reference it
// symbolically ([EnableRateLimiting(Program.AiPerIpRateLimitPolicy)]) instead of
// re-typing a string literal. Program is the class the top-level statements above
// compile into; this partial adds the one shared const.
public partial class Program
{
    /// <summary>The named per-IP AI rate-limit policy (ai-cost-gate/03, AC-03).</summary>
    public const string AiPerIpRateLimitPolicy = "ai-per-ip";
}
