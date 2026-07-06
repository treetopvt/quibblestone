// ----------------------------------------------------------------------------
//  reportedTalesClient.test.ts - covers the operator review-queue web client
//  (sysadmin-console/03, #137). The real moderation state + operator authorization
//  is server-side; this proves the CLIENT contract: it GETs the queue and POSTs the
//  confirm / restore actions to the SEPARATE admin endpoints, narrows the responses,
//  and FAILS GRACEFULLY (never throws) so the queue never shows a raw error.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { confirmHiddenTale, loadReviewQueue, restoreHiddenTale } from './reportedTalesClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, json: () => Promise.resolve(body) } as Response);

/** Reads the Authorization header off a captured fetch init (headers is a plain object here). */
function authHeader(init?: RequestInit): string | undefined {
  return (init?.headers as Record<string, string> | undefined)?.Authorization;
}

describe('loadReviewQueue', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the queue endpoint and returns the hidden tales', async () => {
    const fetchFn = mockFetch(() =>
      okJson({
        tales: [
          {
            slug: 'HIDDENSLUG12',
            title: 'The flagged saga',
            parts: [
              { isWord: false, text: 'Once a ' },
              { isWord: true, text: 'wombat' },
            ],
            bylineNames: 'Sam & Mia',
            reportCount: 4,
          },
        ],
      }),
    );

    const result = await loadReviewQueue();

    expect(result.ok).toBe(true);
    expect(result.tales).toHaveLength(1);
    expect(result.tales[0].slug).toBe('HIDDENSLUG12');
    expect(result.tales[0].reportCount).toBe(4);
    expect(result.tales[0].parts[1]).toEqual({ isWord: true, text: 'wombat' });

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/reported-tales$/);
    expect(init?.method).toBe('GET');
    expect(init?.credentials).toBe('include');
  });

  it('attaches the operator credential as a bearer when the shell holds one (cross-origin path)', async () => {
    const fetchFn = mockFetch(() => okJson({ tales: [] }));
    await loadReviewQueue('PROTECTED-CRED');
    const [, init] = fetchFn.mock.calls[0];
    expect(authHeader(init)).toBe('Bearer PROTECTED-CRED');
    expect(init?.credentials).toBe('include');
  });

  it('resolves ok=false (never throws) on a non-OK status (e.g. 401)', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401, json: () => Promise.resolve({}) } as Response));
    const result = await loadReviewQueue();
    expect(result.ok).toBe(false);
    expect(result.tales).toHaveLength(0);
    expect(result.message.length).toBeGreaterThan(0);
  });

  it('resolves ok=false (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await loadReviewQueue();
    expect(result.ok).toBe(false);
  });

  it('drops malformed queue entries rather than trusting them', async () => {
    mockFetch(() =>
      okJson({
        tales: [
          { slug: 'GOODSLUG1234', title: 'Fine', parts: [], bylineNames: '', reportCount: 3 },
          { slug: 42, title: 'bad' }, // malformed - dropped
        ],
      }),
    );
    const result = await loadReviewQueue();
    expect(result.tales).toHaveLength(1);
    expect(result.tales[0].slug).toBe('GOODSLUG1234');
  });
});

describe('confirmHiddenTale / restoreHiddenTale', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs confirm to the slug-scoped endpoint and returns applied', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ slug: 'HIDDENSLUG12', applied: true, message: 'confirmed hidden' }),
    );

    const result = await confirmHiddenTale('HIDDENSLUG12');

    expect(result.ok).toBe(true);
    expect(result.applied).toBe(true);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/reported-tales\/HIDDENSLUG12\/confirm$/);
    expect(init?.method).toBe('POST');
    expect(init?.credentials).toBe('include');
  });

  it('POSTs restore to the slug-scoped endpoint', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ slug: 'HIDDENSLUG12', applied: true, message: 'restored' }),
    );

    const result = await restoreHiddenTale('HIDDENSLUG12');

    expect(result.applied).toBe(true);
    const [url] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/reported-tales\/HIDDENSLUG12\/restore$/);
  });

  it('encodes the slug in the action URL', async () => {
    const fetchFn = mockFetch(() => okJson({ slug: 'a/b', applied: false, message: 'x' }));
    await confirmHiddenTale('a/b');
    const [url] = fetchFn.mock.calls[0];
    expect(url).toContain('a%2Fb');
  });

  it('attaches the operator credential as a bearer on a confirm action', async () => {
    const fetchFn = mockFetch(() => okJson({ slug: 'S', applied: true, message: 'ok' }));
    await confirmHiddenTale('S', 'PROTECTED-CRED');
    const [, init] = fetchFn.mock.calls[0];
    expect(authHeader(init)).toBe('Bearer PROTECTED-CRED');
    expect(init?.credentials).toBe('include');
  });

  it('resolves ok=false (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await restoreHiddenTale('HIDDENSLUG12');
    expect(result.ok).toBe(false);
    expect(result.applied).toBe(false);
  });
});
