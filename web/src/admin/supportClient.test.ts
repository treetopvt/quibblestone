// ----------------------------------------------------------------------------
//  supportClient.test.ts - covers the Support lookup + verbs web client
//  (sysadmin-console/07, #243). The account lookup, count-only projections, verbs,
//  and operator authorization are server-side; this proves the CLIENT contract: it
//  GETs the account summary, POSTs each verb, narrows the responses, and FAILS
//  GRACEFULLY (never throws).
//
//  THE FIREWALL (AC-08): every wire shape here is account-plane + content-plane facts
//  only - an account summary (id, email, created-at, grants, subscription, COUNTS) and
//  verb results (message / slug + expiry). There is no player / room / session / byline /
//  per-tale field on the wire, so none can leak into the client.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  extendTaleTtl,
  lookupAccount,
  resendMagicLink,
  restoreKeepsake,
  resyncSubscription,
} from './supportClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown, ok = true): Promise<Response> =>
  Promise.resolve({ ok, status: ok ? 200 : 429, json: () => Promise.resolve(body) } as Response);

function header(init: RequestInit | undefined, name: string): string | undefined {
  return (init?.headers as Record<string, string> | undefined)?.[name];
}

const summaryBody = {
  accountExists: true,
  accountId: '11111111-1111-1111-1111-111111111111',
  email: 'buyer@example.com',
  createdUtc: '2026-01-01T00:00:00Z',
  grants: [{ capabilityKey: 'library.full', label: 'Full Library', validThrough: null, source: 'Operator', active: true }],
  subscription: {
    hasSubscription: true,
    plan: 'family-plan',
    status: 'active',
    validThrough: '2026-12-01T00:00:00Z',
    stripeSubscriptionId: 'sub_123',
    mode: 'test',
  },
  vaultTales: { available: false, count: null },
  linkedDevices: { available: true, count: 2 },
};

describe('lookupAccount', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the support account endpoint and narrows the summary', async () => {
    const fetchFn = mockFetch(() => okJson(summaryBody));

    const result = await lookupAccount('buyer@example.com', 'CRED');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/support/accounts/buyer%40example.com');
    expect(init?.method).toBe('GET');
    expect(header(init, 'Authorization')).toBe('Bearer CRED');
    expect(init?.credentials).toBe('include');
    expect(result.ok).toBe(true);
    expect(result.summary?.accountId).toBe('11111111-1111-1111-1111-111111111111');
    expect(result.summary?.subscription.plan).toBe('family-plan');
    expect(result.summary?.linkedDevices.count).toBe(2);
  });

  it('keeps the vault count as the dependency-tolerant unavailable state', async () => {
    mockFetch(() => okJson(summaryBody));
    const result = await lookupAccount('buyer@example.com');
    expect(result.summary?.vaultTales.available).toBe(false);
    expect(result.summary?.vaultTales.count).toBeNull();
  });

  it('surfaces the clear not-found state without erroring', async () => {
    mockFetch(() =>
      okJson({
        accountExists: false,
        accountId: null,
        email: 'nobody@example.com',
        createdUtc: null,
        grants: [],
        subscription: { hasSubscription: false, plan: null, status: null, validThrough: null, stripeSubscriptionId: null, mode: null },
        vaultTales: { available: false, count: null },
        linkedDevices: { available: false, count: null },
      }),
    );

    const result = await lookupAccount('nobody@example.com');

    expect(result.ok).toBe(true);
    expect(result.summary?.accountExists).toBe(false);
  });

  it('fails gracefully on a non-OK status (never throws)', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401 } as Response));
    const result = await lookupAccount('buyer@example.com');
    expect(result.ok).toBe(false);
    expect(result.summary).toBeNull();
    expect(result.message.length).toBeGreaterThan(0);
  });
});

describe('the verbs', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('resendMagicLink POSTs the email and returns the server message', async () => {
    const fetchFn = mockFetch(() => okJson({ ok: true, message: 'A fresh sign-in link is on its way.' }));

    const result = await resendMagicLink('buyer@example.com', 'CRED');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/support/resend-link');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ email: 'buyer@example.com' });
    expect(header(init, 'Authorization')).toBe('Bearer CRED');
    expect(result.ok).toBe(true);
    expect(result.message).toContain('on its way');
  });

  it('extendTaleTtl POSTs the slug (a direct content input, never a search key)', async () => {
    const fetchFn = mockFetch(() => okJson({ outcome: 'extended', slug: 'abc', newExpiryUtc: '2026-12-01T00:00:00Z', message: 'Extended.' }));

    const result = await extendTaleTtl('abc');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/support/tales/extend-ttl');
    expect(JSON.parse(String(init?.body))).toEqual({ slug: 'abc' });
    expect(result.ok).toBe(true);
  });

  it('restoreKeepsake POSTs the vaultId + taleId with the single confirmation', async () => {
    const fetchFn = mockFetch(() => okJson({ outcome: 'restored', message: 'Restored.' }));

    await restoreKeepsake('vault-1', 'tale-1', 'CRED');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/support/vault/restore');
    expect(JSON.parse(String(init?.body))).toEqual({ vaultId: 'vault-1', taleId: 'tale-1', confirm: true });
  });

  it('resyncSubscription POSTs the accountId', async () => {
    const fetchFn = mockFetch(() => okJson({ accountFound: true, message: 'Resync complete.' }));

    await resyncSubscription('11111111-1111-1111-1111-111111111111');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/support/resync');
    expect(JSON.parse(String(init?.body))).toEqual({ accountId: '11111111-1111-1111-1111-111111111111' });
  });

  it('treats a 429 debounce/cap as a non-OK result carrying the server message', async () => {
    mockFetch(() => okJson({ ok: false, message: 'Please wait a moment.' }, false));

    const result = await resyncSubscription('11111111-1111-1111-1111-111111111111');

    expect(result.ok).toBe(false);
    expect(result.message).toContain('wait');
  });

  it('fails gracefully when the network throws', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await resendMagicLink('buyer@example.com');
    expect(result.ok).toBe(false);
    expect(result.message.length).toBeGreaterThan(0);
  });
});
