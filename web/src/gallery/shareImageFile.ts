// ----------------------------------------------------------------------------
//  shareImageFile.ts - the ONE shared "share an image File, gracefully" helper
//  (keepsake-gallery/03, extracted from Reveal.tsx's `shareImage` so the
//  gallery re-share action (AC-02) does not duplicate the same feature-detect
//  / cancel-swallow / fallback logic keepsake-gallery/02 already built).
//
//  Contract: feature-detects `navigator.canShare({ files: [file] })` (the ONE
//  correct place to gate on that predicate - see Reveal.tsx's header comment
//  for why a plain text/URL share must NOT gate on it, but a FILE payload
//  specifically should), shares `{ files: [file], title, text }`, swallows a
//  user-cancelled AbortError (treated as "handled", not a failure), and NEVER
//  throws - any other unsupported/rejected case resolves `false` so the caller
//  can fall back to whatever makes sense for it (Reveal.tsx falls back to its
//  existing text-only share; the gallery has no engine text to fall back to,
//  so it simply leaves the tap a graceful no-op).
//
//  Both Reveal.tsx's `handleShare` (re-sharing a freshly-rendered tablet
//  image) and Gallery.tsx's re-share action (re-sharing an already-stored
//  tablet image) call this - one rendering-agnostic sharing code path, never
//  a second copy of the feature-detect/AbortError dance.
// ----------------------------------------------------------------------------

/** Input to {@link shareImageFile}. */
export interface ShareImageFileInput {
  /** The image file to share (already built by the caller - this module never renders one). */
  file: File;
  title: string;
  text: string;
}

/**
 * Shares `input.file` via the Web Share API's file-share support. Resolves
 * `true` when the share succeeded OR the player cancelled the share sheet
 * (AbortError - not a failure), `false` when file sharing is unsupported or
 * the share failed for any other reason - never throws.
 */
export async function shareImageFile(input: ShareImageFileInput): Promise<boolean> {
  if (typeof navigator === 'undefined' || typeof navigator.canShare !== 'function' || typeof navigator.share !== 'function') {
    return false;
  }
  try {
    if (!navigator.canShare({ files: [input.file] })) return false;
    await navigator.share({ files: [input.file], title: input.title, text: input.text });
    return true;
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') return true;
    return false;
  }
}
