// ----------------------------------------------------------------------------
//  useRoomInvite.test.ts - Vitest coverage for the shared invite helper
//  (session-engine/11, see ./useRoomInvite.ts).
//
//  This suite's vitest.config.ts runs in the `node` environment (no DOM/React
//  render harness available in this repo yet - see CLAUDE.md section 9), so it
//  covers only the ONE pure decision `useRoomInvite` makes outside of React
//  state/effects: `resolveOrigin`'s choice between an explicit
//  `VITE_PUBLIC_BASE_URL` override and the running app's own `window.location.
//  origin`, plus its no-`window` fallback (AC-06 / CLAUDE.md section 4 - never
//  a hardcoded host). The stateful pieces (the `copied` confirmation timer, the
//  `navigator.clipboard`/`navigator.share` calls) are exercised manually per
//  the story's Tests table, mirroring how `Lobby.tsx`'s other interactive
//  pieces are verified today.
// ----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { resolveOrigin } from './useRoomInvite';

const originalWindow = (globalThis as unknown as { window?: Window }).window;

describe('resolveOrigin', () => {
  beforeEach(() => {
    vi.unstubAllEnvs();
  });

  afterEach(() => {
    (globalThis as unknown as { window?: Window }).window = originalWindow;
    vi.unstubAllEnvs();
  });

  it('prefers a non-empty VITE_PUBLIC_BASE_URL override over window.location.origin', () => {
    vi.stubEnv('VITE_PUBLIC_BASE_URL', 'https://app.quibblestone.example');
    (globalThis as unknown as { window: Window }).window = {
      location: { origin: 'http://localhost:5173' },
    } as unknown as Window;

    expect(resolveOrigin()).toBe('https://app.quibblestone.example');
  });

  it('falls back to window.location.origin when no override is configured', () => {
    vi.stubEnv('VITE_PUBLIC_BASE_URL', '');
    (globalThis as unknown as { window: Window }).window = {
      location: { origin: 'http://localhost:5173' },
    } as unknown as Window;

    expect(resolveOrigin()).toBe('http://localhost:5173');
  });

  it('falls back to an empty origin when window is unavailable (never throws)', () => {
    vi.stubEnv('VITE_PUBLIC_BASE_URL', '');
    delete (globalThis as unknown as { window?: Window }).window;

    expect(() => resolveOrigin()).not.toThrow();
    expect(resolveOrigin()).toBe('');
  });
});
