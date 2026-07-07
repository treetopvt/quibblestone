// ----------------------------------------------------------------------------
//  stripeModeClient.test.ts - covers the operator Stripe mode web client
//  (billing-entitlements/07). The real mode resolution/persistence/flip is
//  server-side (story 06); this proves the CLIENT contract: it sends the
//  operator secret as the `X-Operator-Secret` header, parses the outcome, and
//  FAILS GRACEFULLY (never throws) on a 401, another failure status, a
//  network error, or an unparseable body - so the operator screen never shows
//  a raw error and never guesses/defaults a mode silently (AC-01).
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

  it('GETs the status endpoint with the operator secret header and returns the parsed mode', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ activeMode: 'test', lastChangedUtc: '2026-06-01T00:00:00Z', enabled: true }),
    );

    const result = await fetchStripeMode('shh-its-a-secret');

    expect(result.outcome).toBe('ok');
    expect(result.activeMode).toBe('test');
    expect(result.lastChangedUtc).toBe('2026-06-01T00:00:00Z');
    expect(result.enabled).toBe(true);

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/stripe-mode$/);
    expect((init?.headers as Record<string, string>)['X-Operator-Secret']).toBe('shh-its-a-secret');
  });

  it('passes through a null lastChangedUtc (mode never changed)', async () => {
    mockFetch(() => okJson({ activeMode: 'live', lastChangedUtc: null, enabled: true }));
    const result = await fetchStripeMode('secret');
    expect(result.outcome).toBe('ok');
    expect(result.lastChangedUtc).toBeNull();
  });

  it('resolves unauthorized (never a guessed mode) on a 401', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401, json: () => Promise.resolve({}) } as Response));
    const result = await fetchStripeMode('wrong-secret');
    expect(result.outcome).toBe('unauthorized');
    expect(result.activeMode).toBeUndefined();
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await fetchStripeMode('secret');
    expect(result.outcome).toBe('error');
    expect(result.message?.length ?? 0).toBeGreaterThan(0);
  });

  it('resolves error on a non-401 non-OK status', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 500, json: () => Promise.resolve({}) } as Response));
    const result = await fetchStripeMode('secret');
    expect(result.outcome).toBe('error');
  });

  it('resolves error on an unparseable/unexpected body rather than trusting it', async () => {
    mockFetch(() => okJson({ activeMode: 'sideways', enabled: true }));
    const result = await fetchStripeMode('secret');
    expect(result.outcome).toBe('error');
  });
});

describe('setStripeMode', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the mode and operator secret header, returning the new mode + timestamp', async () => {
    const fetchFn = mockFetch(() => okJson({ activeMode: 'live', lastChangedUtc: '2026-07-03T12:00:00Z' }));

    const result = await setStripeMode('shh-its-a-secret', 'live');

    expect(result.outcome).toBe('ok');
    expect(result.activeMode).toBe('live');
    expect(result.lastChangedUtc).toBe('2026-07-03T12:00:00Z');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/stripe-mode$/);
    expect(init?.method).toBe('POST');
    expect((init?.headers as Record<string, string>)['X-Operator-Secret']).toBe('shh-its-a-secret');
    expect(JSON.parse(String(init?.body)).mode).toBe('live');
  });

  it('resolves unauthorized on a 401 and does not surface a mode', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401, json: () => Promise.resolve({}) } as Response));
    const result = await setStripeMode('wrong-secret', 'live');
    expect(result.outcome).toBe('unauthorized');
    expect(result.activeMode).toBeUndefined();
  });

  it('resolves error on a 400 (invalid mode) rather than throwing', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 400, json: () => Promise.resolve({}) } as Response));
    const result = await setStripeMode('secret', 'live');
    expect(result.outcome).toBe('error');
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await setStripeMode('secret', 'test');
    expect(result.outcome).toBe('error');
  });
});
