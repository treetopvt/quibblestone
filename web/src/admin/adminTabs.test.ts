// ----------------------------------------------------------------------------
//  adminTabs.test.ts - guards AC-01 of sysadmin-console/05: the shell shows
//  exactly three tabs - Support, Content, Operations - in that order, replacing
//  the prior two/three-interim-tab shell. A pure-module assertion (no DOM).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { ADMIN_TABS } from './adminTabs';

describe('ADMIN_TABS', () => {
  it('has exactly three entries', () => {
    expect(ADMIN_TABS.length).toBe(3);
  });

  it('has values support, content, ops in that order', () => {
    expect(ADMIN_TABS.map((tab) => tab.value)).toEqual(['support', 'content', 'ops']);
  });

  it('has labels Support, Content, Operations in that order', () => {
    expect(ADMIN_TABS.map((tab) => tab.label)).toEqual(['Support', 'Content', 'Operations']);
  });
});
