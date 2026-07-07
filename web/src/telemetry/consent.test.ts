// ----------------------------------------------------------------------------
//  consent.test.ts - proves the analytics-consent store is robust and its default
//  posture is explicit (analytics/01, AC-05). Consent gates whether ANY analytics
//  loads or sends, so it must survive a disabled / throwing localStorage (private
//  browsing, quota) by reading as 'unset' rather than crashing, and its
//  default-for-an-unchosen-player must be a single asserted token. Mirrors
//  deviceId.test.ts's fail-soft coverage.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ANALYTICS_DEFAULT_CONSENT,
  effectiveConsent,
  isConsentChoice,
  loadConsent,
  saveConsent,
} from './consent';

describe('isConsentChoice', () => {
  it('accepts only "granted" or "denied"', () => {
    expect(isConsentChoice('granted')).toBe(true);
    expect(isConsentChoice('denied')).toBe(true);
  });

  it('rejects unset, null, and anything else', () => {
    expect(isConsentChoice('unset')).toBe(false);
    expect(isConsentChoice(null)).toBe(false);
    expect(isConsentChoice(undefined)).toBe(false);
    expect(isConsentChoice('')).toBe(false);
    expect(isConsentChoice('yes')).toBe(false);
    expect(isConsentChoice(1)).toBe(false);
  });
});

describe('effectiveConsent', () => {
  it('resolves an unchosen player to the documented default token', () => {
    expect(effectiveConsent('unset')).toBe(ANALYTICS_DEFAULT_CONSENT);
  });

  it('resolves a made choice to itself (a choice always wins)', () => {
    expect(effectiveConsent('granted')).toBe('granted');
    expect(effectiveConsent('denied')).toBe('denied');
  });
});

describe('loadConsent / saveConsent', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('round-trips a saved choice', () => {
    const store = new Map<string, string>();
    vi.stubGlobal('window', {
      localStorage: {
        getItem: (k: string) => store.get(k) ?? null,
        setItem: (k: string, v: string) => void store.set(k, v),
      },
    });

    saveConsent('denied');
    expect(loadConsent()).toBe('denied');
    saveConsent('granted');
    expect(loadConsent()).toBe('granted');
  });

  it('reads a missing or corrupt value as "unset" (never assumes consent)', () => {
    const store = new Map<string, string>([['qs.analytics.consent.v1', 'garbage']]);
    vi.stubGlobal('window', {
      localStorage: {
        getItem: (k: string) => store.get(k) ?? null,
        setItem: (k: string, v: string) => void store.set(k, v),
      },
    });
    expect(loadConsent()).toBe('unset');
  });

  it('does not throw and reads "unset" when storage throws', () => {
    vi.stubGlobal('window', {
      localStorage: {
        getItem: () => {
          throw new Error('storage disabled');
        },
        setItem: () => {
          throw new Error('storage disabled');
        },
      },
    });
    expect(() => saveConsent('granted')).not.toThrow();
    expect(loadConsent()).toBe('unset');
  });
});
