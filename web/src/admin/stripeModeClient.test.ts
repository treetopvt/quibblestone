// ----------------------------------------------------------------------------
//  stripeModeClient.test.ts - covers the operator-console Stripe mode web client
//  AFTER sysadmin-console/04 (one console, one auth). Relocated + rewritten from
//  billing/stripeModeClient.test.ts: the client now presents the operator session
//  credential as `Authorization: Bearer` (the SAME way purchasersClient does), NOT
//  the deleted X-Operator-Secret shared-secret header. This proves the CLIENT
//  contract: it sends the bearer, parses the outcome, and FAILS GRACEFULLY (never
//  throws) on a 401, another failure status, a network error, or an unparseable
//  body - so the panel never shows a raw error and never guesses a mode silently.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchStripeMode, setStripeMode } from './stripeModeClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);

describe('fetchStripeMode', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the status endpoint with the operator bearer and returns the parsed mode', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ activeMode: 'test', lastChangedUtc: '2026-06-01T00:00:00Z', enabled: true }),
    );

    const result = await fetchStripeMode('operator-bearer-token');

    expect(result.outcome).toBe('ok');
    expect(result.activeMode).toBe('test');
    expect(result.lastChangedUtc).toBe('2026-06-01T00:00:00Z');
    expect(result.enabled).toBe(true);

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/stripe-mode$/);
    // Authenticated the SAME way as every other admin call: a bearer, NOT a secret header.
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer operator-bearer-token');
    expect((init?.headers as Record<string, string> | undefined)?.['X-Operator-Secret']).toBeUndefined();
    expect(init?.credentials).toBe('include');
  });

  it('passes through a null lastChangedUtc (mode never changed)', async () => {
    mockFetch(() => okJson({ activeMode: 'live', lastChangedUtc: null, enabled: true }));
    const result = await fetchStripeMode('token');
    expect(result.outcome).toBe('ok');
    expect(result.lastChangedUtc).toBeNull();
  });

  it('sends no Authorization header when the credential is null (same-site cookie path)', async () => {
    const fetchFn = mockFetch(() => okJson({ activeMode: 'test', lastChangedUtc: null, enabled: true }));
    await fetchStripeMode(null);
    const [, init] = fetchFn.mock.calls[0];
    expect((init?.headers as Record<string, string> | undefined)?.Authorization).toBeUndefined();
    expect(init?.credentials).toBe('include');
  });

  it('resolves unauthorized (never a guessed mode) on a 401', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401, json: () => Promise.resolve({}) } as Response));
    const result = await fetchStripeMode('stale-token');
    expect(result.outcome).toBe('unauthorized');
    expect(result.activeMode).toBeUndefined();
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await fetchStripeMode('token');
    expect(result.outcome).toBe('error');
    expect(result.message?.length ?? 0).toBeGreaterThan(0);
  });

  it('resolves error on a non-401 non-OK status', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 500, json: () => Promise.resolve({}) } as Response));
    const result = await fetchStripeMode('token');
    expect(result.outcome).toBe('error');
  });

  it('resolves error on an unparseable/unexpected body rather than trusting it', async () => {
    mockFetch(() => okJson({ activeMode: 'sideways', enabled: true }));
    const result = await fetchStripeMode('token');
    expect(result.outcome).toBe('error');
  });
});

describe('setStripeMode', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the mode with the operator bearer, returning the new mode + timestamp', async () => {
    const fetchFn = mockFetch(() => okJson({ activeMode: 'live', lastChangedUtc: '2026-07-03T12:00:00Z' }));

    const result = await setStripeMode('operator-bearer-token', 'live');

    expect(result.outcome).toBe('ok');
    expect(result.activeMode).toBe('live');
    expect(result.lastChangedUtc).toBe('2026-07-03T12:00:00Z');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/stripe-mode$/);
    expect(init?.method).toBe('POST');
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer operator-bearer-token');
    expect((init?.headers as Record<string, string> | undefined)?.['X-Operator-Secret']).toBeUndefined();
    expect(JSON.parse(String(init?.body)).mode).toBe('live');
  });

  it('resolves unauthorized on a 401 and does not surface a mode', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401, json: () => Promise.resolve({}) } as Response));
    const result = await setStripeMode('stale-token', 'live');
    expect(result.outcome).toBe('unauthorized');
    expect(result.activeMode).toBeUndefined();
  });

  it('resolves error on a 400 (invalid mode) rather than throwing', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 400, json: () => Promise.resolve({}) } as Response));
    const result = await setStripeMode('token', 'live');
    expect(result.outcome).toBe('error');
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await setStripeMode('token', 'test');
    expect(result.outcome).toBe('error');
  });
});
