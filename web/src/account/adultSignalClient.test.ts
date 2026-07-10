// ----------------------------------------------------------------------------
//  adultSignalClient.test.ts - covers the READ-ONLY adult-signal web client
//  (accounts-identity/10, #247). The real resolution is server-side; this proves
//  the CLIENT contract and, above all, the FAIL-SAFE (AC-04, a child-safety seam):
//    - a positive { adultUnlocked: true } is the ONLY thing that resolves true;
//    - a { adultUnlocked: false } body resolves false;
//    - a network error, a timeout/abort, and EVERY non-2xx (401 / 429 / 5xx) each
//      resolve to false - never throw, never default true;
//    - an unparseable / malformed body resolves false;
//    - the credential rides `Authorization: Bearer` (never the URL) with
//      credentials:'include', and is omitted entirely for an anonymous device.
//  Mirrors cloudGalleryClient.test.ts (mock fetch).
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { resolveAdultSignal } from './adultSignalClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);

const status = (code: number): Promise<Response> =>
  Promise.resolve({ ok: code >= 200 && code < 300, status: code, json: () => Promise.resolve({}) } as Response);

const CRED = 'PROTECTED-CREDENTIAL';

describe('resolveAdultSignal', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('resolves true ONLY on a positive { adultUnlocked: true } and sends the bearer', async () => {
    const fetchFn = mockFetch(() => okJson({ adultUnlocked: true }));

    const result = await resolveAdultSignal(CRED);

    expect(result).toBe(true);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/adult-signal$/);
    expect(init?.method).toBe('GET');
    expect(init?.headers).toMatchObject({ Authorization: `Bearer ${CRED}` });
    expect(init?.credentials).toBe('include');
  });

  it('resolves false when the server says adultUnlocked:false', async () => {
    mockFetch(() => okJson({ adultUnlocked: false }));
    expect(await resolveAdultSignal(CRED)).toBe(false);
  });

  it('omits the Authorization header for an anonymous device (null credential)', async () => {
    const fetchFn = mockFetch(() => okJson({ adultUnlocked: false }));

    const result = await resolveAdultSignal(null);

    expect(result).toBe(false);
    const [, init] = fetchFn.mock.calls[0];
    expect((init?.headers as Record<string, string>).Authorization).toBeUndefined();
    // Still sends credentials:'include' so a same-site purchaser cookie can be read.
    expect(init?.credentials).toBe('include');
  });

  it('FAILS SAFE to false on a network error (never throws)', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    expect(await resolveAdultSignal(CRED)).toBe(false);
  });

  it('FAILS SAFE to false on an aborted request (never throws)', async () => {
    mockFetch(() => Promise.reject(new DOMException('aborted', 'AbortError')));
    expect(await resolveAdultSignal(CRED)).toBe(false);
  });

  it('FAILS SAFE to false when the request stalls past the timeout (the AbortController fires)', async () => {
    vi.useFakeTimers();
    try {
      // A fetch that never settles on its own, but honors the abort signal the client
      // attaches - so only the client's own REQUEST_TIMEOUT_MS deadline ends it.
      mockFetch(
        (_url, init) =>
          new Promise((_resolve, reject) => {
            init?.signal?.addEventListener('abort', () =>
              reject(new DOMException('aborted', 'AbortError')),
            );
          }),
      );

      const pending = resolveAdultSignal(CRED);
      // Advance past the client deadline; the AbortController fires and the catch resolves false.
      await vi.advanceTimersByTimeAsync(6000);
      await expect(pending).resolves.toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it.each([401, 403, 429, 500, 503])('FAILS SAFE to false on a non-2xx (%i)', async (code) => {
    mockFetch(() => status(code));
    expect(await resolveAdultSignal(CRED)).toBe(false);
  });

  it('FAILS SAFE to false on an unparseable body', async () => {
    mockFetch(() =>
      Promise.resolve({ ok: true, status: 200, json: () => Promise.reject(new Error('bad json')) } as Response),
    );
    expect(await resolveAdultSignal(CRED)).toBe(false);
  });

  it('resolves false on a malformed body (missing / non-boolean field)', async () => {
    mockFetch(() => okJson({ adultUnlocked: 'yes' }));
    expect(await resolveAdultSignal(CRED)).toBe(false);
    mockFetch(() => okJson({ somethingElse: true }));
    expect(await resolveAdultSignal(CRED)).toBe(false);
  });
});
