// ----------------------------------------------------------------------------
//  vaultGallery.ts - merges the device-local "Tales we've carved" gallery with
//  the anonymous server-side keepsake vault into ONE deduplicated, recency-
//  ordered list (keepsake-vault/02, ADR 0003 Layer 2, issue #212).
//
//  Why this exists: keepsake-gallery/03's gallery read ONLY the device's local
//  IndexedDB (web/src/gallery/localGallery.ts) - a 30-tale cap with silent
//  oldest-first eviction and a saveTale() that swallows its own storage
//  failures. Now that keepsake-vault/01 auto-saves every completed reveal
//  server-side (independent of the local write), a locally-evicted or
//  never-written tale still exists in the vault. This module makes the gallery
//  read BOTH sources so the vault is the source of truth and the local
//  IndexedDB becomes a cache / fast offline copy (AC-03, AC-04).
//
//  THE MERGE IS A PURE FUNCTION over two already-fetched arrays (AC-01, AC-02):
//  `mergeGalleryTales(local, vault)` takes a local `TaleMeta[]` and a vault
//  `VaultTaleView[] | null` and returns one merged, recency-sorted list - no
//  IndexedDB, no fetch, no async - mirroring `talesToEvict`'s directly-
//  unit-testable precedent in localGallery.ts. The offline-degrade path
//  (AC-02) is `vault === null`: the merge returns the local list unchanged, so
//  a vault fetch failure is trivially testable without mocking IndexedDB and
//  fetch together.
//
//  DEDUPE (AC-01): a tale saved on this device lands in BOTH stores (story 01's
//  auto-save posts the SAME title/parts/byline the local saveTale persists), so
//  the merge must never double-list it. Two dedupe signals, in order:
//    1. The explicit `vaultTaleId` stamp on a local `TaleMeta` (mirrors
//       `cloudTaleId`) - a local record whose `vaultTaleId` equals a vault
//       tale's `taleId` IS that vault tale; the vault copy is dropped.
//    2. A content signature (title + byline + ordered parts) - the fallback
//       when no stamp is present yet, which is the common case today because
//       story 01's fire-and-forget auto-save does not round-trip the minted id
//       back onto the local record. Reveal.tsx hands the SAME title/byline/parts
//       to both calls, so the two copies start identical; the signature then
//       NORMALIZES each side the way the vault server does before it stores a
//       tale (trims the title and byline, drops empty coral-word slots - see
//       api/src/Vault/VaultController.cs Save), so a stored vault copy and its
//       local original still produce the same signature. The signature is a
//       structured JSON encoding of the normalized fields (title, byline, and
//       the ordered [isWord, text] parts), so field boundaries are unambiguous
//       by construction - correctness never depends on a delimiter being absent
//       from the content.
//
//    Residual caveat (inherent to signature-based dedup, not a regression):
//    two DISTINCT plays that produce byte-identical normalized content (same
//    title, byline, and filled words) share one signature, so if one such copy
//    was locally evicted it will not separately re-populate from the vault (the
//    surviving local copy already covers the signature). The two are visually
//    identical, so the effect is invisible; the durable fix is story 01
//    round-tripping vaultTaleId onto the local record, after which signal 1
//    (the exact id stamp) dedupes instead.
//  Local wins on a match: it holds the rendered image blob the gallery shows;
//  the vault copy (image-less within this story's scope) would only degrade the
//  card to a placeholder. A vault tale with no local match is added as a
//  merged, image-less entry (the existing GalleryCard already renders a
//  placeholder when it has no image URL - no visual redesign, AC-06).
//
//  RECENCY ORDER (AC-01): newest-first by `savedAt`, exactly as listTales
//  already sorts, so the merged list keeps the gallery's existing order. A
//  vault-only tale's `savedAt` is derived from its server-stamped `createdUtc`.
//
//  Child safety (AC-05): a vault-sourced entry carries ONLY the already-
//  filtered shape story 01 stored (title, parts, byline nicknames, createdUtc).
//  This module introduces no new free-text entry point and no new PII surface -
//  it is read/merge logic only. The vault id stays a bearer secret in the
//  X-Vault-Id header (vaultClient.ts), never in a URL and never surfaced here.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import type { TaleMeta, TalePart } from '../gallery/localGallery';
import { listVaultTales, type VaultTaleView } from './vaultClient';
import { readStoredVaultId } from './vaultId';

/**
 * One tale in the merged gallery view. It is a {@link TaleMeta} (so the
 * existing Gallery render tree consumes it unchanged - AC-06) plus a `source`
 * discriminator recording where the entry came from:
 *   - 'local': backed by a local IndexedDB record (has a rendered image blob
 *     the gallery can show and re-share).
 *   - 'vault': present only in the vault (locally evicted or a local write that
 *     failed) - image-less within this story's scope, so its card renders the
 *     existing placeholder (no server-side image render, out of scope).
 * The `source` lets the caller decide which entries can load / re-share a local
 * image without re-deriving it; the render tree itself is untouched.
 */
export interface MergedTale extends TaleMeta {
  source: 'local' | 'vault';
}

/**
 * A stable content signature (title + byline + ordered parts) used to dedupe a
 * local record against its vault counterpart when no `vaultTaleId` stamp links
 * them yet. Reveal.tsx hands the SAME title/byline/parts to both the local
 * saveTale and the vault auto-save, so the two copies start identical; this
 * function then NORMALIZES each side the way the vault server normalizes a tale
 * before storing it (see api/src/Vault/VaultController.cs Save), so a STORED
 * vault copy still matches its untouched local original:
 *   - the title and byline are trimmed (the server `.Trim()`s both);
 *   - an empty coral-WORD slot is dropped (the server skips a word part whose
 *     text is blank/whitespace - an unfilled blank renders as a gap).
 * Literal (non-word) part text is left as-is on both sides (the server stores it
 * untouched). Applying this to the already-normalized vault side is idempotent,
 * so both sides compute the same value - no residual double-list from a trimmed
 * title or an unfilled blank.
 *
 * A tale with no parts (an old local record saved before keepsake-gallery/05)
 * can have no vault counterpart (the vault post-dates parts), so its signature
 * never needs to match a vault tale.
 */
function contentSignature(title: string, bylineNames: string, parts: readonly TalePart[]): string {
  const normalizedParts = parts
    .filter((part) => !(part.isWord && part.text.trim().length === 0))
    .map((part) => [part.isWord, part.text]);
  // A structured JSON encoding of the normalized fields - field boundaries are
  // unambiguous by construction, so dedupe correctness never rests on any
  // delimiter being absent from a title, byline, or (literal or player) part.
  return JSON.stringify([title.trim(), bylineNames.trim(), normalizedParts]);
}

/** The content signature of a local record (its `parts`/`bylineNames` may be absent - treated as empty). */
function localSignature(tale: TaleMeta): string {
  return contentSignature(tale.title, tale.bylineNames ?? '', tale.parts ?? []);
}

/** The content signature of a vault tale (parts + byline are always present in the vault view). */
function vaultSignature(tale: VaultTaleView): string {
  return contentSignature(tale.title, tale.bylineNames, tale.parts);
}

/**
 * Parses a vault tale's server-stamped `createdUtc` (an ISO instant) into epoch
 * ms for recency ordering. Falls back to 0 (oldest) rather than NaN on an
 * unparseable value, so a malformed timestamp sinks to the end of the list
 * instead of scrambling the sort.
 */
function vaultSavedAt(createdUtc: string): number {
  const parsed = Date.parse(createdUtc);
  return Number.isNaN(parsed) ? 0 : parsed;
}

/** Maps a vault tale into a merged, image-less entry keyed by its server-minted tale id. */
function vaultToMerged(tale: VaultTaleView): MergedTale {
  return {
    // The vault tale id is a stable, server-minted key (not a bearer secret -
    // that is the vault id, which never appears here). Using it as the merged
    // entry's `id` keeps the card key stable across reloads.
    id: tale.taleId,
    title: tale.title,
    savedAt: vaultSavedAt(tale.createdUtc),
    bylineNames: tale.bylineNames.length > 0 ? tale.bylineNames : undefined,
    parts: tale.parts,
    vaultTaleId: tale.taleId,
    source: 'vault',
  };
}

/**
 * Merges the device-local gallery list with the vault's tale list into one
 * deduplicated, newest-first list (AC-01). A PURE function over two already-
 * fetched arrays - no IndexedDB, no fetch, no async.
 *
 * - `vault === null` (offline / a network failure / an unparseable body, AC-02):
 *   returns the local list alone (recency-sorted), so a vault fetch failure
 *   degrades gracefully to the local cache and never blocks or errors the
 *   gallery.
 * - Dedupe (AC-01): a vault tale already represented locally - by a matching
 *   `vaultTaleId` stamp OR a matching content signature - is dropped; the local
 *   copy (which holds the rendered image) wins.
 * - Re-population (AC-03/AC-04): a vault tale with no local match (locally
 *   evicted past GALLERY_CAP, or a local write that failed) is added as an
 *   image-less merged entry, so the tale reappears in the view from the vault,
 *   the source of truth.
 */
export function mergeGalleryTales(
  local: readonly TaleMeta[],
  vault: readonly VaultTaleView[] | null,
): MergedTale[] {
  const localMerged: MergedTale[] = local.map((tale) => ({ ...tale, source: 'local' }));

  // Offline / fetch failure: the local cache is the whole answer (AC-02).
  if (vault === null) {
    return sortByRecencyDesc(localMerged);
  }

  // Build the two dedupe indexes over the local list once (O(n)), so the vault
  // scan is O(m) rather than O(n*m).
  const localVaultIds = new Set<string>();
  const localSignatures = new Set<string>();
  for (const tale of local) {
    if (tale.vaultTaleId !== undefined) localVaultIds.add(tale.vaultTaleId);
    localSignatures.add(localSignature(tale));
  }

  const merged = localMerged;
  const seenVaultIds = new Set<string>();
  for (const tale of vault) {
    // Guard against a vault list that repeats a tale id (defensive - the server
    // should not, but a duplicate would otherwise double-list).
    if (seenVaultIds.has(tale.taleId)) continue;
    seenVaultIds.add(tale.taleId);

    // Already represented locally (explicit stamp or identical content): the
    // local copy wins (it has the image), so drop the vault copy (AC-01).
    if (localVaultIds.has(tale.taleId)) continue;
    if (localSignatures.has(vaultSignature(tale))) continue;

    // Vault-only (locally evicted or a failed local write): re-populate it into
    // the view from the vault (AC-03/AC-04).
    merged.push(vaultToMerged(tale));
  }

  return sortByRecencyDesc(merged);
}

/** Newest-saved first, matching listTales' existing gallery order (AC-01). */
function sortByRecencyDesc(tales: MergedTale[]): MergedTale[] {
  return [...tales].sort((a, b) => b.savedAt - a.savedAt);
}

/**
 * Fetches this device's vault tale list for the gallery merge, resolving `null`
 * on ANY reason the vault should not (or cannot) contribute this load (AC-02):
 *   - the device holds no vault id yet (nothing was ever saved server-side, or
 *     storage is cleared) - read-only, so opening the gallery never mints one;
 *   - the vault fetch failed (offline, a network error, a non-OK status, an
 *     unparseable body) - listVaultTales already swallows these to null.
 * A `null` result feeds mergeGalleryTales' offline-degrade path (the gallery
 * shows the local cache alone). Never throws - a vault problem can never block
 * or error the gallery screen.
 */
export async function fetchVaultTales(): Promise<VaultTaleView[] | null> {
  const vaultId = readStoredVaultId();
  if (vaultId === null) return null;
  return listVaultTales(vaultId);
}
