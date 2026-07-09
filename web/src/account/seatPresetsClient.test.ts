// ----------------------------------------------------------------------------
//  seatPresetsClient.test.ts - covers the kid-seat-preset web client
//  (accounts-identity/08, issue #228). The real list/create/update/delete +
//  safety filtering is server-side; this proves the CLIENT contract: it calls the
//  right account-plane endpoints with the family bearer, narrows presets to a
//  { id, nickname, variant } shape (AC-05), surfaces the server's friendly
//  rejection message on a 400 (AC-04), maps a 401 to 'signed-out', and FAILS
//  GRACEFULLY (never throws) so a hiccup never breaks a screen.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchPresets, createPreset, updatePreset, deletePreset } from './seatPresetsClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const json = (status: number, body: unknown): Promise<Response> =>
  Promise.resolve({
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  } as Response);

describe('fetchPresets', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the presets endpoint with the bearer and returns the narrowed list', async () => {
    const fetchFn = mockFetch(() =>
      json(200, { presets: [{ id: 'p1', nickname: 'Emma', variant: 'gold' }] }),
    );

    const result = await fetchPresets('cred-123');

    expect(result.status).toBe('ok');
    expect(result.presets).toEqual([{ id: 'p1', nickname: 'Emma', variant: 'gold' }]);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/presets$/);
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer cred-123');
  });

  it('narrows an unknown variant off the wire to the default (teal)', async () => {
    mockFetch(() => json(200, { presets: [{ id: 'p1', nickname: 'Emma', variant: 'rainbow' }] }));
    const result = await fetchPresets('cred');
    expect(result.presets[0].variant).toBe('teal');
  });

  it('maps a 401 to signed-out', async () => {
    mockFetch(() => json(401, {}));
    const result = await fetchPresets('cred');
    expect(result.status).toBe('signed-out');
    expect(result.presets).toEqual([]);
  });

  it('resolves error (never throws) on a transport failure', async () => {
    mockFetch(() => Promise.reject(new Error('network')));
    const result = await fetchPresets('cred');
    expect(result.status).toBe('error');
  });
});

describe('createPreset', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the nickname + variant and returns the saved preset', async () => {
    const fetchFn = mockFetch(() => json(200, { id: 'p9', nickname: 'Sam', variant: 'coral' }));

    const result = await createPreset('cred', 'Sam', 'coral');

    expect(result).toEqual({ status: 'ok', preset: { id: 'p9', nickname: 'Sam', variant: 'coral' } });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/presets$/);
    expect(init?.method).toBe('POST');
    const sent = JSON.parse(String(init?.body));
    expect(sent).toEqual({ nickname: 'Sam', variant: 'coral' });
  });

  it('surfaces the server rejection message on a 400 (AC-04)', async () => {
    mockFetch(() => json(400, { message: "Let's try a different name." }));
    const result = await createPreset('cred', 'badword', 'gold');
    expect(result).toEqual({ status: 'invalid', message: "Let's try a different name." });
  });

  it('maps a 401 to signed-out', async () => {
    mockFetch(() => json(401, {}));
    const result = await createPreset('cred', 'Sam', 'gold');
    expect(result).toEqual({ status: 'signed-out' });
  });
});

describe('updatePreset', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('PUTs to the id-scoped endpoint', async () => {
    const fetchFn = mockFetch(() => json(200, { id: 'p1', nickname: 'Emmie', variant: 'teal' }));

    const result = await updatePreset('cred', 'p1', 'Emmie', 'teal');

    expect(result).toEqual({ status: 'ok', preset: { id: 'p1', nickname: 'Emmie', variant: 'teal' } });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/presets\/p1$/);
    expect(init?.method).toBe('PUT');
  });

  it('maps a 404 (stale id) to error', async () => {
    mockFetch(() => json(404, {}));
    const result = await updatePreset('cred', 'gone', 'X', 'gold');
    expect(result).toEqual({ status: 'error' });
  });
});

describe('deletePreset', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('DELETEs the id-scoped endpoint and treats 204 as done', async () => {
    const fetchFn = mockFetch(() => json(204, {}));
    const result = await deletePreset('cred', 'p1');
    expect(result).toBe('ok');
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/presets\/p1$/);
    expect(init?.method).toBe('DELETE');
  });

  it('treats a 404 (already gone / cross-account) as an idempotent done', async () => {
    mockFetch(() => json(404, {}));
    expect(await deletePreset('cred', 'gone')).toBe('ok');
  });

  it('maps a 401 to signed-out and a transport failure to error', async () => {
    mockFetch(() => json(401, {}));
    expect(await deletePreset('cred', 'p1')).toBe('signed-out');
    vi.unstubAllGlobals();
    mockFetch(() => Promise.reject(new Error('network')));
    expect(await deletePreset('cred', 'p1')).toBe('error');
  });
});
