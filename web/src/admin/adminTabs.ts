// ----------------------------------------------------------------------------
//  adminTabs.ts - the operator-console JOB list (sysadmin-console/05, the jobs
//  shell reorganization). ADR 0003 Layer 3 reframes the back office around three
//  jobs an operator does - Support (find a person, fix their problem), Content
//  (moderation - the reported-tales review queue, later joined by content-factory
//  vetting and pack publishing in this same shell), and Operations (settings/
//  flags, Stripe mode, later an AI spend snapshot linking OUT to App Insights) -
//  rather than one tab per feature that happened to ship a screen (the old
//  'review' | 'entitlements' | 'stripe-mode' flat shell this replaces, AC-01).
//
//  PURE MODULE, NO DOM (deliberate): this list is its own file, with no React
//  import, so AC-01 ("three tabs, in this order, with these labels") is a plain
//  Vitest `.test.ts` assertion - no component-render harness needed (this repo's
//  Vitest config has no @testing-library/react or jsdom installed).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** Which post-login back-office JOB is showing (a plain toggle, not a route). */
export type AdminTab = 'support' | 'content' | 'ops';

/** One tab's stable value + its human-facing label. */
export interface AdminTabDescriptor {
  value: AdminTab;
  label: string;
}

/**
 * The three operator jobs, in the order they render (AC-01). Support first (the
 * most common day-to-day job: find a person, fix their problem), then Content
 * (moderation), then Operations (settings/flags + Stripe mode) last, matching
 * ADR 0003 Layer 3's job ordering.
 */
export const ADMIN_TABS: readonly AdminTabDescriptor[] = [
  { value: 'support', label: 'Support' },
  { value: 'content', label: 'Content' },
  { value: 'ops', label: 'Operations' },
] as const;
