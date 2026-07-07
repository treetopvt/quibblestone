// ----------------------------------------------------------------------------
//  cloudGalleryClient.test.ts - covers the purchaser cloud-gallery web client
//  (keepsake-gallery/05). The real per-purchaser storage / entitlement read /
//  content re-vet is server-side; this proves the CLIENT contract: it hits the
//  right endpoints with the bearer credential, parses the shapes, maps a 401 to
//  'signed-out' (AC-04), and FAILS GRACEFULLY (never throws) so the Account
//  surface never shows a raw error. Mirrors signInClient.test.ts (mock fetch).
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  deleteCloudTale,
  fetchCloudGallery,
  revokeCloudGallery,
  saveCloudTale,
} from './cloudGalleryClient';

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

describe('fetchCloudGallery', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the gallery with the bearer credential and returns parsed tales', async () => {
    const fetchFn = mockFetch(() =>
      okJson({
        tales: [
          {
            taleId: 't-1',
            title: 'The wobbly dragon',
            parts: [
              { isWord: false, text: 'The ' },
              { isWord: true, text: 'wobbly' },
              { isWord: false, text: ' dragon.' },
            ],
            bylineNames: 'carved by Sam & Mia',
            createdUtc: '2026-07-03T10:00:00Z',
          },
        ],
      }),
    );

    const result = await fetchCloudGallery(CRED);

    expect(result.status).toBe('ok');
    expect(result.tales).toHaveLength(1);
    expect(result.tales[0].parts).toHaveLength(3);
    expect(result.tales[0].title).toBe('The wobbly dragon');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/account\/gallery$/);
    expect(init?.headers).toMatchObject({ Authorization: `Bearer ${CRED}` });
    expect(init?.credentials).toBe('include');
  });

  it('maps a 401 to signed-out (never leaks data)', async () => {
    mockFetch(() => status(401));
    const result = await fetchCloudGallery(CRED);
    expect(result.status).toBe('signed-out');
    expect(result.tales).toEqual([]);
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await fetchCloudGallery(CRED);
    expect(result.status).toBe('error');
    expect(result.tales).toEqual([]);
  });

  it('drops malformed tale items rather than trusting them', async () => {
    mockFetch(() =>
      okJson({
        tales: [
          { taleId: 'good', title: 'ok', parts: [], bylineNames: '', createdUtc: '2026-07-03T10:00:00Z' },
          { taleId: 42, title: 'bad-id' },
          null,
        ],
      }),
    );
    const result = await fetchCloudGallery(CRED);
    expect(result.status).toBe('ok');
    expect(result.tales.map((t) => t.taleId)).toEqual(['good']);
  });
});

describe('saveCloudTale', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the payload with the bearer credential and returns the minted taleId', async () => {
    const fetchFn = mockFetch(() => okJson({ taleId: 'cloud-99' }));

    const result = await saveCloudTale(CRED, {
      title: 'A tale',
      parts: [{ isWord: false, text: 'hi' }],
      bylineNames: 'carved by Sam',
    });

    expect(result.status).toBe('ok');
    expect(result.taleId).toBe('cloud-99');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/account\/gallery$/);
    expect(init?.method).toBe('POST');
    expect(init?.headers).toMatchObject({ Authorization: `Bearer ${CRED}` });
    const parsed = JSON.parse(String(init?.body));
    expect(parsed.title).toBe('A tale');
    expect(parsed.bylineNames).toBe('carved by Sam');
  });

  it('maps a rejected save (400 re-vet) to error rather than crashing the batch', async () => {
    mockFetch(() => status(400));
    const result = await saveCloudTale(CRED, { title: 'x', parts: [], bylineNames: '' });
    expect(result.status).toBe('error');
    expect(result.taleId).toBeUndefined();
  });

  it('maps a 401 to signed-out', async () => {
    mockFetch(() => status(401));
    const result = await saveCloudTale(CRED, { title: 'x', parts: [], bylineNames: '' });
    expect(result.status).toBe('signed-out');
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await saveCloudTale(CRED, { title: 'x', parts: [], bylineNames: '' });
    expect(result.status).toBe('error');
  });
});

describe('deleteCloudTale', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('DELETEs the single tale by id and resolves ok on 204', async () => {
    const fetchFn = mockFetch(() => status(204));
    const result = await deleteCloudTale(CRED, 't-1');
    expect(result.status).toBe('ok');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/account\/gallery\/t-1$/);
    expect(init?.method).toBe('DELETE');
    expect(init?.headers).toMatchObject({ Authorization: `Bearer ${CRED}` });
  });

  it('encodes an odd tale id into the path', async () => {
    const fetchFn = mockFetch(() => status(204));
    await deleteCloudTale(CRED, 'a/b c');
    const [url] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/account\/gallery\/a%2Fb%20c$/);
  });

  it('maps a 401 to signed-out and non-2xx to error', async () => {
    mockFetch(() => status(401));
    expect((await deleteCloudTale(CRED, 't')).status).toBe('signed-out');
    mockFetch(() => status(500));
    expect((await deleteCloudTale(CRED, 't')).status).toBe('error');
  });
});

describe('revokeCloudGallery', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('DELETEs the whole gallery and resolves ok on 204', async () => {
    const fetchFn = mockFetch(() => status(204));
    const result = await revokeCloudGallery(CRED);
    expect(result.status).toBe('ok');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/account\/gallery$/);
    expect(init?.method).toBe('DELETE');
  });

  it('maps a server failure to error (caller re-issues until empty)', async () => {
    mockFetch(() => status(500));
    const result = await revokeCloudGallery(CRED);
    expect(result.status).toBe('error');
  });

  it('resolves error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await revokeCloudGallery(CRED);
    expect(result.status).toBe('error');
  });
});
