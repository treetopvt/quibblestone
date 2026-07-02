// ----------------------------------------------------------------------------
//  publishTale.test.ts - covers the web publish/revoke client for the shareable
//  tale link (keepsake-gallery/04). The real publish (re-vet, slug, storage, the
//  public page) is server-side; this proves the CLIENT contract: it POSTs to the
//  right endpoint with the assembled tale, parses the returned link, and FAILS
//  GRACEFULLY (resolves null / false, never throws) so the share hand-off can fall
//  back to the image / text payload (AC-01). Also pins the pure slug extractor.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { publishTale, revokeTale, slugFromTaleUrl } from './publishTale';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, json: () => Promise.resolve(body) } as Response);

describe('publishTale', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the assembled tale to /api/tales and returns the public link', async () => {
    const fetchFn = mockFetch(() => okJson({ slug: 'ABCDEFGHJKMN', url: 'https://app.test/t/ABCDEFGHJKMN' }));

    const result = await publishTale({
      title: 'A tale',
      parts: [
        { isWord: false, text: 'Once ' },
        { isWord: true, text: 'banana' },
      ],
      bylineNames: 'Sam & Mia',
    });

    expect(result).toEqual({ url: 'https://app.test/t/ABCDEFGHJKMN' });

    // It hit the tales endpoint with a POST and a JSON body carrying the parts.
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/tales$/);
    expect(init?.method).toBe('POST');
    const sent = JSON.parse(String(init?.body));
    expect(sent.title).toBe('A tale');
    expect(sent.parts).toHaveLength(2);
    expect(sent.bylineNames).toBe('Sam & Mia');
  });

  it('sends an empty byline string when none is given', async () => {
    const fetchFn = mockFetch(() => okJson({ url: 'https://app.test/t/XYZ' }));
    await publishTale({ title: 't', parts: [{ isWord: false, text: 'x' }] });
    const sent = JSON.parse(String(fetchFn.mock.calls[0][1]?.body));
    expect(sent.bylineNames).toBe('');
  });

  it('resolves null on a non-OK status (e.g. the disabled-feature 503)', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 503, json: () => Promise.resolve({}) } as Response));
    const result = await publishTale({ title: 't', parts: [{ isWord: false, text: 'x' }] });
    expect(result).toBeNull();
  });

  it('resolves null (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(publishTale({ title: 't', parts: [] })).resolves.toBeNull();
  });

  it('resolves null when the body has no usable url', async () => {
    mockFetch(() => okJson({ slug: 'ABC' }));
    const result = await publishTale({ title: 't', parts: [{ isWord: false, text: 'x' }] });
    expect(result).toBeNull();
  });
});

describe('revokeTale', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('DELETEs the slug and resolves true on success', async () => {
    const fetchFn = mockFetch(() => Promise.resolve({ ok: true } as Response));
    const ok = await revokeTale('ABCDEFGHJKMN');
    expect(ok).toBe(true);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/tales\/ABCDEFGHJKMN$/);
    expect(init?.method).toBe('DELETE');
  });

  it('resolves false for an empty slug without calling fetch', async () => {
    const fetchFn = mockFetch(() => Promise.resolve({ ok: true } as Response));
    expect(await revokeTale('')).toBe(false);
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it('resolves false (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(revokeTale('SLUG')).resolves.toBe(false);
  });
});

describe('slugFromTaleUrl', () => {
  it('extracts the trailing slug from a /t/<slug> url', () => {
    expect(slugFromTaleUrl('https://app.test/t/ABCDEFGHJKMN')).toBe('ABCDEFGHJKMN');
  });

  it('ignores a trailing query or hash', () => {
    expect(slugFromTaleUrl('https://app.test/t/SLUG12345678?x=1')).toBe('SLUG12345678');
    expect(slugFromTaleUrl('https://app.test/t/SLUG12345678#frag')).toBe('SLUG12345678');
  });

  it('accepts a trailing slash on the tale url', () => {
    expect(slugFromTaleUrl('https://app.test/t/SLUG12345678/')).toBe('SLUG12345678');
  });

  it('returns an empty string for a url that is not a /t/<slug> link', () => {
    expect(slugFromTaleUrl('')).toBe('');
    // A bare origin has no /t/<slug> - must NOT return the host (would revoke the
    // wrong slug). Copilot review PR #130.
    expect(slugFromTaleUrl('https://app.test/')).toBe('');
    expect(slugFromTaleUrl('https://app.test/join/MOSS')).toBe('');
  });
});
