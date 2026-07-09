// ----------------------------------------------------------------------------
//  PiiScrubbingTelemetryInitializer - the ONE choke point every piece of
//  Application Insights telemetry flows through before it leaves the process
//  (platform-devops/04, AC-04 - the whole point of this story).
//
//  WHY THIS EXISTS (non-negotiable, README section 6): QuibbleStone's players are
//  anonymous minors (join code + nickname, no account, no PII). Operational
//  telemetry must NEVER carry a person or their content - not a nickname (free
//  text), not a join code, not a player session id, not a submitted word, not
//  story text, and not an IP-derived identity. Rather than trust every call site
//  to remember that, we register a SINGLE ITelemetryInitializer as the choke
//  point: the App Insights SDK runs EVERY ITelemetry item (requests, exceptions,
//  dependencies, custom events / traces - including story-05's FUTURE anonymous
//  usage events and the client-error beacon that forwards through TelemetryClient)
//  through Initialize() before send. If it is scrubbed here, it is scrubbed
//  everywhere - there is no second path out.
//
//  WHAT IT DOES (defence in depth - only anonymous operational data survives):
//    1. Zero the client IP on EVERY item (Context.Location.Ip = "0.0.0.0"). An IP
//       is an identity-adjacent identifier and the default request pipeline would
//       otherwise capture it. We never need it for operational health.
//    2. For a RequestTelemetry, strip the URL QUERY STRING and keep only the
//       path/route. A query string is the most likely accidental carrier of a
//       nickname / code / word (e.g. ?name=... on a crafted request), so we drop
//       it wholesale and keep just the route template + status + duration.
//    3. Defensively DROP any known-sensitive custom-property key (nickname, join
//       code, room code, player/session/connection id, word, answer, story text)
//       from a telemetry item's Properties bag - belt-and-braces in case a future
//       call site ever attaches one by mistake. The allowed shape is anonymous
//       operational data ONLY: route templates, status codes, durations, exception
//       types/stacks, dependency names.
//
//  This class carries NO game logic and holds NO state - it is a pure, allocation
//  -light transform run on the hot telemetry path, registered once as a singleton
//  in Program.cs. It sits ALONGSIDE (never replaces) the anonymous serve-log sink
//  (ITelemetrySink / Table Storage, story-selection/04) - that is a separate
//  content-curation pipeline with its own no-PII guarantee.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// The SINGLE PII/content scrubber every App Insights telemetry item passes
/// through before send (AC-04). Zeroes the client IP, strips request query
/// strings (keeping only the route/path), and defensively drops any
/// known-sensitive custom-property key. Only anonymous operational data (route
/// templates, status codes, durations, exception types/stacks, dependency names)
/// leaves the process. Registered as a singleton ITelemetryInitializer in
/// Program.cs; story-05's future usage events and the client-error beacon both
/// flow through this same choke point.
/// </summary>
public sealed class PiiScrubbingTelemetryInitializer : ITelemetryInitializer
{
    // The anonymized IP we stamp onto every item: App Insights still records a
    // (meaningless) location, but never the caller's real address.
    private const string AnonymizedIp = "0.0.0.0";

    // Custom-property keys that must NEVER ride telemetry (case-insensitive).
    // This is defence in depth - no current call site attaches these - so that a
    // future one cannot silently leak identity or content through the Properties
    // bag. Covers the identifiers (join/room code, player/session/connection id)
    // and the free-text/content carriers (nickname, word, answer, story text).
    //
    // NOTE (platform-devops/05, AC-03): "deviceId" is deliberately NOT in this set.
    // The anonymous product-usage events attach a device-local random GUID as
    // "deviceId" so approximate device REACH is answerable - it is a DEVICE-count
    // key, explicitly anonymous (no account, reset on storage clear), NOT a
    // "player session id" (that stays "sessionId"/"connectionId" above, still
    // dropped). Do not add "deviceId" here or reach telemetry goes dark. The
    // "mode" / "context" / "playerCount" usage keys are likewise anonymous by
    // construction (a stable enum-ish label, solo/group, and a count).
    private static readonly HashSet<string> SensitivePropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "nickname", "name", "displayName",
        "code", "joinCode", "roomCode",
        "playerId", "sessionId", "connectionId",
        "word", "answer", "story", "storyText", "text",
        "ip", "ipAddress",
        // keepsake-vault/01 (#196, ADR 0003 "Handles are secrets"): the vault id is a
        // bearer credential - it must never ride telemetry through the Properties bag
        // (it travels in the X-Vault-Id header, never a query string / path, so the
        // path/query scrub above already covers the request URL; this is the
        // belt-and-braces for a custom property).
        "vaultId",
        // keepsake-vault/03 (#230) + accounts-identity (ADR 0003 "Telemetry knows the
        // new identifiers"): the claim code is a bearer secret (like the vault id); the
        // account id / email / credential tokens are account-plane identity. None may
        // ride telemetry through a custom property (belt-and-braces for a future call
        // site). The device-link token (accounts-identity/09) is the newest of these -
        // see "deviceToken" below.
        "claimCode", "accountId", "email",
        "token", "access_token", "deviceToken",
    };

    /// <summary>
    /// Scrubs one telemetry item in place before the SDK sends it. Safe to run on
    /// every item type; only touches the IP, the request query string, and any
    /// sensitive property keys - it never inspects or logs a value.
    /// </summary>
    /// <param name="telemetry">The telemetry item about to be sent.</param>
    public void Initialize(ITelemetry telemetry)
    {
        // 1. Zero the client IP on EVERY item (AC-04) - never an IP-derived identity.
        telemetry.Context.Location.Ip = AnonymizedIp;

        // 1a. Clear the App Insights context IDENTITY identifiers (AC-04). The
        //     ASP.NET Core SDK can populate stable anonymous User.Id / Session.Id
        //     (from correlation / a cookie) - benign in most apps, but our players
        //     are anonymous minors and telemetry must never carry a per-user or
        //     per-session identifier. Product-usage REACH (story 05) is answered by
        //     our OWN explicit, anonymous "deviceId" property, never by these, so
        //     clearing them costs nothing and closes the identity channel for good.
        telemetry.Context.User.Id = null;
        telemetry.Context.User.AuthenticatedUserId = null;
        telemetry.Context.User.AccountId = null;
        telemetry.Context.Session.Id = null;

        // 2. For a request, keep ONLY the route/path and drop the query string,
        //    which is the likeliest accidental carrier of a nickname / code / word.
        if (telemetry is RequestTelemetry request)
        {
            StripQueryString(request);
        }

        // 3. Defensively drop any known-sensitive custom-property key from the
        //    item's Properties bag (belt-and-braces, in case a future call site
        //    ever attaches one). Only anonymous operational data may survive.
        if (telemetry is ISupportProperties withProperties)
        {
            ScrubProperties(withProperties);
        }
    }

    /// <summary>
    /// Removes the query string from a request's URL and name, keeping only the
    /// path/route. Operates defensively - a null/relative URL is left as-is.
    /// </summary>
    private static void StripQueryString(RequestTelemetry request)
    {
        var url = request.Url;
        if (url is not null && !string.IsNullOrEmpty(url.Query))
        {
            // Rebuild with the query (and fragment) removed - path only.
            var builder = new UriBuilder(url)
            {
                Query = string.Empty,
                Fragment = string.Empty,
            };
            request.Url = builder.Uri;
        }

        // The request NAME can also embed a raw "?..." on some capture paths;
        // trim anything from the first '?' so the operation name stays a route.
        var name = request.Name;
        if (!string.IsNullOrEmpty(name))
        {
            var queryIndex = name.IndexOf('?');
            if (queryIndex >= 0)
            {
                request.Name = name[..queryIndex];
            }
        }
    }

    /// <summary>
    /// Drops every known-sensitive custom-property key from the item's Properties
    /// bag (AC-04). We remove the whole key rather than mask the value so nothing
    /// identity- or content-bearing can leak, even partially.
    /// </summary>
    private static void ScrubProperties(ISupportProperties withProperties)
    {
        var properties = withProperties.Properties;
        if (properties.Count == 0)
        {
            return;
        }

        // Collect first, then remove - the Properties dictionary must not be
        // mutated while enumerated.
        List<string>? toRemove = null;
        foreach (var key in properties.Keys)
        {
            if (SensitivePropertyKeys.Contains(key))
            {
                (toRemove ??= new List<string>()).Add(key);
            }
        }

        if (toRemove is null)
        {
            return;
        }

        foreach (var key in toRemove)
        {
            properties.Remove(key);
        }
    }
}
