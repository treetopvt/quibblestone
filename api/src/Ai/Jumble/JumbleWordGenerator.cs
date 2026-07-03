// ----------------------------------------------------------------------------
//  JumbleWordGenerator - the AI word-bank jumble backend (ai-on-demand-generation
//  /05, issue #126; its moderation policy is /02, issue #127). The FIRST real
//  consumer of the AI cost gate: the lightest, cheapest live-generation payload
//  in the product (ADR 0001), and the deliberate proving ground for the gate.
//
//  WHAT IT DOES: when a Word Bank player taps "Fresh runes" and the client wants
//  AI-fresh options, this builds a tiny prompt (brand voice + family-safe rules +
//  the blank's category + an avoid-list), routes it through the shared
//  GatedAiCompletionClient (NEVER a raw provider call), and parses the moderated
//  reply into a small set of short single words for that category. It falls back
//  to game-modes/07's free deterministic reshuffle - by returning FellBack=true -
//  whenever the gate degrades (quota-exhausted, breaker-open, AI unavailable, or
//  too few safe words survive), so the player always gets fresh runes.
//
//  CONSUMES THE GATE, DOES NOT FORK IT (story 05 AC-07, story 02 AC-05): the
//  quota (03), spend breaker (04), attribution telemetry (04), and
//  moderate-before-display (05) all live in the gate; this class calls the gate
//  and does NOT re-implement any of them. In particular it stands up NO second
//  filter: it instructs the model to emit ONE short word per line, so the gate's
//  generic newline split moderates every candidate word INDIVIDUALLY (the same
//  IContentSafetyFilter + family-safe seam), and this class only shapes the
//  already-moderated survivors (dedupe, cap, drop non-single-word noise). No
//  AI-sourced word reaches a player without passing the gate's moderation
//  (README section 6, non-negotiable).
//
//  PARSE DEFENSIVELY (story 05 AC-02): the model may number lines, add stray
//  punctuation, or return multi-word lines. Each moderated line is reduced to a
//  single lowercase alphabetic word or dropped; a malformed / empty / too-thin
//  reply is treated as "unavailable" -> FellBack, never a broken set thrown into
//  gameplay.
//
//  ANONYMOUS + METERED (story 05 AC-04/05): the gate keys quota + the ONE
//  attribution event on the anonymous instanceId (a room's InstanceId or a solo
//  device session id - never PII), tagged feature "jumble". This class passes
//  the id straight through; it holds no identity and logs no words.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Ai.Jumble;

/// <summary>
/// Generates a small set of fresh, moderated, on-theme word-bank words for one
/// blank's category via the AI cost gate (ai-on-demand-generation/05). A
/// stateless singleton: it holds only the injected <see cref="GatedAiCompletionClient"/>
/// and a logger. Every call routes through the gate, so quota / breaker /
/// attribution / moderation all apply; a degraded gate yields
/// <see cref="JumbleWordResult.FellBack"/> so the caller runs the free reshuffle.
/// </summary>
public sealed class JumbleWordGenerator
{
    /// <summary>The attribution feature tag for every jumble call (ADR 0001 / story 05 AC-05).</summary>
    public const string FeatureTag = "jumble";

    /// <summary>The most words a single jumble asks for / returns (ADR 0001 sizes the payload at ~8-10 short words).</summary>
    public const int MaxWords = 10;

    /// <summary>
    /// The fewest usable words that make a jumble worth showing. Below this the
    /// caller degrades to the deterministic reshuffle rather than offer a thin set
    /// (story 05 AC-06 / story 02 AC-03). The gate's own moderator floor is 1
    /// (generic); the jumble raises the bar here where it knows its payload.
    /// </summary>
    public const int MinUsableWords = 3;

    // A short cap on the max output tokens. With the transport's minimal reasoning
    // effort, ~10 short words on their own lines cost ~32 completion tokens (measured
    // live), so this is comfortable headroom, not a limit we brush against; billing is
    // on ACTUAL tokens, so the margin is free. It still bounds worst-case latency/cost
    // (the ~$0.0001/call payload the gate is proved on - ADR 0001). NOTE: this cap is
    // only safe because the transport pins reasoning effort to minimal - a reasoning
    // model at default effort would burn the entire budget on hidden reasoning and
    // return nothing (the bug that once silently fell every jumble back).
    private const int MaxOutputTokens = 128;

    // How many already-shown words to name in the avoid-list. Enough to steer the
    // model toward fresh options without bloating the prompt (and its input cost).
    private const int MaxAvoidWords = 20;

    private readonly GatedAiCompletionClient _gate;
    private readonly ILogger<JumbleWordGenerator> _logger;

    public JumbleWordGenerator(GatedAiCompletionClient gate, ILogger<JumbleWordGenerator> logger)
    {
        _gate = gate;
        _logger = logger;
    }

    /// <summary>
    /// Generates fresh AI word-bank options for <paramref name="category"/>, or a
    /// fell-back result the caller degrades to the free reshuffle for.
    /// </summary>
    /// <param name="category">The blank's category (e.g. "noun") - a controlled label, not player free text. Steers the prompt.</param>
    /// <param name="avoid">Words already shown for this blank (curated or prior AI, already vetted) - the model is asked to skip them.</param>
    /// <param name="familySafe">The round's family-safe toggle - tightens the prompt AND the gate's moderation (story 05 AC-03).</param>
    /// <param name="instanceId">The anonymous session key (room InstanceId or solo device session id) the gate meters + attributes on. NEVER PII.</param>
    /// <param name="cancellationToken">Cancellation threaded to the gate (a dropped client / shed round).</param>
    public async Task<JumbleWordResult> GenerateAsync(
        string category,
        IReadOnlyList<string> avoid,
        bool familySafe,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var request = new AiCompletionRequest(
            SystemInstruction: BuildSystemInstruction(familySafe),
            Prompt: BuildPrompt(category, avoid),
            MaxOutputTokens: MaxOutputTokens);

        // The ONE gated call: quota -> spend-ceiling -> transport -> record +
        // attribution -> moderate. The gate splits the model's one-word-per-line
        // reply and moderates every candidate individually; Output is the safe,
        // already-vetted survivors (story 05 AC-03).
        var gated = await _gate.CompleteGatedAsync(
            request,
            instanceId,
            FeatureTag,
            familySafe,
            cancellationToken).ConfigureAwait(false);

        // The gate degraded (quota / breaker / AI unavailable / too few safe) ->
        // the caller runs the free deterministic reshuffle (story 05 AC-06).
        if (gated.FellBack || !gated.IsAvailable)
        {
            return JumbleWordResult.FellBackWith(gated.RemainingQuota);
        }

        // Shape the moderated survivors into a clean single-word set (story 05
        // AC-02): drop non-single-word noise, lower-case, dedupe against each
        // other AND the avoid-list, cap to MaxWords.
        var words = ShapeWords(gated.Output, avoid);

        // Too few usable words is treated as "unavailable" -> fall back rather
        // than show a thin set (story 05 AC-06 / story 02 AC-03).
        if (words.Count < MinUsableWords)
        {
            _logger.LogDebug(
                "AI jumble: only {Count} usable word(s) after shaping (feature={Feature}); falling back.",
                words.Count,
                FeatureTag);
            return JumbleWordResult.FellBackWith(gated.RemainingQuota);
        }

        return new JumbleWordResult(words, gated.RemainingQuota, FellBack: false);
    }

    /// <summary>Builds the brand-voice + format system instruction (family-safe tightens it - story 05 AC-02/03).</summary>
    private static string BuildSystemInstruction(bool familySafe)
    {
        var instruction = new StringBuilder(
            "You are the Stonecarver, a warm guide for QuibbleStone, a playful family word game. " +
            "Reply with a list of fresh, whimsical, on-theme words for a word-bank round: " +
            "ONE short single lowercase word per line and nothing else - no numbering, no bullets, " +
            "no punctuation, no sentences, no explanations.");
        if (familySafe)
        {
            instruction.Append(
                " Every word must be wholesome and family-safe: nothing about violence, weapons, gore, " +
                "romance, alcohol, or anything unkind.");
        }
        return instruction.ToString();
    }

    /// <summary>Builds the user prompt: the category plus a short avoid-list of already-shown words (story 05 AC-01/02).</summary>
    private static string BuildPrompt(string category, IReadOnlyList<string> avoid)
    {
        var prompt = new StringBuilder(
            $"Give me up to {MaxWords} short, single {category} words for a word-bank round.");

        // Name a bounded sample of already-shown words so the model favors fresh
        // options (story 05 AC-02). Reduce each to a single clean word first: the
        // avoid-list is client-supplied, so this keeps stray text / injection-shaped
        // input out of the prompt (defense in depth - the OUTPUT is moderated by the
        // gate regardless, but the prompt stays clean).
        var avoidSample = avoid
            .Select(ToSingleWord)
            .OfType<string>()
            .Take(MaxAvoidWords)
            .ToArray();
        if (avoidSample.Length > 0)
        {
            prompt.Append(" Do not use any of these already-shown words: ");
            prompt.Append(string.Join(", ", avoidSample));
            prompt.Append('.');
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Reduces the gate's moderated, per-line output to a clean set of unique
    /// single lowercase words (story 05 AC-02), deduped against the avoid-list and
    /// capped at <see cref="MaxWords"/>. Every input line has ALREADY passed the
    /// gate's moderation; this only shapes them (it is not a second filter).
    /// </summary>
    private static IReadOnlyList<string> ShapeWords(IReadOnlyList<string> moderated, IReadOnlyList<string> avoid)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in avoid)
        {
            var normalized = ToSingleWord(word);
            if (normalized is not null)
            {
                seen.Add(normalized);
            }
        }

        var words = new List<string>(moderated.Count);
        foreach (var line in moderated)
        {
            var word = ToSingleWord(line);
            if (word is null || !seen.Add(word))
            {
                continue;
            }
            words.Add(word);
            if (words.Count >= MaxWords)
            {
                break;
            }
        }

        return words;
    }

    /// <summary>
    /// Reduces one line to a single lowercase alphabetic word, or null if it is
    /// not exactly one word of a sensible length. Splitting on non-letters strips
    /// numbering / bullets / punctuation ("1. moss" -> "moss"); a line that yields
    /// zero or more than one letter-token is dropped (defensive: "single common
    /// words" only - story 05 AC-02).
    /// </summary>
    private static string? ToSingleWord(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        string? single = null;
        foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Keep only pure-letter tokens; anything with a digit/symbol is noise.
            var letters = new StringBuilder(token.Length);
            foreach (var ch in token)
            {
                if (char.IsLetter(ch))
                {
                    letters.Append(ch);
                }
                else
                {
                    // A token with a non-letter mixed in (e.g. "moss,") - strip it;
                    // but a token that is ONLY punctuation/digits yields nothing.
                }
            }

            if (letters.Length == 0)
            {
                continue;
            }

            if (single is not null)
            {
                // More than one word on the line - not a single common word, drop it.
                return null;
            }

            single = letters.ToString();
        }

        if (single is null || single.Length < 2 || single.Length > 20)
        {
            return null;
        }

        return single.ToLowerInvariant();
    }
}

/// <summary>
/// The result of one AI jumble generation. Carries the moderated words (empty on
/// fall-back), the per-session remaining quota for the "N Fresh Runes left" meter
/// (ai-cost-gate/03), and whether the caller must degrade to the free
/// deterministic reshuffle (<see cref="FellBack"/>). The web wire DTO mirrors
/// this shape (game-modes/07).
/// </summary>
/// <param name="Words">The fresh, moderated, single-word options. Empty when <see cref="FellBack"/> is true.</param>
/// <param name="RemainingQuota">Per-session AI jumbles left, for the client meter.</param>
/// <param name="FellBack">True when the gate degraded - the caller runs the deterministic reshuffle.</param>
public sealed record JumbleWordResult(
    IReadOnlyList<string> Words,
    int RemainingQuota,
    bool FellBack)
{
    /// <summary>Builds the fell-back result (no words) carrying the remaining quota so the meter stays honest.</summary>
    public static JumbleWordResult FellBackWith(int remainingQuota) =>
        new(Array.Empty<string>(), remainingQuota, FellBack: true);
}
