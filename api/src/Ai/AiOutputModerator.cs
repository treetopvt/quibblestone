// ----------------------------------------------------------------------------
//  AiOutputModerator - the REAL moderate-before-display gate (ai-cost-gate story 05,
//  issue #124). Replaces PassthroughAiOutputModerator as the last stage of the
//  GatedAiCompletionClient pipeline: EVERY AI-sourced item passes here BEFORE any
//  child sees it (README section 6, non-negotiable; ADR 0001 decision B).
//
//  WHAT IT DOES, per item, in order:
//    1. HARD GATE (AC-01): run the item through the existing server-side
//       IContentSafetyFilter.CheckAsync - the SAME deterministic blocklist +
//       normalization gate every player free-text surface uses (child-safety/01).
//       CheckAsync was made async from day one precisely so this is a drop-in. An
//       item that fails is DROPPED, never returned, never shown or made tappable.
//    2. FAMILY-SAFE (AC-02): when the round's family-safe toggle is on, apply the
//       STRICTER family-safe standard too, routing the per-word decision through the
//       ONE family-safe selection RULE (FamilySafeContentSelector) so the drop
//       semantics are single-sourced with the curated-content gate (child-safety/02).
//    3. CONTENT SAFETY (AC-05): pass survivors through the OPTIONAL, config-gated
//       Azure AI Content Safety second layer (IAiContentSafetyScreen). Absent config
//       => the NoOp screen allows everything and behavior equals the hard filter
//       alone; present config => a real drop-in second layer (story 06). A config
//       flip, not a code change.
//
//  BATCH SEMANTICS (AC-04): drop-and-continue. Keep the safe survivors, drop the
//  unsafe, and return them PLUS a Sufficient flag telling the caller whether enough
//  safe items remain to use. Too few (below the small MinimumSafeItems floor) =>
//  Sufficient=false so the caller degrades to its deterministic fallback (the jumble
//  reshuffle) rather than show a thin or empty set. INVARIANT: an empty Safe list is
//  NEVER Sufficient=true.
//
//  NO EVASION TEACHING, NO PII (AC-06): the result carries ONLY the safe survivors +
//  the sufficiency flag - never WHICH item failed or WHY. Rejections are counted
//  ANONYMOUSLY (a number in a Debug log line only); the rejected TEXT is never
//  logged, sampled, echoed, or persisted, and no player identity is ever touched
//  (README section 6). The friendly "no fresh runes right now" fallback is the
//  caller's concern (the gate just signals insufficient).
//
//  NOT SKIPPABLE (AC-07): this stage lives inside the server proxy pipeline
//  (GatedAiCompletionClient calls it non-optionally). There is no code path that
//  returns AI output without passing steps 1 (+2 for family-safe). Curated, pre-
//  vetted content still skips the filter UNCHANGED (game-modes/04) - this is AI-
//  SOURCED output ONLY.
//
//  REUSABLE, NOT JUMBLE-SPECIFIC (AC-03): the seam is a general "moderate this AI
//  output before display" service - a list of strings in, the safe subset out. No
//  word-bank / category / jumble shape is baked in; the jumble (and later the
//  verdict, on-demand tales, packs) CONSUMES it.
//
//  Stateless after construction (its embedded family-safe term set is built once),
//  so it is registered as a DI singleton (Program.cs).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The real <see cref="IAiOutputModerator"/> (story 05): composes the always-on hard
/// gate (<see cref="IContentSafetyFilter"/>), the family-safe rule
/// (<see cref="FamilySafeContentSelector"/>), and the optional config-gated Content
/// Safety second layer (<see cref="IAiContentSafetyScreen"/>) over every AI item, and
/// reports whether enough safe items survived. Registered as the default
/// <see cref="IAiOutputModerator"/> in Program.cs, replacing the pass-through seam.
/// </summary>
public sealed class AiOutputModerator : IAiOutputModerator
{
    /// <summary>
    /// The default "enough safe items to use" floor for the generic seam. A small,
    /// documented const: at least this many survivors are required for
    /// <see cref="AiModerationResult.Sufficient"/> to be true, so the caller degrades
    /// to its deterministic fallback rather than show a thin set (AC-04). A consumer
    /// that wants a richer set (the jumble wants ~6-10 words) may raise its own floor
    /// by constructing with a larger <c>minimumSafeItems</c>; 1 is the universal
    /// minimum (never an empty set counted as sufficient).
    /// </summary>
    public const int DefaultMinimumSafeItems = 1;

    // The MINIMAL, DOCUMENTED per-word family-safe-sensitive term set (AC-02).
    //
    // The existing FamilySafeContentSelector reads an AUTHORED FamilySafe flag off
    // curated catalog entries - it has no signal for an arbitrary free WORD. So the
    // per-word family-safe SIGNAL here is a small in-code set of terms that PASS the
    // profanity filter (they are not slurs / profanity) but are not kid-appropriate
    // for a family-safe round (mild violence / weapons / intoxication / cruelty).
    // This is DELIBERATELY minimal and defensible for a reusable SEAM: the real
    // family-safe wordlist lands with the first real consumer (ai-on-demand-
    // generation/05) and its family-safe content work. See OPEN_QUESTIONS. Terms are
    // NORMALIZED (via ContentSafetyFilter.Normalize) at construction so the match is
    // apples-to-apples with candidate tokens, exactly like the blocklist.
    private static readonly string[] FamilySafeSensitiveSeed =
    [
        "kill", "kills", "killed", "gun", "guns", "knife", "knives", "blood", "bloody",
        "dead", "death", "die", "dies", "weapon", "weapons", "drunk", "beer", "wine",
        "hate", "stupid", "dumb",
    ];

    private readonly IContentSafetyFilter _safety;
    private readonly FamilySafeContentSelector _familySafe;
    private readonly IAiContentSafetyScreen _contentSafety;
    private readonly ILogger<AiOutputModerator> _logger;
    private readonly int _minimumSafeItems;
    private readonly HashSet<string> _familySafeSensitiveTerms;

    /// <summary>
    /// Builds the moderator from the always-on hard gate, the family-safe rule, the
    /// optional Content Safety screen, and a logger for anonymous rejection counts.
    /// </summary>
    /// <param name="safety">The single server-side content safety filter (child-safety/01) - the hard gate on every item (AC-01).</param>
    /// <param name="familySafe">The family-safe selection rule (child-safety/02) - the single-sourced drop semantics for family-safe rounds (AC-02).</param>
    /// <param name="contentSafety">The optional, config-gated Content Safety second layer (AC-05). NoOp by default.</param>
    /// <param name="logger">For anonymous rejection COUNTS only - never the rejected text, never PII (AC-06).</param>
    /// <param name="minimumSafeItems">The "enough left" floor (AC-04). Defaults to <see cref="DefaultMinimumSafeItems"/>.</param>
    public AiOutputModerator(
        IContentSafetyFilter safety,
        FamilySafeContentSelector familySafe,
        IAiContentSafetyScreen contentSafety,
        ILogger<AiOutputModerator> logger,
        int minimumSafeItems = DefaultMinimumSafeItems)
    {
        _safety = safety;
        _familySafe = familySafe;
        _contentSafety = contentSafety;
        _logger = logger;
        _minimumSafeItems = minimumSafeItems < 1 ? 1 : minimumSafeItems;

        // Normalize the family-safe-sensitive seed the same way candidate tokens are,
        // so membership tests are apples-to-apples (mirrors ContentSafetyFilter).
        _familySafeSensitiveTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in FamilySafeSensitiveSeed)
        {
            var normalized = ContentSafetyFilter.Normalize(term);
            if (normalized.Length > 0)
            {
                _familySafeSensitiveTerms.Add(normalized);
            }
        }
    }

    /// <inheritdoc />
    public async Task<AiModerationResult> ModerateAsync(
        IReadOnlyList<string> items,
        bool familySafe,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            // Nothing to show: an empty set is NEVER sufficient (AC-04 invariant).
            return new AiModerationResult(Array.Empty<string>(), Sufficient: false);
        }

        var safe = new List<string>(items.Count);
        var dropped = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An empty / whitespace item carries nothing displayable - drop it quietly
            // (it is not a moderation "rejection" to sample, just noise).
            if (string.IsNullOrWhiteSpace(item))
            {
                dropped++;
                continue;
            }

            // Step 1 - HARD GATE (AC-01): the same deterministic blocklist gate every
            // free-text surface uses. Fail => drop, never return.
            var verdict = await _safety.CheckAsync(item, cancellationToken).ConfigureAwait(false);
            if (!verdict.IsAllowed)
            {
                dropped++;
                continue;
            }

            // Step 2 - FAMILY-SAFE (AC-02): only when the toggle is on, apply the
            // stricter standard through the ONE family-safe selection rule.
            if (familySafe && !IsFamilySafeWord(item))
            {
                dropped++;
                continue;
            }

            // Step 3 - CONTENT SAFETY (AC-05): the optional second layer. NoOp (allow)
            // by default, so with no config this is a no-op and behavior equals the
            // hard filter alone.
            if (!await _contentSafety.IsAllowedAsync(item, familySafe, cancellationToken).ConfigureAwait(false))
            {
                dropped++;
                continue;
            }

            safe.Add(item);
        }

        // AC-06: sample rejections ANONYMOUSLY - a COUNT only, never the text, never
        // which item or why, never PII. This is the only trace a rejection leaves.
        if (dropped > 0)
        {
            _logger.LogDebug(
                "AI moderation dropped {DroppedCount} of {TotalCount} items (familySafe={FamilySafe}).",
                dropped,
                items.Count,
                familySafe);
        }

        // AC-04: enough left? Below the floor (or empty) => the caller degrades to its
        // deterministic fallback. An empty set is NEVER sufficient.
        var sufficient = safe.Count > 0 && safe.Count >= _minimumSafeItems;
        return new AiModerationResult(safe, sufficient);
    }

    /// <summary>
    /// The per-word family-safe decision (AC-02). Computes the minimal per-word
    /// family-safe SIGNAL (below) and routes the keep/drop decision through the ONE
    /// family-safe selection RULE (<see cref="FamilySafeContentSelector"/>) so a
    /// family-safe AI round drops non-family-safe output by exactly the same rule the
    /// curated catalog gate uses - the drop semantics stay single-sourced. Returns
    /// true if the item may survive a family-safe round.
    /// </summary>
    private bool IsFamilySafeWord(string item)
    {
        var signal = ComputeFamilySafeSignal(item);

        // Route the per-word signal through the shared family-safe RULE: model the item
        // as a one-entry catalog carrying the computed FamilySafe flag and ask the
        // selector (with the toggle ON) whether it survives. Keeps the family-safe DROP
        // semantics identical to curated content (child-safety/02) instead of forking a
        // second rule here. The SIGNAL is minimal (see the seed set); the RULE is shared.
        var probe = new[] { new TemplateCatalogEntry(item, FamilySafe: signal, BlankCount: 0) };
        return _familySafe.SelectAllowed(probe, familySafeOn: true).Count > 0;
    }

    /// <summary>
    /// The minimal per-word family-safe SIGNAL: false if any normalized token of the
    /// item is in the family-safe-sensitive set (mild-but-not-kid terms that pass the
    /// profanity filter), true otherwise. Whole-token match (like the blocklist), so
    /// an innocent word that merely CONTAINS a sensitive term as a substring is not
    /// caught. Minimal and documented - hardened when the real consumer + family-safe
    /// wordlist land (OPEN_QUESTIONS).
    /// </summary>
    private bool ComputeFamilySafeSignal(string item)
    {
        if (_familySafeSensitiveTerms.Count == 0)
        {
            return true;
        }

        foreach (var token in item.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = ContentSafetyFilter.Normalize(token);
            if (normalized.Length > 0 && _familySafeSensitiveTerms.Contains(normalized))
            {
                return false;
            }
        }

        // Separator-obfuscation net (mirrors ContentSafetyFilter): collapse the whole
        // item and test it as one token too.
        var collapsed = ContentSafetyFilter.Normalize(item);
        if (collapsed.Length > 0 && _familySafeSensitiveTerms.Contains(collapsed))
        {
            return false;
        }

        return true;
    }
}
