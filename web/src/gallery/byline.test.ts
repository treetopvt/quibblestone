// ----------------------------------------------------------------------------
//  byline.test.ts - Vitest spec for the keepsake-gallery image byline
//  formatting (keepsake-gallery/02, PART C wiring).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { formatCrewByline, joinNamesReadably } from './byline';

describe('joinNamesReadably', () => {
  it('returns an empty string for an empty list', () => {
    expect(joinNamesReadably([])).toBe('');
  });

  it('returns the single name as-is for one name', () => {
    expect(joinNamesReadably(['Sam'])).toBe('Sam');
  });

  it('joins two names with an ampersand', () => {
    expect(joinNamesReadably(['Sam', 'Mia'])).toBe('Sam & Mia');
  });

  it('joins three or more names with commas and a trailing ampersand', () => {
    expect(joinNamesReadably(['Sam', 'Mia', 'Bo'])).toBe('Sam, Mia & Bo');
    expect(joinNamesReadably(['Sam', 'Mia', 'Bo', 'Zoe'])).toBe('Sam, Mia, Bo & Zoe');
  });
});

describe('formatCrewByline', () => {
  it('returns undefined for an empty crew', () => {
    expect(formatCrewByline([])).toBeUndefined();
  });

  it('formats a single-name byline', () => {
    expect(formatCrewByline(['Sam'])).toBe('carved by Sam');
  });

  it('formats a multi-name byline matching joinNamesReadably', () => {
    expect(formatCrewByline(['Sam', 'Mia', 'Bo'])).toBe('carved by Sam, Mia & Bo');
  });
});
