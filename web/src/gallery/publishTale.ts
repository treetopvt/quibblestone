// ----------------------------------------------------------------------------
//  publishTale.ts - the web client for the shareable public tale link
//  (keepsake-gallery/04, AC-01/AC-07). A thin REST client, NOT the feature: the
//  real publish (re-vet, slug minting, storage, the public page) lives server-
//  side in api/src/PublishedTales/PublishedTalesController.cs. This module only
//  POSTs the already-assembled tale and returns the public `/t/<slug>` link.
//
//  Mirrors web/src/safety/checkWord.ts: the API base URL comes from
//  `import.meta.env.VITE_API_BASE_URL` (never hardcoded, CLAUDE.md section 4),
//  and it FAILS GRACEFULLY - a network error, non-OK status, or unparseable body
//  resolves `null` rather than throwing, so the share hand-off can fall back to
//  the watermarked image / text share exactly as before this story (AC-01). It
//  never blocks or breaks the existing share flow.
//
//  Child safety (AC-03): this client sends only the already-assembled, already-
//  filtered story parts (literal template text + already-vetted coral words) and
//  the in-session nicknames - never raw submissions, never PII. The SERVER re-vets
//  the coral words + byline before it ever stores or serves them; a lying client
//  cannot get unfiltered content onto the public page.
//
//  Publishing is HOST-INITIATED and OPT-IN (AC-03): nothing here fires
//  automatically - a caller invokes `publishTale` only in response to an explicit
//  host tap.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** One ordered part of the tale body sent to the server: literal text or a coral word. */
export interface PublishTalePart {
  /** True for a player-supplied coral word (the server re-vets these), false for literal template text. */
  isWord: boolean;
  /** The part's text (a template run, or one already-vetted player word). */
  text: string;
}

/** Input to {@link publishTale}: the already-assembled tale plus its byline. */
export interface PublishTaleInput {
  /** The tale title (already shown on the reveal). */
  title: string;
  /** The ordered body parts (from buildRevealParts - literal text interleaved with coral words). */
  parts: PublishTalePart[];
  /** The joined in-session nicknames for the byline (e.g. "Sam, Mia & Bo"), or undefined for no crew. */
  bylineNames?: string;
}

/** The public link returned by a successful publish. */
export interface PublishedTaleLink {
  /** The unguessable public URL: `https://<app>/t/<slug>`. */
  url: string;
}

/** Narrows an unknown parsed JSON body into a PublishedTaleLink, or null if it does not match. */
function asPublishedTaleLink(value: unknown): PublishedTaleLink | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.url !== 'string' || record.url.length === 0) return null;
  return { url: record.url };
}

/**
 * Publishes an already-assembled, already-filtered tale to a public link
 * (POST /api/tales) and returns its `/t/<slug>` URL. Resolves `null` on any
 * failure (network error, non-OK status such as the disabled-feature 503, a
 * rejected re-vet, or an unparseable body) so the caller falls back to the
 * existing image / text share rather than surfacing an error (AC-01).
 */
export async function publishTale(input: PublishTaleInput): Promise<PublishedTaleLink | null> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/tales`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: input.title,
        parts: input.parts,
        bylineNames: input.bylineNames ?? '',
      }),
    });

    if (!response.ok) return null;

    const body: unknown = await response.json();
    return asPublishedTaleLink(body);
  } catch {
    // Network failure, JSON parse failure, or any other unexpected rejection:
    // the share simply falls back to the image/text payload (AC-01).
    return null;
  }
}

/**
 * Revokes a published tale so its public link stops resolving (AC-07). Takes the
 * slug (the last path segment of a `/t/<slug>` URL). Resolves `true` when the
 * revoke succeeded, `false` on any failure - never throws.
 */
export async function revokeTale(slug: string): Promise<boolean> {
  if (slug.length === 0) return false;
  try {
    const response = await fetch(
      `${import.meta.env.VITE_API_BASE_URL}/api/tales/${encodeURIComponent(slug)}`,
      { method: 'DELETE' },
    );
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Extracts the slug from a `/t/<slug>` tale URL so {@link revokeTale} can target
 * it. Matches ONLY the `/t/<slug>` shape (optionally with a trailing slash /
 * query / hash) - anything else (a bare origin, some other path) returns an empty
 * string, so a malformed/non-tale URL never makes revoke delete the wrong slug.
 */
export function slugFromTaleUrl(url: string): string {
  const path = url.split(/[?#]/, 1)[0];
  const match = path.match(/\/t\/([^/]+)\/?$/);
  return match ? match[1] : '';
}
