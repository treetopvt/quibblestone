// ----------------------------------------------------------------------------
//  AiOptions - the ONE bound configuration shape for the AI cost gate.
//
//  WHAT THIS IS: the strongly-typed view of the `Ai:*` configuration section
//  (appsettings / env / user-secrets / Key Vault-backed App Service settings).
//  It is where the model id AND the per-model $-per-1M-token rate constants live
//  TOGETHER so that swapping the deployed model is a ONE-PLACE config change
//  (ADR 0001 decision A: gpt-4o-mini now, swappable to gpt-4.1-nano later). The
//  spend circuit-breaker (story 04) reads InputCostPerMillion / OutputCostPer
//  Million from here to estimate $ per call from story 01's returned token usage;
//  this story just defines WHERE those rates live.
//
//  WHY A BOUND OPTIONS TYPE (not scattered Configuration["Ai:..."] reads): a
//  single typed surface keeps the model id and its rates from drifting apart, and
//  gives every later gate story ONE thing to inject.
//
//  SECRETS POSTURE (AC-03, non-negotiable): the provider ENDPOINT and DEPLOYMENT
//  are non-secret config and may sit in appsettings; the optional API KEY is a
//  SECRET and MUST arrive from user-secrets / env / a Key Vault-backed app setting
//  ONLY - NEVER a committed literal, and NEVER a VITE_* var (VITE_ vars ship to
//  the browser). Preferred auth is the App Service managed identity (no key at
//  all); the key is the simpler fallback (see FoundryAiCompletionClient).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// Bound view of the <c>Ai</c> configuration section. Presence of
/// <see cref="Endpoint"/> is the single switch Program.cs branches on to register
/// the real Foundry-backed proxy versus the no-op (mirrors the ITelemetrySink
/// config-presence idiom). Rates live here beside the model so a model swap is one
/// config change (ADR 0001).
/// </summary>
public sealed class AiOptions
{
    /// <summary>The configuration section name (<c>Ai</c>).</summary>
    public const string SectionName = "Ai";

    /// <summary>
    /// The Azure AI Foundry (Azure OpenAI) endpoint URI, e.g.
    /// <c>https://my-foundry.openai.azure.com/</c>. Non-secret. Its PRESENCE is the
    /// switch that selects the real client over the no-op (AC-04). Absent =&gt; the
    /// app runs with the no-op and zero AI config.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The deployed model / deployment name, e.g. <c>gpt-4o-mini</c> (ADR 0001
    /// decision A). Non-secret. Also surfaced back on <see cref="AiCompletionResult.ModelId"/>
    /// so cost attribution (story 04) records which model a call used.
    /// </summary>
    public string Deployment { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// OPTIONAL provider API key (AC-03). A SECRET - supplied from user-secrets /
    /// env / a Key Vault-backed app setting, NEVER committed, NEVER a VITE_* var.
    /// When null/empty the client authenticates with <c>DefaultAzureCredential</c>
    /// (the preferred managed-identity path); when present it is the simpler
    /// AzureKeyCredential fallback.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Per-call wall-clock timeout in seconds (AC-06). A dropped client or a shed
    /// round must not leak a long-running provider call; the proxy caps every call
    /// at this budget and fails soft to <see cref="AiCompletionResult.Unavailable"/>
    /// on expiry. Kept small because the jumble payload is sub-second.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Input token price in USD per 1,000,000 tokens for the deployed model (ADR
    /// 0001: gpt-4o-mini = 0.15). Story 04's estimator reads this beside
    /// <see cref="OutputCostPerMillion"/>; kept next to <see cref="Deployment"/> so
    /// a model swap updates the id AND its rates in one place.
    /// </summary>
    public decimal InputCostPerMillion { get; set; } = 0.15m;

    /// <summary>
    /// Output token price in USD per 1,000,000 tokens for the deployed model (ADR
    /// 0001: gpt-4o-mini = 0.60). See <see cref="InputCostPerMillion"/>.
    /// </summary>
    public decimal OutputCostPerMillion { get; set; } = 0.60m;

    /// <summary>
    /// The per-anonymous-session AI call quota (ai-cost-gate/03, AC-01) - how many
    /// AI units ("Fresh Runes") one session (keyed by the anonymous Room.InstanceId)
    /// may spend before it degrades to the deterministic fallback. Bound from
    /// <c>Ai:QuotaPerSession</c> so N lives in ONE config place, never scattered
    /// literals. The alpha default is a sensible, generous-but-bounded ceiling: enough
    /// to play with the AI runes across a session, low enough that a replaying client
    /// cannot multiply spend without bound. <see cref="AiQuota"/> reads this; the
    /// remaining count is server-authoritative and threaded to the client meter. A
    /// value &lt;= 0 means "no allowance" (every call falls back), which is the
    /// fail-safe side, never "unlimited".
    /// </summary>
    public int QuotaPerSession { get; set; } = 20;
}
