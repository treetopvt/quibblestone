// ----------------------------------------------------------------------------
//  relativeLastSeen.test.ts - Vitest coverage for the Account page's device
//  "last seen" formatter (accounts-identity/09, AC-04. See ./relativeLastSeen.ts).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { formatRelativeLastSeen } from './relativeLastSeen';

describe('formatRelativeLastSeen', () => {
  const now = new Date('2026-07-09T12:00:00.000Z');

  it('reads "never used since linking" for a null lastUsedUtc', () => {
    expect(formatRelativeLastSeen(null, now)).toBe('never used since linking');
  });

  it('falls back to "never used" for an unparseable timestamp', () => {
    expect(formatRelativeLastSeen('not-a-date', now)).toBe('never used since linking');
  });

  it('reads "moments ago" for under a minute', () => {
    expect(formatRelativeLastSeen('2026-07-09T11:59:45.000Z', now)).toBe('used moments ago');
  });

  it('formats minutes, singular and plural', () => {
    expect(formatRelativeLastSeen('2026-07-09T11:59:00.000Z', now)).toBe('used 1 minute ago');
    expect(formatRelativeLastSeen('2026-07-09T11:55:00.000Z', now)).toBe('used 5 minutes ago');
  });

  it('formats hours, singular and plural', () => {
    expect(formatRelativeLastSeen('2026-07-09T11:00:00.000Z', now)).toBe('used 1 hour ago');
    expect(formatRelativeLastSeen('2026-07-09T09:00:00.000Z', now)).toBe('used 3 hours ago');
  });

  it('formats days, singular and plural', () => {
    expect(formatRelativeLastSeen('2026-07-08T12:00:00.000Z', now)).toBe('used 1 day ago');
    expect(formatRelativeLastSeen('2026-07-02T12:00:00.000Z', now)).toBe('used 7 days ago');
  });
});
