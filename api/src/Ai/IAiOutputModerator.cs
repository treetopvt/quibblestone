// ----------------------------------------------------------------------------
//  IAiOutputModerator - the moderate-before-display seam (ai-cost-gate STORY 05,
//  issue #124). Defined here in story 01 as a PASS-THROUGH seam so the gate
//  pipeline compiles and runs green with zero config; story 05 replaces the
//  default with the real IContentSafetyFilter + family-safe composition.
//
//  WHAT THIS IS (story 05's job): the LAST gate stage. EVERY AI-sourced item is
//  vetted BEFORE any child sees it (README section 6, non-negotiable): run each
//  item through the existing server-side IContentSafetyFilter AND the family-safe
//  gate, DROP the unsafe ones, keep the survivors, and signal whether ENOUGH safe
//  items remain so the caller can fall back to the deterministic path if too many
//  were dropped. There is NO bypass path - curated (non-AI) content still skips the
//  filter unchanged, but AI output NEVER does.
//
//  DANGER - THE PASS-THROUGH DEFAULT IS NEVER SHIPPED TO A REAL AI CONSUMER. The
//  PassthroughAiOutputModerator below returns every item unmoderated. It exists
//  SOLELY so this seam compiles before story 05 lands. Shipping it in front of a
//  real AI output would put UNMODERATED model text in front of children - which the
//  charter forbids. Story 05 (#124) MUST replace it before ai-on-demand-generation/
//  05 (the first real consumer) goes live.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The moderate-before-display gate over AI output (story 05). The gate calls
/// <see cref="ModerateAsync"/> on the raw AI-produced items; it returns only the
/// items that pass the safety filter + family-safe gate, plus a
/// <see cref="AiModerationResult.Sufficient"/> flag telling the caller whether
/// enough safe items survived to use (vs. fall back). Story 05 supplies the real
/// composition of <c>IContentSafetyFilter</c> + family-safe; story 01 ships only
/// the contract + the <see cref="PassthroughAiOutputModerator"/> default (which
/// story 05 REPLACES and which is never shipped to a real consumer).
/// </summary>
public interface IAiOutputModerator
{
    /// <summary>
    /// Moderates AI-produced items before display. Drops unsafe items, keeps
    /// survivors, and reports whether enough remain to use.
    /// </summary>
    /// <param name="items">The raw AI-produced items (e.g. candidate words). Never shown before this runs.</param>
    /// <param name="familySafe">The round's family-safe toggle position - tightens the gate when true.</param>
    /// <param name="cancellationToken">Cancellation for the (possibly remote, story 05) checks.</param>
    /// <returns>The safe survivors + a sufficiency flag.</returns>
    Task<AiModerationResult> ModerateAsync(IReadOnlyList<string> items, bool familySafe, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of <see cref="IAiOutputModerator.ModerateAsync"/>: the safe
/// survivors and whether there are enough of them to use. When
/// <see cref="Sufficient"/> is false the caller degrades to the deterministic
/// fallback (README section 6: never show unsafe or too-thin AI output).
/// </summary>
/// <param name="Safe">The items that passed moderation - the only items a caller may display.</param>
/// <param name="Sufficient">True if enough safe items survived to use; false =&gt; fall back.</param>
public sealed record AiModerationResult(
    IReadOnlyList<string> Safe,
    bool Sufficient);

/// <summary>
/// The PASS-THROUGH default moderator (story 01 seam only): returns EVERY item
/// unmoderated, with <see cref="AiModerationResult.Sufficient"/> = (items.Count &gt;
/// 0). It exists ONLY so the gate pipeline compiles before story 05 lands, and is
/// NEVER shipped to a real AI consumer - story 05 (#124) REPLACES it with the real
/// <c>IContentSafetyFilter</c> + family-safe composition BEFORE ai-on-demand-
/// generation/05 goes live. Registered as the default <see cref="IAiOutputModerator"/>
/// in Program.cs until then.
/// </summary>
public sealed class PassthroughAiOutputModerator : IAiOutputModerator
{
    /// <inheritdoc />
    public Task<AiModerationResult> ModerateAsync(IReadOnlyList<string> items, bool familySafe, CancellationToken cancellationToken = default)
    {
        // SEAM ONLY - no moderation. Story 05 replaces this with the real filter.
        return Task.FromResult(new AiModerationResult(items, items.Count > 0));
    }
}
