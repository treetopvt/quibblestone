// ----------------------------------------------------------------------------
//  joinLink.test.ts - Vitest coverage for the deep-link builder
//  (session-engine/06, see ./joinLink.ts).
//
//  Pure-function coverage only: the normal case, an origin with a trailing
//  slash (must never double up to `//join`), and that the code lands right
//  after the `/join/` segment regardless of origin shape.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { buildJoinLink } from './joinLink';

describe('buildJoinLink', () => {
  it('builds a full deep link from an origin with no trailing slash', () => {
    expect(buildJoinLink('MOSS', 'https://app.quibblestone.example')).toBe(
      'https://app.quibblestone.example/join/MOSS',
    );
  });

  it('strips a trailing slash on the origin so the path never doubles up', () => {
    expect(buildJoinLink('MOSS', 'https://app.quibblestone.example/')).toBe(
      'https://app.quibblestone.example/join/MOSS',
    );
  });

  it('places the code immediately after the /join/ segment', () => {
    const link = buildJoinLink('WXYZ', 'http://localhost:5173');
    expect(link.endsWith('/join/WXYZ')).toBe(true);
    expect(link).toBe('http://localhost:5173/join/WXYZ');
  });
});
