// ----------------------------------------------------------------------------
//  useRoomInvite.ts - the ONE shared invite action (session-engine/11, see
//  docs/features/session-engine/11-invite-slot-action.md).
//
//  Before this story, the Lobby's `ShareWidget` (session-engine/04, upgraded by
//  session-engine/06) had two PRIVATE closures - `handleCopy` and `handleShare`
//  - plus the `canShare` feature-detect and the `copied` confirmation timer, all
//  scoped to that one component. The "+ invite" roster slot wanted to trigger
//  the exact same action, which would have meant a second, drifting copy of
//  `navigator.clipboard.writeText` / `navigator.share` call sites (AC-03 is
//  explicit: exactly ONE invite code path). This hook is that lift: both
//  `ShareWidget` and `InviteSlot` in `./Lobby.tsx` call `useRoomInvite(code)`
//  and share one implementation.
//
//  What it returns:
//    - `joinLink`   - the tappable `/join/:code` deep link (session-engine/06's
//                     `buildJoinLink`, resolved against `VITE_PUBLIC_BASE_URL`
//                     or the running app's own origin - never a hardcoded host,
//                     CLAUDE.md section 4).
//    - `canShare`   - whether the Web Share API is available on this browser,
//                     feature-detected ONCE via `typeof navigator.share ===
//                     'function'` (session-engine/04's posture - deliberately
//                     NOT gated on `navigator.canShare()`, which is unnecessary
//                     for a plain text/URL payload and unsupported on some
//                     browsers that otherwise have `share()`).
//    - `copy()`     - copies the deep link to the clipboard and flips `copied`
//                     true for COPIED_CONFIRMATION_MS (~1.8s) before reverting -
//                     byte-for-byte the timing `ShareWidget`'s Copy button
//                     already had.
//    - `share()`    - invokes `navigator.share` with the SAME title/text/url
//                     payload `ShareWidget`'s Share button already sent; a
//                     user cancel (AbortError) or any other rejection is
//                     swallowed, never surfaced as an error.
//    - `copied`     - true while the post-copy confirmation should show.
//
//  Child safety / privacy (AC-06): the payload carries only the room code (via
//  the deep link) and the human-readable "Room code: XXXX" text - no nickname,
//  no free text, no PII. This is identical to story 06 AC-07's link contents,
//  so there is nothing new here for the safety filter to check.
//
//  Not host-gated (AC-04): this hook has no notion of `isHost` at all - the
//  room code is already visible to every player on the Lobby screen, so any
//  caller (the widget's own buttons, or the roster's "+ invite" slot) may
//  invoke it for any player.
// ----------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import { buildJoinLink } from './joinLink';
import { trackEvent } from '../telemetry/analytics';
import { ANALYTICS_EVENTS } from '../telemetry/analyticsEvents';

/**
 * How long the post-copy confirmation (e.g. a "Copied!" label) stays up before
 * reverting (session-engine/04 AC-02) - matches the design spec's ~1.8s. Both
 * `ShareWidget` and `InviteSlot` read this SAME constant so their confirmation
 * timing never drifts apart.
 */
export const COPIED_CONFIRMATION_MS = 1800;

export interface RoomInvite {
  /** The tappable `/join/:code` deep link both Copy and Share hand out. */
  joinLink: string;
  /** Whether the Web Share API is available on this browser (feature-detected once). */
  canShare: boolean;
  /** True for COPIED_CONFIRMATION_MS immediately after a successful copy. */
  copied: boolean;
  /** Copy the deep link to the clipboard; sets `copied` briefly on success, fails silently otherwise. */
  copy: () => Promise<void>;
  /** Invoke the Web Share API with the deep link; a cancel/rejection is swallowed, never thrown. */
  share: () => Promise<void>;
}

/**
 * Resolve the app's base URL for the deep link (AC-06 / CLAUDE.md section 4):
 * prefer an explicit non-empty `VITE_PUBLIC_BASE_URL` override, otherwise the
 * running app's own origin. Guarded for a non-browser environment (no
 * `window`) - falls back to an empty origin rather than throwing, which still
 * yields a coherent (if relative) `/join/<code>` path.
 *
 * Exported (rather than a private module closure) purely so `useRoomInvite.
 * test.ts` can cover its origin-selection branches directly without a DOM -
 * this hook's stateful pieces (the `copied` timer, the `navigator.clipboard`/
 * `navigator.share` calls) still need a render harness this repo does not
 * have yet, but this one pure decision does not.
 */
export function resolveOrigin(): string {
  const configuredBase = import.meta.env.VITE_PUBLIC_BASE_URL;
  if (configuredBase && configuredBase.length > 0) return configuredBase;
  return typeof window !== 'undefined' ? window.location.origin : '';
}

/**
 * The one shared invite action for a room code: builds the deep link once,
 * feature-detects Web Share once, and exposes `copy`/`share` plus the
 * "Copied!" confirmation timer. See the file header for the full contract.
 */
export function useRoomInvite(code: string): RoomInvite {
  const joinLink = buildJoinLink(code, resolveOrigin());

  // "Copied!" confirmation (AC-02): local state only, reverts after
  // COPIED_CONFIRMATION_MS. The timer is cleared on unmount so we never call
  // setState after the owning component is gone.
  const [copied, setCopied] = useState(false);
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
    };
  }, []);

  const copy = async () => {
    // Guard clipboard availability gracefully (e.g. insecure context / an
    // older browser) - never throw, just skip the confirmation.
    if (typeof navigator === 'undefined' || !navigator.clipboard) return;
    try {
      // session-engine/06 AC-04/AC-05: copy the tappable deep link, not the
      // bare code - this path is independent of Web Share availability, so it
      // still yields the full link with no error when Share is unavailable.
      await navigator.clipboard.writeText(joinLink);
      // analytics/01 (AC-07): the share loop's copy-link path (anonymous - the
      // method label only, never the code). No-op unless configured + consented.
      trackEvent(ANALYTICS_EVENTS.InviteShared, { method: 'copy-link' });
      setCopied(true);
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
      copiedTimer.current = setTimeout(() => setCopied(false), COPIED_CONFIRMATION_MS);
    } catch {
      // Clipboard permission denied or unavailable - fail silently, no error surfaced.
    }
  };

  // Feature-detect the Web Share API once per render (it does not change over
  // the caller's lifetime) - AC-04 of story 04: hide/skip Share entirely when
  // it is not available (e.g. desktop Chrome) rather than a dead action.
  const canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  const share = async () => {
    if (!canShare) return;
    try {
      // session-engine/06 AC-01: the tappable deep link travels in `url`
      // alongside the human-readable `text` (still naming the bare code, so
      // a channel that drops `url` - e.g. some SMS clients - still shows a
      // readable message), so the recipient can just tap through.
      await navigator.share({
        title: 'QuibbleStone',
        text: `Join my QuibbleStone game! Room code: ${code}`,
        url: joinLink,
      });
      // analytics/01 (AC-07): the share loop's OS/browser share-sheet path -
      // recorded only on a completed share (a user cancel throws AbortError and is
      // swallowed below, so it is not counted). Anonymous - the method label only.
      trackEvent(ANALYTICS_EVENTS.InviteShared, { method: 'share-sheet' });
    } catch {
      // A user-cancelled share (AbortError) or any other rejection should
      // never surface as an unhandled error or noisy console log.
    }
  };

  return { joinLink, canShare, copied, copy, share };
}
