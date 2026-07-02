// ----------------------------------------------------------------------------
//  PublishedTalesController - the ONE server surface of the Keepsake Gallery
//  feature (keepsake-gallery/04, issue #66) and the HIGHEST child-safety-stakes
//  surface in the app: a PUBLIC, read-only tale page plus its host-initiated
//  publish / revoke endpoints.
//
//  This is the documented exception to the feature's client-only boundary
//  (feature.md Decisions). It is a THIN, DEDICATED controller kept well away from
//  GameHub.cs and the round lifecycle - it never touches the hub, the room
//  registry, or the real-time backbone. It owns three routes:
//
//    POST   /api/tales         (host-initiated publish, opt-in)  - AC-01/AC-03
//    DELETE /api/tales/{slug}  (revoke - stop sharing)           - AC-07
//    GET    /t/{slug}          (the PUBLIC read-only tale page)   - AC-02/AC-03
//
//  CHILD SAFETY, NON-NEGOTIABLE (AC-03) - enforced HERE, server-side, because a
//  public page must NEVER show unfiltered content even if a client lies:
//    - SERVER-SIDE RE-VET: EVERY non-empty part (coral player-words AND the
//      "literal" template runs) plus the byline names is run through the
//      authoritative IContentSafetyFilter on publish; if ANY fails, the whole
//      publish is REJECTED (400). We do NOT trust the client's word-vs-literal
//      classification: the server has no template to verify a "literal" claim
//      against, and the endpoint is public + anonymous, so a lying client could
//      otherwise smuggle unfiltered text onto the child-visible page as a
//      "literal" part. Genuine author template text is false-positive-resistant
//      and passes harmlessly.
//    - UNGUESSABLE SLUG: SlugGenerator mints a 12-char cryptographically-random,
//      non-sequential slug (resists enumeration).
//    - noindex: the public page sends an X-Robots-Tag: noindex, nofollow HEADER
//      and carries a <meta name="robots" content="noindex"> so it is never
//      search-indexed.
//    - HOST-INITIATED / OPT-IN: publishing only ever happens on this explicit
//      POST; nothing publishes automatically.
//    - TTL EXPIRY: every tale is stamped ExpiresUtc = now + TaleTtl and reads as
//      GONE past it (lazy expiry-on-read in the store).
//    - NO PII: only the already-vetted in-session nicknames (the byline) and the
//      already-filtered story are ever stored / served - never an IP, session id,
//      or real name.
//
//  FREE (AC-04): there is NO entitlement / capability check anywhere on this
//  path - the share link is a growth loop, not a gated capability. This
//  controller consumes no billing key.
//
//  ANTI-ABUSE (open write endpoint): sane length caps (title, per-part, part
//  count, byline) reject oversized input. A per-IP rate limit / quota is a noted
//  follow-up (no rate-limit infra exists yet) - see the story's open questions.
//
//  DISABLED FALLBACK: with NO storage connection string (local dev, CI, a fresh
//  clone) the injected store is DisabledPublishedTaleStore - publish returns a
//  clear 503 "not available" and every read 404s, so the app runs with the
//  feature simply OFF and ZERO Azure setup (mirrors the NoOp-telemetry posture).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.PublishedTales;

/// <summary>One part of a publish request body: literal template text or a coral player-word.</summary>
/// <param name="IsWord">True for a player-supplied coral word (re-vetted), false for literal template text.</param>
/// <param name="Text">The part's text. May be null - treated as empty.</param>
public sealed record PublishTalePartRequest(bool IsWord, string? Text);

/// <summary>
/// Request body for POST /api/tales (host-initiated publish). The client sends
/// the ALREADY-ASSEMBLED story as ordered parts plus the byline of in-session
/// nicknames; the server re-vets the coral words + byline, mints a slug, and
/// stores it. No PII is accepted or stored (AC-03).
/// </summary>
/// <param name="Title">The tale title (length-capped).</param>
/// <param name="Parts">The ordered body parts (literal text + coral player-words).</param>
/// <param name="BylineNames">The joined in-session nicknames (e.g. "Sam, Mia &amp; Bo"); may be null/empty.</param>
public sealed record PublishTaleRequest(
    string? Title,
    IReadOnlyList<PublishTalePartRequest>? Parts,
    string? BylineNames);

[ApiController]
[Route("api/tales")]
public sealed class PublishedTalesController : ControllerBase
{
    // TTL (AC-05): a published tale is an ephemeral keepsake, not a system of
    // record (README section 4). 30 days is generous for a "look what we made"
    // share while guaranteeing tales do not accumulate unbounded.
    public static readonly TimeSpan TaleTtl = TimeSpan.FromDays(30);

    // Anti-abuse caps for an open, anonymous write endpoint. Generous for a real
    // family tale, tight enough to reject a payload built to bloat storage.
    private const int MaxTitleLength = 200;
    private const int MaxPartTextLength = 500;
    private const int MaxPartsCount = 400;
    private const int MaxBylineLength = 300;

    // Local-dev fallback for the web app base (the "Play QuibbleStone" CTA target)
    // when no PublishedTales:WebAppBaseUrl is configured - the Vite dev origin.
    private const string DefaultWebAppBaseUrl = "http://localhost:5173";

    private readonly IPublishedTaleStore _store;
    private readonly IContentSafetyFilter _safety;
    private readonly string _webAppBaseUrl;

    public PublishedTalesController(IPublishedTaleStore store, IContentSafetyFilter safety, IConfiguration configuration)
    {
        _store = store;
        _safety = safety;
        // The web app base for the public page's CTAs. Configured per-environment
        // (composed in Bicep from the Static Web App host name, NEVER hardcoded);
        // falls back to the Vite dev origin for local runs.
        var configured = configuration["PublishedTales:WebAppBaseUrl"];
        _webAppBaseUrl = string.IsNullOrWhiteSpace(configured)
            ? DefaultWebAppBaseUrl
            : configured.TrimEnd('/');
    }

    /// <summary>
    /// POST /api/tales -> { slug, url }. Host-initiated, opt-in publish of an
    /// already-assembled, already-filtered tale (AC-01/AC-03). Re-vets every coral
    /// word + the byline server-side, enforces length caps, mints an unguessable
    /// slug, stamps a TTL, and stores it. FREE - no entitlement check (AC-04).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Publish([FromBody] PublishTaleRequest? request, CancellationToken cancellationToken)
    {
        // Disabled fallback (no storage configured): a clear "not available", never a 500.
        if (!_store.IsEnabled)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Publishing a public tale link is not available right now." });
        }

        if (request is null)
        {
            return BadRequest(new { message = "A tale to publish is required." });
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return BadRequest(new { message = "A tale needs a title to publish." });
        }
        if (title.Length > MaxTitleLength)
        {
            return BadRequest(new { message = "That title is too long to publish." });
        }

        var requestParts = request.Parts ?? [];
        if (requestParts.Count == 0)
        {
            return BadRequest(new { message = "A tale needs some story to publish." });
        }
        if (requestParts.Count > MaxPartsCount)
        {
            return BadRequest(new { message = "That tale is too long to publish." });
        }

        var byline = (request.BylineNames ?? string.Empty).Trim();
        if (byline.Length > MaxBylineLength)
        {
            return BadRequest(new { message = "That byline is too long to publish." });
        }

        // SERVER-SIDE RE-VET (AC-03, critical): a public page must never show
        // anything a family-safe session would not have. Re-run EVERY non-empty
        // part - coral player-words AND "literal" template runs - plus the byline
        // through the authoritative filter; reject the WHOLE publish if any fails.
        // We deliberately do NOT trust the client's IsWord classification here: the
        // server has no template to verify a "literal" claim against, and this
        // endpoint is public + anonymous, so trusting the flag would let a crafted
        // request smuggle unfiltered text onto the child-visible page as a
        // "literal" part (security review CR-001). Genuine author template text is
        // false-positive-resistant and passes harmlessly. The rejected text is
        // never echoed back.
        var parts = new List<TalePart>(requestParts.Count);
        foreach (var part in requestParts)
        {
            var text = part.Text ?? string.Empty;
            if (text.Length > MaxPartTextLength)
            {
                return BadRequest(new { message = "Part of that tale is too long to publish." });
            }

            // Skip empty coral word slots (an unfilled blank renders as a gap,
            // exactly like the reveal) so an empty coral part never reaches the page.
            if (part.IsWord && text.Trim().Length == 0)
            {
                continue;
            }

            // Re-vet any non-empty text regardless of word/literal (see above).
            // Empty literal text (inter-word spacing/punctuation) has nothing to
            // vet and is stored as-is so the story reads correctly.
            if (text.Trim().Length > 0)
            {
                var verdict = await _safety.CheckAsync(text, cancellationToken);
                if (!verdict.IsAllowed)
                {
                    return BadRequest(new { message = "That tale cannot be shared publicly - some content did not pass the family-safe check." });
                }
            }

            parts.Add(new TalePart(IsWord: part.IsWord, Text: text));
        }

        if (parts.Count == 0)
        {
            return BadRequest(new { message = "A tale needs some story to publish." });
        }

        if (byline.Length > 0)
        {
            var bylineVerdict = await _safety.CheckAsync(byline, cancellationToken);
            if (!bylineVerdict.IsAllowed)
            {
                return BadRequest(new { message = "That tale cannot be shared publicly - some content did not pass the family-safe check." });
            }
        }

        var now = DateTimeOffset.UtcNow;
        var tale = new PublishedTale(
            Slug: SlugGenerator.Generate(),
            Title: title,
            Parts: parts,
            BylineNames: byline,
            CreatedUtc: now,
            ExpiresUtc: now + TaleTtl);

        try
        {
            await _store.PublishAsync(tale, cancellationToken);
        }
        catch (Exception)
        {
            // The write failed - never hand back a link to a tale that was not
            // stored. A clear "not available" (the detail is logged by the store).
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Publishing a public tale link is not available right now." });
        }

        // The public link lives on THIS app's own public base (the API serves
        // /t/{slug}), derived from the request - never a hardcoded literal.
        var url = $"{Request.Scheme}://{Request.Host}/t/{tale.Slug}";
        return Ok(new { slug = tale.Slug, url });
    }

    /// <summary>
    /// DELETE /api/tales/{slug} -> 204. Revoke a published tale so its link stops
    /// resolving (AC-07). Low-ceremony and idempotent: revoking an unknown / already
    /// gone slug still succeeds.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Revoke(string slug, CancellationToken cancellationToken)
    {
        await _store.RevokeAsync(slug, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// GET /t/{slug} -> server-rendered HTML tale page (AC-02/AC-03). The PUBLIC,
    /// read-only page: no app install, no account, no login. Renders the carved
    /// stone-tablet from the stored parts (coral player-words), the "carved by
    /// [names]" byline, and the gold "Play QuibbleStone" CTA. Missing / expired ->
    /// a friendly 404 page. Always sent noindex, nofollow (header + meta).
    /// </summary>
    [HttpGet("/t/{slug}")]
    public async Task<IActionResult> Page(string slug, CancellationToken cancellationToken)
    {
        // noindex HEADER on EVERY response from this route (found or not) - a public
        // tale must never be search-indexed (AC-03).
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        var tale = await _store.GetAsync(slug, cancellationToken);
        if (tale is null)
        {
            return new ContentResult
            {
                Content = RenderNotFoundPage(_webAppBaseUrl),
                ContentType = "text/html; charset=utf-8",
                StatusCode = StatusCodes.Status404NotFound,
            };
        }

        return new ContentResult
        {
            Content = RenderTalePage(tale, _webAppBaseUrl),
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
    }

    // ---- Server-rendered HTML (standalone page - inline CSS, no SPA import) -----
    //
    // This page does NOT load the React SPA, so it cannot pull from the MUI theme;
    // instead it mirrors the app's stone-tablet look with the SAME theme color
    // values as INLINE styles (purple / coral / gold / parchment - web/src/theme.ts).
    // Every piece of stored content is HTML-encoded before it lands in the markup
    // (WebUtility.HtmlEncode) - defense in depth on a public surface even though the
    // content is already family-safe filtered. No JS beyond the plain CTA links.

    private static string RenderTalePage(PublishedTale tale, string webAppBaseUrl)
    {
        var body = new StringBuilder();
        foreach (var part in tale.Parts)
        {
            var text = WebUtility.HtmlEncode(part.Text);
            body.Append(part.IsWord
                ? $"<span class=\"coral\">{text}</span>"
                : text);
        }

        var bylineHtml = string.IsNullOrWhiteSpace(tale.BylineNames)
            ? string.Empty
            : $"<p class=\"byline\">carved by {WebUtility.HtmlEncode(tale.BylineNames)}</p>";

        var home = WebUtility.HtmlEncode(webAppBaseUrl);
        var host = WebUtility.HtmlEncode($"{webAppBaseUrl}/host");

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="robots" content="noindex, nofollow" />
              <title>{{WebUtility.HtmlEncode(tale.Title)}} - a QuibbleStone tale</title>
              <style>
                :root {
                  --purple: #6C4BD8; --coral: #FF6B57; --gold: #FFB22E; --gold-deep: #E89A12;
                  --tablet-top: #EFE3C7; --tablet-mid: #E3D2AC; --tablet-bottom: #D6C194;
                  --parchment-top: #F8F1E2; --parchment-bottom: #F0E6D0;
                  --stone-edge: #B49B6E; --ink: #2B2622;
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0; min-height: 100vh; padding: 24px 16px 48px;
                  font-family: "Nunito", system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
                  color: var(--ink);
                  background: linear-gradient(180deg, var(--parchment-top) 0%, var(--parchment-bottom) 100%);
                  display: flex; flex-direction: column; align-items: center;
                }
                .wrap { width: 100%; max-width: 480px; }
                .brand {
                  text-align: center; font-family: "Fredoka", system-ui, sans-serif;
                  font-weight: 700; font-size: 20px; color: var(--purple); margin: 8px 0 20px;
                }
                .tablet {
                  background: linear-gradient(168deg, var(--tablet-top) 0%, var(--tablet-mid) 52%, var(--tablet-bottom) 100%);
                  border-radius: 40px 40px 28px 28px;
                  box-shadow: 0 26px 55px -22px rgba(108,75,216,.5), 0 0 0 6px rgba(255,255,255,.3);
                  padding: 32px 28px; margin-bottom: 28px;
                }
                .title {
                  font-family: "Fredoka", system-ui, sans-serif; font-weight: 700;
                  font-size: 23px; line-height: 1.18; color: var(--purple); margin: 0 0 20px;
                }
                .story {
                  font-family: "Nunito", system-ui, sans-serif; font-weight: 600;
                  font-size: 17.5px; line-height: 1.72; color: var(--ink); white-space: pre-wrap;
                }
                .coral { color: var(--coral); font-weight: 800; border-bottom: 2px solid rgba(255,107,87,.4); }
                .byline {
                  margin: 20px 0 0; text-align: center; font-weight: 700; font-size: 14px;
                  color: rgba(43,38,34,.66);
                }
                .cta {
                  display: block; width: 100%; text-align: center; text-decoration: none;
                  padding: 18px 20px; border-radius: 18px; font-family: "Fredoka", system-ui, sans-serif;
                  font-weight: 700; font-size: 18px; margin-bottom: 14px;
                }
                .cta-play {
                  color: var(--ink);
                  background: linear-gradient(180deg, var(--gold) 0%, var(--gold-deep) 100%);
                  box-shadow: 0 10px 22px -10px rgba(232,154,18,.8);
                }
                .cta-start {
                  color: var(--purple); background: transparent; border: 2px solid var(--purple);
                }
                .foot { text-align: center; font-size: 12.5px; color: rgba(43,38,34,.5); margin-top: 8px; }
              </style>
            </head>
            <body>
              <div class="wrap">
                <div class="brand">QuibbleStone</div>
                <div class="tablet">
                  <h1 class="title">{{WebUtility.HtmlEncode(tale.Title)}}</h1>
                  <p class="story">{{body}}</p>
                  {{bylineHtml}}
                </div>
                <a class="cta cta-play" href="{{home}}">Play QuibbleStone</a>
                <a class="cta cta-start" href="{{host}}">Start your own tale</a>
                <p class="foot">A fill-in-the-blank word game for hilarity and easy fun.</p>
              </div>
            </body>
            </html>
            """;
    }

    private static string RenderNotFoundPage(string webAppBaseUrl)
    {
        var home = WebUtility.HtmlEncode(webAppBaseUrl);
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="robots" content="noindex, nofollow" />
              <title>Tale not found - QuibbleStone</title>
              <style>
                * { box-sizing: border-box; }
                body {
                  margin: 0; min-height: 100vh; padding: 40px 20px;
                  font-family: "Nunito", system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
                  color: #2B2622;
                  background: linear-gradient(180deg, #F8F1E2 0%, #F0E6D0 100%);
                  display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center;
                }
                h1 { font-family: "Fredoka", system-ui, sans-serif; color: #6C4BD8; font-size: 26px; margin: 0 0 12px; }
                p { font-size: 16px; color: rgba(43,38,34,.7); max-width: 360px; margin: 0 0 24px; }
                a {
                  text-decoration: none; padding: 18px 28px; border-radius: 18px;
                  font-family: "Fredoka", system-ui, sans-serif; font-weight: 700; font-size: 18px; color: #2B2622;
                  background: linear-gradient(180deg, #FFB22E 0%, #E89A12 100%);
                  box-shadow: 0 10px 22px -10px rgba(232,154,18,.8);
                }
              </style>
            </head>
            <body>
              <h1>This tale has drifted away</h1>
              <p>The link may have expired or been unshared. Every QuibbleStone tale is a fleeting keepsake - but you can always carve a new one.</p>
              <a href="{{home}}">Play QuibbleStone</a>
            </body>
            </html>
            """;
    }
}
