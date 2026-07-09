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
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Accounts;
using Microsoft.AspNetCore.Authorization;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Ai.Jumble;
using QuibbleStone.Api.CloudGallery;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Invite;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Settings;
using QuibbleStone.Api.Telemetry;
using QuibbleStone.Api.Vault;

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
// control-plane/02 (#213): the AI config-presence boolean this branch already computes,
// extracted (not re-derived a second way) so the SystemConfigPresence value registered
// below can AND it against the ai.enabled system flag - config-presence is the floor.
var aiConfigured = !string.IsNullOrWhiteSpace(aiOptions.Endpoint);
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
            // control-plane/03 (#232): the monthly ceiling is read live from here now.
            sp.GetRequiredService<IRuntimeSettingsService>(),
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

// ai-on-demand-generation/05 (#126): the FIRST real consumer of the gate - the AI
// word-bank jumble. It builds the tiny prompt, routes it through the gate above
// (never a raw provider call), and shapes the moderated reply into fresh words; a
// degraded gate makes it fall back to game-modes/07's free reshuffle. Stateless
// (holds only the gate + a logger), so a singleton like its dependencies.
builder.Services.AddSingleton<JumbleWordGenerator>();

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

    // Fixed window per remote IP. The permit limit is a coarse abuse-guard value (a
    // family toy, not a hardened public API): generous for real play, low enough to blunt
    // a scripted spin-up. control-plane/03 (#232) migrated it onto the
    // `ai.rateLimit.perIpPermitPerMinute` settings key (code default 30) - read INSIDE the
    // partition-factory lambda below so a NEW partition picks up an operator override
    // (AC-06), and CLAMPED [1, 10000] there (AC-08) so a bad value can never disable or
    // crash the limiter. The window stays a local (unmigrated).
    var aiPerIpWindow = TimeSpan.FromMinutes(1);

    options.AddPolicy(AiPerIpRateLimitPolicy, httpContext =>
    {
        // Partition on the remote IP (transient key only - never stored/logged). A
        // missing IP (unusual) folds into one shared "unknown" bucket rather than
        // bypassing the guard.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            // The options factory runs ONLY when a NEW partition is created (not per request
            // against an existing one), so read + clamp the current effective permit limit
            // HERE (AC-06 / AC-08): a newly-created partition picks up an operator override,
            // an already-open partition keeps its baked-in options until its window resets,
            // and existing partitions cost no settings lookup. Resolved off
            // HttpContext.RequestServices (captured in this closure), never closed over at
            // registration time, so the value is current for each newly-created partition.
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ClampedRateLimitPermits(
                    httpContext, SettingsCatalog.AiRateLimitPerIpPermitPerMinute, codeDefault: 30),
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

    // sysadmin-console/03 (#137): the OPEN, anonymous "report this tale" endpoint's
    // per-IP guard - a SIBLING of the PublishTalesRateLimit policy above for the new
    // report surface, in this SAME registration. Only POST /api/tales/{slug}/report
    // opts in (via [EnableRateLimiting(ReportTalesRateLimit.PolicyName)]); the rest of
    // the API (publish, hub, health, moderation) is untouched. It stops one actor from
    // flooding reports to force-hide a legitimate tale past the threshold or bloating
    // storage (AC-05). Per-IP behind App Service is honored by ForwardedHeaders below.
    // 429 on reject.
    options.AddPolicy(ReportTalesRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ReportTalesRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ReportTalesRateLimit.PermitLimit,
                Window = ReportTalesRateLimit.Window,
                QueueLimit = 0,
            }));

    // accounts-identity/03 (review W-001): the OPEN, unauthenticated magic-link
    // request endpoint's per-IP guard, in this SAME registration alongside the two
    // policies above. Only POST /api/accounts/signin/request opts in (via
    // [EnableRateLimiting(SignInRateLimit.PolicyName)]); verify is bounded by the
    // single-use nonce + short expiry, and the game path is untouched. Stops an
    // email-bomb / token-mint flood before an email provider ships. 429 on reject.
    options.AddPolicy(SignInRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: SignInRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = SignInRateLimit.PermitLimit,
                Window = SignInRateLimit.Window,
                QueueLimit = 0,
            }));

    // keepsake-gallery/05 (#154): the signed-in cloud-gallery SAVE endpoint's per-IP
    // guard, in this SAME registration alongside the policies above. Only POST
    // /api/account/gallery opts in (via [EnableRateLimiting(CloudGalleryRateLimit.PolicyName)]);
    // the gallery read + deletes and the whole game path are untouched. Defense in
    // depth so a compromised / scripted purchaser credential cannot flood the store.
    // 429 on reject; per-IP behind App Service via the ForwardedHeaders config below.
    options.AddPolicy(CloudGalleryRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: CloudGalleryRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = CloudGalleryRateLimit.PermitLimit,
                Window = CloudGalleryRateLimit.Window,
                QueueLimit = 0,
            }));

    // sysadmin-console/01 (#135): the OPEN, unauthenticated operator-login request
    // endpoint's per-IP guard - a SIBLING of the SignInRateLimit policy above for the
    // SEPARATE operator surface, in this SAME registration. Only POST
    // /api/admin/login/request opts in (via [EnableRateLimiting(OperatorLoginRateLimit.PolicyName)]);
    // verify is bounded by the single-use nonce + short expiry, and the game / purchaser
    // paths are untouched. Stops an email-bomb / token-mint flood on the admin request
    // endpoint before an email provider ships. 429 on reject.
    // control-plane/03 (#232): the operator-login permit limit is migrated onto the
    // `admin.operatorLogin.rateLimitPermitPerMinute` settings key (code default 5), read
    // INSIDE this factory lambda so a NEW partition picks up an override (AC-06) and
    // CLAMPED [1, 10000] there (AC-08) - the same read-site safety net as the AI per-IP
    // policy above. OperatorLoginRateLimit.PermitLimit is now the code-default source.
    options.AddPolicy(OperatorLoginRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: OperatorLoginRateLimit.PartitionKey(httpContext),
            // Read + clamp inside the options factory (runs only at partition creation), the
            // same posture as the AI per-IP policy above - a new partition picks up an
            // override (AC-06), existing partitions cost no settings lookup.
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ClampedRateLimitPermits(
                    httpContext,
                    SettingsCatalog.AdminOperatorLoginRateLimitPermitPerMinute,
                    codeDefault: OperatorLoginRateLimit.PermitLimit),
                Window = OperatorLoginRateLimit.Window,
                QueueLimit = 0,
            }));

    // session-engine/12 (#180): the OPEN, anonymous email-game-invite endpoint's per-IP
    // guard - a SIBLING of the SignInRateLimit policy above for the new invite surface,
    // in this SAME registration. Only POST /api/invite/email opts in (via
    // [EnableRateLimiting(EmailInviteRateLimit.PolicyName)]); the GET availability probe,
    // the game path, and the rest of the API are untouched. Generous for a family
    // inviting a handful of relatives in one sitting, tight enough that it cannot become
    // a scripted email-bombing relay. 429 on reject; per-IP behind App Service via the
    // ForwardedHeaders config below.
    options.AddPolicy(EmailInviteRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: EmailInviteRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = EmailInviteRateLimit.PermitLimit,
                Window = EmailInviteRateLimit.Window,
                QueueLimit = 0,
            }));

    // keepsake-vault/01 (#196, AC-06): the anonymous keepsake-vault endpoints' per-IP
    // guards, in this SAME registration alongside the policies above. UNLIKE the
    // sibling keepsake surfaces (which limit their WRITE only), BOTH the vault write
    // and the vault READ are limited - the vault's read is an anonymous, bearer-id-
    // gated partition list a scripted caller could otherwise scrape. Only POST
    // /api/vault/tales (VaultSave) and GET /api/vault/tales (VaultRead) opt in (via
    // [EnableRateLimiting]); the mint endpoint and the whole game path are untouched.
    // The write window is tighter than the read window. 429 on reject; per-IP behind
    // App Service via the ForwardedHeaders config below. The residual (one attacker
    // rotating IPs against one vault id) is covered by the per-vault cap (AC-07).
    options.AddPolicy(VaultRateLimit.SavePolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: VaultRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = VaultRateLimit.SavePermitLimit,
                Window = VaultRateLimit.Window,
                QueueLimit = 0,
            }));
    options.AddPolicy(VaultRateLimit.ReadPolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: VaultRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = VaultRateLimit.ReadPermitLimit,
                Window = VaultRateLimit.Window,
                QueueLimit = 0,
            }));

    // keepsake-vault/03 (#230, AC-03.1): the per-IP guard on the anonymous claim-code
    // REDEEM endpoint (POST /api/vault/claim-code/redeem opts in via
    // [EnableRateLimiting]). This is only the FIRST of redemption's three controls -
    // an attacker rotating source IPs defeats a per-IP limiter, so it sits alongside
    // the IP-agnostic global ceiling (ClaimRedemptionCeiling, checked in the action)
    // and the per-code failed-attempt burn (in the vault store). 429 on reject.
    options.AddPolicy(VaultRateLimit.RedeemPolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: VaultRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = VaultRateLimit.RedeemPermitLimit,
                Window = VaultRateLimit.Window,
                QueueLimit = 0,
            }));

    // billing-entitlements/08 (#215, AC-06d): the OPERATOR-only Stripe-resync endpoint's
    // guard, in this SAME registration alongside the policies above. UNLIKE the per-IP
    // siblings, this partitions on a CONSTANT GLOBAL key - the abuse scenario is repeated
    // INVOCATION against Stripe's API (the operator-only auth already scopes the caller to
    // one trusted actor), so the whole endpoint shares one small budget rather than each IP
    // getting its own. Only POST /api/admin/billing/resync opts in (via
    // [EnableRateLimiting(StripeResyncRateLimit.PolicyName)]); a call beyond the budget gets
    // 429, so a scripted loop cannot fan out unbounded CustomerService.List +
    // SubscriptionService.List traffic and disrupt live webhook processing.
    options.AddPolicy(StripeResyncRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: StripeResyncRateLimit.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = StripeResyncRateLimit.PermitLimit,
                Window = StripeResyncRateLimit.Window,
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
// control-plane/02 (#213): the publishing config-presence boolean, extracted from the
// SAME expression this branch already uses so SystemConfigPresence can AND it against the
// publishing.enabled system flag (reserved - no consuming capability key yet).
var publishingConfigured = !string.IsNullOrWhiteSpace(talesConnectionString);
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

// keepsake-gallery/05 (cloud-synced purchaser gallery, #154): the cloud-gallery
// store, chosen at STARTUP by whether a storage connection string is configured -
// the SAME config-presence idiom as the stores above, but with a WORKING in-memory
// fallback (LIKE the account store, NOT the published-tale disabled no-op) so the
// whole save -> list -> delete -> revoke-all flow is exercisable with ZERO Azure
// setup. This is a purchaser-account-scoped surface (CloudGalleryController), kept
// isolated from GameHub and the round lifecycle (the keepsake-gallery/04 precedent).
// WITH a connection string (supplied per-environment, NEVER a committed literal),
// tales persist to the "CloudGalleryTales" table keyed PartitionKey = owner-hash,
// RowKey = tale id for a single-partition list-by-owner (AC-01/AC-05). WITHOUT one
// (local dev, CI, a fresh clone), the working in-memory store keeps the flow live.
// A singleton either way (stateless past construction / holds the process-local map).
// Availability is gated by the entitlement seam + a valid purchaser credential
// (AC-04), never a disabled-store flag. Reuses the SAME storage account infra provisions.
var cloudGalleryConnectionString = builder.Configuration["CloudGallery:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(cloudGalleryConnectionString))
{
    builder.Services.AddSingleton<ICloudGalleryStore, InMemoryCloudGalleryStore>();
}
else
{
    builder.Services.AddSingleton<ICloudGalleryStore>(sp =>
        new TableStorageCloudGalleryStore(
            cloudGalleryConnectionString,
            sp.GetRequiredService<ILogger<TableStorageCloudGalleryStore>>()));
}

// keepsake-vault/01 (anonymous server-side keepsake vault, #196): the vault store,
// chosen at STARTUP by whether a storage connection string is configured - the
// SAME config-presence idiom as the stores above, with a WORKING in-memory
// fallback (LIKE the cloud-gallery store, NOT the published-tale disabled no-op)
// because the vault is DEFAULT-ON for every anonymous player, so the whole
// save -> list flow this and later stories build on must be exercisable with ZERO
// Azure setup. This is an anonymous, vault-id-keyed surface (VaultController), kept
// isolated from GameHub and the round lifecycle (the keepsake-gallery precedent).
// WITH a connection string (supplied per-environment, NEVER a committed literal),
// tales persist to the "VaultTales" table keyed PartitionKey = vaultId,
// RowKey = tale id for a single-partition list (AC-02/AC-04); the TTL is computed
// from CreatedUtc at read time (AC-03) and a per-vault cap bounds growth (AC-07).
// WITHOUT one (local dev, CI, a fresh clone), the working in-memory store keeps the
// flow live. A singleton either way (stateless past construction / holds the
// process-local map). Access is gated by possession of the bearer vault id + the
// AC-01 length/format floor + the AC-06 per-IP rate limits, never a disabled flag.
// This is one of the ADR 0003 wave-1 Program.cs service registrations - it lands as
// its own small, rebased PR, not batched with the sibling wave-1 edits.
var vaultConnectionString = builder.Configuration["Vault:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(vaultConnectionString))
{
    builder.Services.AddSingleton<IVaultStore, InMemoryVaultStore>();
}
else
{
    builder.Services.AddSingleton<IVaultStore>(sp =>
        new TableStorageVaultStore(
            vaultConnectionString,
            sp.GetRequiredService<ILogger<TableStorageVaultStore>>()));
}

// keepsake-vault/03 (#230, AC-03.2): the GLOBAL, IP-agnostic redemption ceiling for
// the claim-code redeem endpoint - a SINGLETON so its one fixed-window budget is
// shared across every caller (a fresh request cannot hold window state). This is the
// control that bounds a distributed, IP-rotating brute force the per-IP limiter above
// cannot; the redeem action checks it before touching the store. Stateless past its
// counter; no storage, no config.
builder.Services.AddSingleton<ClaimRedemptionCeiling>();

// keepsake-gallery/04 (W-001, deployment hardening): make the per-IP publish
// limiter actually per-IP behind App Service. The platform terminates TLS at its
// front end, so Connection.RemoteIpAddress is the load balancer unless we honor
// the forwarded header - without this the limiter (PublishTalesRateLimit.PartitionKey
// reads RemoteIpAddress) would collapse every caller into ONE bucket. Trust only
// X-Forwarded-For; the App Service edge is the one hop (ForwardLimit = 1 takes the
// single client IP it appends), and KnownNetworks/Proxies are cleared because that
// edge IP is not fixed. The PII scrubber still zeroes the IP before any telemetry
// leaves the process (README section 6), so this stays no-PII.
//
// platform-devops/08 (ADR 0003 "Credentials survive scale-out safely" - the XFF
// single-hop-trusted-edge verification item): ForwardLimit is pinned to 1 EXPLICITLY
// (not left to the framework default) so the trust boundary is unmistakable - the app
// takes exactly the LAST hop's appended client IP and trusts no caller-supplied
// X-Forwarded-For beyond it. This is only safe because EVERY lane platform-devops/07
// stands up (qa AND beta) sits behind the SAME single-hop trusted edge (the Azure App
// Service front end, which strips any inbound XFF and appends the real client IP). If
// a lane were ever fronted by an additional proxy (a CDN / WAF in front of App
// Service), ForwardLimit would need raising to match the real hop count AND that
// proxy's network added to KnownProxies - otherwise every per-IP limiter is spoofable.
// Both current lanes are single-hop App Service edges, so 1 is correct; revisit here
// if the lane topology changes.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Ephemeral in-memory room store (session-engine/01). Registered as a SINGLETON
// so every transient GameHub instance (SignalR builds a fresh hub per
// invocation) shares the SAME set of active rooms. This is the toy's ONLY room
// state - there is no database (CLAUDE.md section 10); rooms live in memory for
// the length of a play session and expire when idle (AC-05).
builder.Services.AddSingleton<RoomRegistry>();

// accounts-identity/06 (ADR 0002 Decision F, finally wired - #210): the per-connection
// bridge from GameHub.OnConnectedAsync's one-time purchaser-credential resolution to a
// later CreateRoom on the SAME connection. A SINGLETON for the SAME cold-builder reason
// RoomRegistry is one - SignalR builds a fresh GameHub per invocation, so this CANNOT be
// a hub instance field. It holds ONLY resolved capabilities (a SessionEntitlements + a
// reserved bool) keyed by ConnectionId - never a purchaser identity (ADR 0003 "Identity
// is discarded at the boundary, structurally"; the value type has no identity field).
// Written in OnConnectedAsync, read in CreateRoom, cleared in OnDisconnectedAsync.
builder.Services.AddSingleton<IConnectionEntitlementStore, ConnectionEntitlementStore>();

// session-engine/07 (hold the seat): the ONE scheduled timer in the app. A dropped
// connection no longer evicts its seat on the spot - GameHub.OnDisconnected holds the
// seat and hands this singleton a one-shot timer that runs the eventual eviction ONLY
// if the grace window elapses with no reconnect (AC-03). A SINGLETON (like the
// RoomRegistry it works with) so the timer never lives on the per-invocation hub, and
// it uses IHubContext<GameHub> to broadcast the grace-expiry epilogue after the
// originating invocation has ended. The grace window (3 minutes) is a single
// named constant (SeatGraceService.DefaultGraceWindow) for easy tuning. No-ops
// for a connection that was never seated; story 08's Rejoin cancels a pending
// timer to keep the seat.
builder.Services.AddSingleton<SeatGraceService>();

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
//
// billing-entitlements/01 (#70): the DI swap that lands the REAL stored-value
// evaluation behind the SAME IEntitlementService contract (AC-02) - GameHub.CreateRoom,
// SessionEntitlements, and Room.cs are untouched. DefaultUnlockedEntitlementService is
// now registered as a CONCRETE singleton (the composed default-unlocked BASELINE,
// AC-03), and StoredValueEntitlementService is the IEntitlementService: baseline +
// any capabilities a resolved purchaser holds an active grant for. Anonymous sessions
// (null purchaser - every alpha session) get exactly the baseline, so behavior is
// unchanged today.
builder.Services.AddSingleton<DefaultUnlockedEntitlementService>();
// control-plane/02 (#213): the system-scope flag evaluator - the post-compose kill-switch
// filter StoredValueEntitlementService runs AFTER its baseline + grant composition. Bundles
// the IRuntimeSettingsService (the *.enabled flags, registered below) and the
// SystemConfigPresence floor (registered after the email options bind). A singleton so it
// shares the settings service's short read cache. Registration order is irrelevant here -
// every dependency is a singleton resolved lazily at first session-creation.
builder.Services.AddSingleton<SystemFlagEvaluator>();
builder.Services.AddSingleton<IEntitlementService, StoredValueEntitlementService>();

// billing-entitlements/01 (#70): the purchaser entitlement-grant store, chosen at
// STARTUP by whether a storage connection string is configured - EXACTLY the
// config-presence idiom of the account store below and the telemetry / published-tale
// stores above. WITH a connection string (supplied per-environment, NEVER a committed
// literal), grants persist to Azure Table Storage partitioned by a SHA-256 hash of the
// purchaser identity for a single-partition session-creation read (AC-05). WITHOUT one
// (local dev, CI, a fresh clone), it degrades to a WORKING in-memory store - NOT a
// no-op - so the gate and stories 03-05 are exercisable with ZERO Azure setup. A
// singleton either way. It reuses the SAME storage account infra already provisions.
var entitlementsConnectionString = builder.Configuration["Entitlements:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(entitlementsConnectionString))
{
    builder.Services.AddSingleton<IEntitlementGrantStore, InMemoryEntitlementGrantStore>();
}
else
{
    builder.Services.AddSingleton<IEntitlementGrantStore>(sp =>
        new TableStorageEntitlementGrantStore(
            entitlementsConnectionString,
            sp.GetRequiredService<ILogger<TableStorageEntitlementGrantStore>>()));
}

// control-plane/01 (#197): the runtime settings service - one typed key catalog with
// code defaults + persisted overrides, generalizing the Stripe-mode store's single-row
// pattern into one row per key. The store is chosen at STARTUP by the SAME config-presence
// idiom, reusing the SAME Entitlements storage account (no new resource): WITH a connection
// string the overrides persist to Table Storage (survive a recycle); WITHOUT one (local dev,
// CI, a fresh clone) a WORKING in-memory store keeps every AC exercisable with ZERO Azure
// setup (AC-05). The service is a singleton so its short read cache is shared.
if (string.IsNullOrWhiteSpace(entitlementsConnectionString))
{
    builder.Services.AddSingleton<IRuntimeSettingsStore, InMemoryRuntimeSettingsStore>();
}
else
{
    builder.Services.AddSingleton<IRuntimeSettingsStore>(sp =>
        new TableStorageRuntimeSettingsStore(
            entitlementsConnectionString,
            sp.GetRequiredService<ILogger<TableStorageRuntimeSettingsStore>>()));
}
builder.Services.AddSingleton<IRuntimeSettingsService, RuntimeSettingsService>();

// control-plane/01 (#197, AC-09): the operator action-log WRITE seam every settings PUT /
// DELETE appends to. Declared narrowly here (dependency-tolerant) with a WORKING in-memory
// implementation so SettingsController always has something to call - it does NOT hard-block
// on sysadmin-console/06, which lands the durable Table Storage store in a later wave. When
// 06 merges, this registration swaps to the real store with ZERO change to the call sites.
builder.Services.AddSingleton<IOperatorActionLog, InMemoryOperatorActionLog>();

// billing-entitlements/03 (#72): the Stripe billing seam. StripeOptions binds the
// "Stripe" config section (SecretKey + WebhookSigningSecret are Key Vault-backed
// SECRETS, never committed / never VITE_*, AC-01). Registered as a singleton so the
// checkout service and webhook handler share one bound instance.
var stripeOptions = builder.Configuration.GetSection(StripeOptions.SectionName).Get<StripeOptions>() ?? new StripeOptions();
builder.Services.AddSingleton(stripeOptions);

// billing-entitlements/06 (live/test mode toggle): the persisted active-mode flag,
// chosen at STARTUP by whether a storage connection string is configured - EXACTLY the
// config-presence idiom of the grant store above, and it REUSES the SAME storage account
// (Entitlements:StorageConnectionString - no new resource). WITH a connection string, the
// active mode persists to Table Storage (survives a recycle); WITHOUT one (local dev, CI,
// a fresh clone), the WORKING in-memory store keeps the toggle exercisable with ZERO Azure
// setup. Either way a fresh store defaults to Test (AC-05). A singleton so the cached
// context below sees a consistent value.
if (string.IsNullOrWhiteSpace(entitlementsConnectionString))
{
    builder.Services.AddSingleton<IActiveStripeModeStore, InMemoryActiveStripeModeStore>();
}
else
{
    builder.Services.AddSingleton<IActiveStripeModeStore>(sp =>
        new TableStorageActiveStripeModeStore(
            entitlementsConnectionString,
            sp.GetRequiredService<ILogger<TableStorageActiveStripeModeStore>>()));
}

// The single front door every billing consumer uses to get the ACTIVE mode's credentials
// (billing-entitlements/06). Resolves the active mode (cached briefly for the checkout /
// products hot paths) and projects StripeOptions.ForMode. A singleton over the store +
// options so the cache is shared.
builder.Services.AddSingleton<IActiveStripeContext, ActiveStripeContext>();

// sysadmin-console/04 (one console, one auth): the mode-toggle endpoint
// (StripeModeController) is now guarded by the REAL "Operator" authorization policy,
// exactly like the other admin controllers - so the interim shared-secret gate that
// used to be registered here (and its config key) is deleted. No dedicated DI is
// needed: the [Authorize(Policy = ...)] attribute uses the operator authentication +
// policy already registered above.

// The checkout service, chosen at STARTUP by whether Stripe is configured in ANY mode (the
// same config-presence idiom as the AI client / stores above). WITH a secret key, the real
// StripeCheckoutService resolves the ACTIVE mode's key per call (both payment + subscription
// modes through one method, AC-02); WITHOUT one (local dev, CI, a fresh clone), the DISABLED
// no-op so the app runs with ZERO Stripe setup and the tip jar / paywall show a clean "not
// available" state.
if (stripeOptions.IsConfigured)
{
    builder.Services.AddSingleton<IStripeCheckoutService>(sp =>
        new StripeCheckoutService(sp.GetRequiredService<IActiveStripeContext>(), sp.GetRequiredService<ILogger<StripeCheckoutService>>()));
}
else
{
    builder.Services.AddSingleton<IStripeCheckoutService, DisabledStripeCheckoutService>();
}

// The webhook idempotency ledger (AC-05), config-gated on the SAME storage account as
// the grant store: a working in-memory ledger locally, Table Storage when configured.
if (string.IsNullOrWhiteSpace(entitlementsConnectionString))
{
    builder.Services.AddSingleton<IProcessedEventStore, InMemoryProcessedEventStore>();
}
else
{
    builder.Services.AddSingleton<IProcessedEventStore>(sp =>
        new TableStorageProcessedEventStore(
            entitlementsConnectionString,
            sp.GetRequiredService<ILogger<TableStorageProcessedEventStore>>()));
}

// The webhook DOMAIN handler (SDK-free, AC-04): applies a normalized BillingEvent to
// the grant store, idempotently (AC-05), keyed to the purchaser account (AC-06), with
// the subscription lifecycle lease math (AC-08). A singleton over the stores + options.
builder.Services.AddSingleton<StripeWebhookHandler>();

// billing-entitlements/08 (#215, ADR 0003 Layer 2): the per-account Stripe RESYNC path.
// The Stripe-coupled candidate SOURCE follows the SAME config-presence idiom as the
// checkout service: WITH Stripe configured, the live StripeSubscriptionSource lists an
// email's candidate customers + subscriptions in the ACTIVE mode; WITHOUT it, the
// disabled no-op so the reconciliation service short-circuits to a clean "not configured"
// result with ZERO Stripe setup. The reconciliation SERVICE (pure, SDK-free) enforces the
// two binding security rules - metadata-matched identity (AC-04) + the mode-aware guard
// (AC-08) - over that source, the active-mode context, and the grant store. Both singletons.
if (stripeOptions.IsConfigured)
{
    builder.Services.AddSingleton<IStripeSubscriptionSource>(sp =>
        new StripeSubscriptionSource(
            sp.GetRequiredService<IActiveStripeContext>(),
            sp.GetRequiredService<ILogger<StripeSubscriptionSource>>()));
}
else
{
    builder.Services.AddSingleton<IStripeSubscriptionSource, DisabledStripeSubscriptionSource>();
}
builder.Services.AddSingleton<IStripeReconciliationService, StripeReconciliationService>();

// billing-entitlements/04 (#73): the product -> capability-bundle map (the family plan
// + add-on packs + the goodwill tip), the server-side lookup BillingController uses so
// the client only ever sends a product id, never capability keys. A singleton - the map
// is fixed after construction (price ids resolved from StripeOptions).
builder.Services.AddSingleton<IProductCatalog, ProductCatalog>();

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

// accounts-identity/08 (kid seat presets, #228): the seat-preset store, chosen at
// STARTUP by the SAME Accounts:StorageConnectionString the account store above uses
// - the identical config-presence idiom. WITH a connection string it persists presets
// to Azure Table Storage partitioned by the family AccountId; WITHOUT one (local dev,
// CI, a fresh clone) it degrades to a WORKING in-memory store (NOT a no-op) so the
// presets manager + join-flow picker are exercisable with ZERO Azure setup. A preset
// holds ONLY { Id, Nickname, Variant } (SeatPreset) and NO room / player reference,
// so this store stays entirely on the account plane (AC-03/AC-05). A singleton either
// way. NOTE: Program.cs is a Wave-3 hotspot (four ADR stories add registrations here)
// - this is a small, separately-rebased addition, per the serial-merge rule.
var seatPresetsConnectionString = builder.Configuration["Accounts:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(seatPresetsConnectionString))
{
    builder.Services.AddSingleton<ISeatPresetStore, InMemorySeatPresetStore>();
}
else
{
    builder.Services.AddSingleton<ISeatPresetStore>(sp =>
        new TableStorageSeatPresetStore(
            seatPresetsConnectionString,
            sp.GetRequiredService<ILogger<TableStorageSeatPresetStore>>()));
}

// platform-devops/08 (AC-07): the single-use magic-link nonce store. Config-presence
// split mirroring the account store above: WITH a storage connection string
// (Accounts:StorageConnectionString - a deployed environment) it persists consumed
// nonces to Azure Table Storage, SHARED across every instance, so a single-use link
// consumed on one instance is recognized as consumed by every other instance behind
// the load balancer (the replay-once-per-instance gap a durable, shared signing key
// would otherwise open - see IConsumedNonceStore). WITHOUT one (local dev, CI, a
// fresh clone) it degrades to a WORKING in-memory set - NOT a no-op - so single use
// is exercisable with ZERO Azure setup, exactly as it was before this story (AC-02).
// It holds ONLY the opaque nonce + its expiry, never a subject / email / room /
// player field (AC-05). A singleton either way (the shared set / the Table client).
// NOTE: a DEPLOYED environment that configures the durable key ring below MUST also
// configure this (see the fail-closed guard) - a shared signing key with a
// per-process nonce set is the exact replay gap AC-07 closes.
var accountsConnectionStringForNonces = builder.Configuration["Accounts:StorageConnectionString"];
if (string.IsNullOrWhiteSpace(accountsConnectionStringForNonces))
{
    builder.Services.AddSingleton<IConsumedNonceStore, InMemoryConsumedNonceStore>();
}
else
{
    builder.Services.AddSingleton<IConsumedNonceStore>(sp =>
        new TableStorageConsumedNonceStore(
            accountsConnectionStringForNonces,
            sp.GetRequiredService<ILogger<TableStorageConsumedNonceStore>>()));
}

// accounts-identity/02 (#68): the REUSABLE magic-link token service - a SINGLETON
// because it owns the signing key (regenerated per process only when
// Accounts:TokenSigningKey is absent, so all callers must share the ONE instance or
// tokens would not verify across it); the single-use nonce set now lives in the
// injected IConsumedNonceStore (durable + shared when deployed, platform-devops/08
// AC-07). The signing secret is read from configuration (Key Vault-backed when
// deployed - platform-devops/08 auto-provisions a durable CSPRNG value, AC-04 - NEVER
// a committed literal and NEVER a VITE_* var, AC-06); the service NEVER logs the
// token or the key. It is deliberately identity-neutral (subject is opaque), so
// sysadmin-console/01's operator login reuses this SAME registration against a
// SEPARATE allowlist - purchaser and admin stay structurally distinct here.
builder.Services.AddSingleton<IMagicLinkTokenService>(sp =>
    new MagicLinkTokenService(
        sp.GetRequiredService<IConfiguration>()[MagicLinkTokenService.ConfigKeyName],
        sp.GetRequiredService<IConsumedNonceStore>()));

// accounts-identity/04 (magic-link email delivery, #167): the ONE email transport
// seam both request endpoints deliver through (AC-02), chosen at STARTUP by whether
// an email provider is configured - EXACTLY the ITelemetrySink / IAiCompletionClient
// / IPublishedTaleStore config-presence idiom above. WITH a provider configured
// (EmailOptions.IsConfigured: a verified from-address PLUS an ACS endpoint for the
// keyless managed-identity path, or a Key Vault-backed connection string), the real
// AcsEmailSender emails the already-minted magic link. WITHOUT one (local dev, CI, a
// fresh clone, today's deployed footprint), it degrades to the NoOpEmailSender so the
// app builds + runs with ZERO email setup and the controllers' Development-only
// dev-token echo keeps local walkthroughs working unchanged (AC-03). A singleton
// either way - the sender is stateless after construction (holds an EmailClient or a
// logger). The provider secret (only on the connection-string fallback) is Key
// Vault-backed via an app setting, NEVER committed and NEVER a VITE_* var (AC-05).
var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton(emailOptions);
// control-plane/02 (#213): the email config-presence boolean, extracted from the SAME
// EmailOptions.IsConfigured expression this branch already uses so SystemConfigPresence
// can AND it against the email.enabled system flag (reserved - no consuming key yet).
var emailConfigured = emailOptions.IsConfigured;
if (!emailOptions.IsConfigured)
{
    builder.Services.AddSingleton<IEmailSender, NoOpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender>(sp =>
        new AcsEmailSender(emailOptions, sp.GetRequiredService<ILogger<AcsEmailSender>>()));
}

// control-plane/02 (#213): the config-presence FLOOR for the system-scope capability
// flags, constructed ONCE here - now that all three options are bound (AI ~line 167,
// publishing ~469, email above) - from the SAME expressions their config-presence
// branches already computed (never re-derived a second way). SystemFlagEvaluator ANDs
// each field against the matching *.enabled settings flag so an operator can force a
// CONFIGURED capability OFF but can never enable one whose infra is not wired up (ADR
// 0003 Layer 1 - config-presence is the floor). A singleton alongside the branches above.
builder.Services.AddSingleton(new SystemConfigPresence(
    AiConfigured: aiConfigured,
    PublishingConfigured: publishingConfigured,
    EmailConfigured: emailConfigured));

// accounts-identity/03 + platform-devops/08 (durable key ring, #199): ASP.NET Core
// Data Protection, used by PurchaserCredentialService to mint the SHORT-LIVED
// purchaser sign-in credential AND by OperatorSession for the operator admin-session
// credential (both under their own dedicated purpose strings). No key material is
// ever a committed literal or a VITE_* var (AC-06 / AC-01).
//
// DURABLE, SHARED KEY RING when deployed (platform-devops/08 AC-01): extends the
// config-presence idiom used throughout this file (Telemetry, Stripe, Email). When
// BOTH a key-ring blob URI (DataProtection:KeyRingBlobUri) AND a Key Vault key URI
// (DataProtection:KeyVaultKeyUri) are configured, the key ring PERSISTS to Azure Blob
// Storage and is ENCRYPTED AT REST by a Key Vault key - so a purchaser or operator
// credential minted before an app restart or a scale-out event still verifies after
// it (AC-03). Both URIs are non-secret pointers (not key material); auth is KEYLESS
// via the SAME DefaultAzureCredential the ACS email path already uses (the App
// Service SystemAssigned identity, granted "Storage Blob Data Contributor" on the
// container + "Key Vault Crypto User" on the key in infra/main.bicep). Each lane
// (qa / beta) points at its OWN Storage account + Key Vault, so its key ring backing
// is DISTINCT and a qa-minted credential never verifies against beta (ADR 0003
// "Credentials survive scale-out safely").
//
// FAIL CLOSED in a deployed environment (AC-08): when the durable backing is absent
// we branch on environment. In Development (local dev / CI) we leave the bare default
// in-process key ring exactly as before - zero Azure setup, no behavior change
// (AC-02). In ANY other environment we THROW at startup naming the missing config,
// rather than silently reverting to per-instance keys - a silent fallback there would
// invalidate every outstanding purchaser + operator credential on the next
// restart / scale-out with no error to point at (a self-inflicted lockout). The
// consumed-nonce store (AC-07) is folded into the SAME guard: a shared signing key
// with a per-process nonce set is the exact replay gap that story closes, so a
// deployed environment must configure Accounts:StorageConnectionString too.
//
// CRITICAL boundary (accounts-identity AC-03/AC-04): the purchaser credential is
// consumed ONLY by the purchaser sign-in / restore surface - never required by, nor
// checked in, GameHub or any player-facing endpoint, so free play stays login-free.
var dpBlobUri = builder.Configuration["DataProtection:KeyRingBlobUri"];
var dpKeyVaultKeyUri = builder.Configuration["DataProtection:KeyVaultKeyUri"];
var dpKeyRingConfigured = !string.IsNullOrWhiteSpace(dpBlobUri) && !string.IsNullOrWhiteSpace(dpKeyVaultKeyUri);

// FAIL CLOSED first (AC-08): in ANY deployed (non-Development) environment the FULL
// durable backing must be present - the key ring (Blob + Key Vault) AND the shared
// nonce store (Accounts:StorageConnectionString). We check the nonce connection string
// here too because a durable, SHARED signing key paired with a per-process nonce set is
// the exact replay-once-per-instance gap AC-07 closes: allowing the key ring durable but
// the nonce store in-memory would silently reopen it. So the deployed environment either
// has ALL of it, or the app refuses to start (never a silent partial degrade). Local
// development (AC-02) is exempt and needs no configuration.
if (!builder.Environment.IsDevelopment() &&
    (!dpKeyRingConfigured || string.IsNullOrWhiteSpace(accountsConnectionStringForNonces)))
{
    // Name the exact missing configuration so the failure is actionable (no secret is
    // interpolated - these are non-secret URIs / config KEY names, not the values).
    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(dpBlobUri)) missing.Add("DataProtection:KeyRingBlobUri");
    if (string.IsNullOrWhiteSpace(dpKeyVaultKeyUri)) missing.Add("DataProtection:KeyVaultKeyUri");
    if (string.IsNullOrWhiteSpace(accountsConnectionStringForNonces)) missing.Add("Accounts:StorageConnectionString");
    throw new InvalidOperationException(
        $"Durable Data Protection key ring is not configured in environment '{builder.Environment.EnvironmentName}'. " +
        $"Missing: {string.Join(", ", missing)}. A deployed environment MUST persist the key ring to durable, " +
        "shared storage (Blob + Key Vault) AND back the single-use magic-link nonce set with the same shared store, " +
        "so purchaser and operator credentials survive restart / scale-out and a single-use link cannot be replayed " +
        "per instance; the app refuses to start rather than silently fall back to per-instance keys (platform-devops/08 " +
        "AC-08). See infra/main.bicep and docs/runbooks/enable-magic-link-email.md. In local development this fallback " +
        "is intentional and no configuration is required.");
}

if (dpKeyRingConfigured)
{
    // Keyless: the same DefaultAzureCredential AcsEmailSender uses (the App Service
    // managed identity in a deployed environment). No stored key material beyond the
    // two non-secret URIs.
    var dataProtectionCredential = new DefaultAzureCredential();
    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(new Uri(dpBlobUri!), dataProtectionCredential)
        .ProtectKeysWithAzureKeyVault(new Uri(dpKeyVaultKeyUri!), dataProtectionCredential);
}
else
{
    // Only reachable in Development (a deployed environment without the durable backing
    // already threw above): the framework's default in-process key ring, unchanged from
    // before this story - zero Azure setup for local dev / CI (AC-02).
    builder.Services.AddDataProtection();
}

// accounts-identity/03 + billing-entitlements/05: the ONE purchaser-credential
// minter+resolver over Data Protection. AccountsController mints it on sign-in;
// EntitlementsController (the restore/manage read endpoint) resolves it - both share
// this so the credential purpose + lifetime live in exactly one place (story 05 AC-06:
// reuse the guard, do not write a second auth check). A singleton over the protector.
builder.Services.AddSingleton<PurchaserCredentialService>();

// sysadmin-console/01 (#135): the SEPARATE, auth-gated operator back office.
// Every edit in this block is ADDITIVE and localized here (a parallel feature,
// billing-entitlements, edits Program.cs in the entitlement-registration region -
// keep this seam small). This wires: (1) the config-backed operator allowlist, and
// (2) the FIRST AddAuthentication / AddAuthorization in this app.
//
// THE LOAD-BEARING BOUNDARY (AC-03): the "Operator" authorization policy binds ONLY
// to the "Operator" authentication scheme, whose handler authenticates a request
// ONLY when it presents a credential that unprotects under the DEDICATED operator
// Data Protection purpose (distinct from the purchaser purpose) AND carries an
// allowlisted email. A purchaser credential fails the unprotect by construction, so
// "signed in as some purchaser" can NEVER satisfy an admin endpoint. There is NO
// default scheme, so no player-facing / purchaser endpoint (none carry [Authorize])
// is affected - the game path stays 100% auth-free.
//
// The allowlist is operator-only configuration (Operator:AllowedEmails), Key
// Vault-backed when deployed - NEVER a VITE_* var, never committed, never inferred
// from player / purchaser data (AC-05). Singleton: it holds only IConfiguration and
// reads the list live so an operator add / remove is a config change (AC-05).
builder.Services.AddSingleton<IOperatorAllowlist, ConfigurationOperatorAllowlist>();
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, OperatorAuthenticationHandler>(
        OperatorSession.AuthenticationScheme, configureOptions: null);
builder.Services.AddAuthorization(options =>
{
    // NEVER a bare RequireAuthenticatedUser over the default scheme - the policy
    // pins the Operator scheme explicitly so only an operator credential (not any
    // future authenticated principal) can satisfy it (AC-03). Admin endpoints
    // authorize via [Authorize(Policy = OperatorSession.PolicyName)].
    options.AddPolicy(OperatorSession.PolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(OperatorSession.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });

    // sysadmin-console/05 (#214): the three SCOPED operator policies (Support / Content
    // / Ops). Each pins the SAME Operator scheme + RequireAuthenticatedUser as the base
    // policy (so only a valid operator credential authenticates - the story 01 boundary
    // is unchanged) AND adds an OperatorScopeRequirement, so a de-scoped future operator
    // is rejected at the policy layer (a 403), not by convention. TODAY the single
    // operator holds all three scopes (ConfigurationOperatorAllowlist.ScopesFor's
    // all-three default for an operator with no Operator:Scopes entry), so every scope
    // requirement succeeds and the existing controller test suites pass UNMODIFIED
    // (AC-05). This is additive metadata - the base "Operator" policy above still exists
    // for the session echo and any un-scoped admin surface.
    foreach (var scope in Enum.GetValues<OperatorScope>())
    {
        options.AddPolicy(OperatorScopePolicy.For(scope), policy =>
        {
            policy.AddAuthenticationSchemes(OperatorSession.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new OperatorScopeRequirement(scope));
        });
    }
});
// The handler that resolves an operator's scope set (from the config-backed allowlist)
// and evaluates each OperatorScopeRequirement (sysadmin-console/05). Singleton: it holds
// only the singleton IOperatorAllowlist and is otherwise stateless.
builder.Services.AddSingleton<IAuthorizationHandler, OperatorScopeHandler>();

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

// sysadmin-console/01 (#135): authenticate then authorize, in that order, before the
// endpoints run. Added here (after UseCors, before the endpoints) as the FIRST auth
// middleware in this app. Both are INERT for every current REST / hub route (none
// carry [Authorize]); they only engage where an endpoint opts in - today just the
// operator console's [Authorize(Policy = "Operator")] session echo (AC-03/AC-06).
app.UseAuthentication();
app.UseAuthorization();

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

    /// <summary>
    /// control-plane/03 (#232, AC-08): read a rate-limit-permit knob from the runtime
    /// settings service resolved off the request's <see cref="HttpContext.RequestServices"/>
    /// (NOT a value closed over at AddRateLimiter registration time, so a newly-created
    /// partition picks up the latest override, AC-06) and CLAMP it into
    /// <c>[<see cref="SettingsCatalog.RateLimitPermitClampMin"/>, <see cref="SettingsCatalog.RateLimitPermitClampMax"/>]</c>
    /// immediately before it is handed to <see cref="FixedWindowRateLimiterOptions.PermitLimit"/>.
    ///
    /// The clamp is the independent safety net for this specific crash mode:
    /// <c>PermitLimit &lt;= 0</c> THROWS inside the partition-factory lambda, which the
    /// rate-limiter middleware surfaces as a 500 on every request against that partition (an
    /// outage, not a graceful degrade), and an absurdly large value would silently disable
    /// the limiter. It must exist even though story 01's catalog <c>Bounds</c> already
    /// rejects a bad PUT - belt AND suspenders, so a bad value can never reach the read site
    /// via a stale cache, a race, or a future key added without a catalog bound.
    ///
    /// The read is synchronous by necessity (the partition-factory lambda is synchronous);
    /// the settings service's short cache keeps it cheap and these are low-traffic operator /
    /// AI surfaces, never a gameplay hot path. On any read failure it degrades to the code
    /// default rather than crash the limiter.
    ///
    /// Exposed on the public <see cref="Program"/> partial (like <see cref="AiPerIpRateLimitPolicy"/>)
    /// so the read-site clamp (AC-08) is unit-testable directly: a settings value of 0, a
    /// negative, and an absurdly large value must each produce an in-range permit count.
    /// </summary>
    public static int ClampedRateLimitPermits(HttpContext httpContext, string settingsKey, int codeDefault)
    {
        int permits;
        try
        {
            var settings = httpContext.RequestServices.GetRequiredService<IRuntimeSettingsService>();
            permits = settings.GetIntAsync(settingsKey, httpContext.RequestAborted).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Never let a settings-read failure crash the limiter - fall back to the code
            // default, which the clamp below then guarantees is in-range anyway.
            permits = codeDefault;
        }

        return Math.Clamp(permits, SettingsCatalog.RateLimitPermitClampMin, SettingsCatalog.RateLimitPermitClampMax);
    }
}
