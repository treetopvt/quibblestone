// ----------------------------------------------------------------------------
//  settingsClient.test.ts - covers the operator-console runtime-settings web
//  client (sysadmin-console/05, AC-04). Proves the DEPENDENCY-TOLERANCE contract:
//  a network failure, any non-2xx status, or an unparseable/non-array body all
//  collapse to 'unavailable' - never a thrown error - while a genuine 2xx array
//  response parses into the settings list.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchAdminSettings } from './settingsClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);

const statusOnly = (status: number): Promise<Response> =>
  Promise.resolve({ ok: false, status, json: () => Promise.resolve({}) } as Response);

describe('fetchAdminSettings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('resolves unavailable on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await fetchAdminSettings('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 404', async () => {
    mockFetch(() => statusOnly(404));
    const result = await fetchAdminSettings('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 500', async () => {
    mockFetch(() => statusOnly(500));
    const result = await fetchAdminSettings('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 200 with a non-array body', async () => {
    mockFetch(() => okJson({}));
    const result = await fetchAdminSettings('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable when the 2xx body cannot be parsed as JSON', async () => {
    // A 200 whose body throws / rejects on .json() (malformed JSON, truncated stream) -
    // the client's .catch(() => null) must collapse it to 'unavailable', never throw.
    mockFetch(() =>
      Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.reject(new SyntaxError('Unexpected end of JSON input')),
      } as Response),
    );
    const result = await fetchAdminSettings('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves available with parsed settings on a 200 array body', async () => {
    const fetchFn = mockFetch(() =>
      okJson([
        {
          key: 'ai.fresh-runes.enabled',
          type: 'bool',
          description: 'Enable Fresh Runes AI jumble',
          codeDefault: false,
          effectiveValue: true,
          override: true,
          bounds: null,
          requiresConfirmation: false,
        },
        { key: 'ai.spend-cap-usd', effectiveValue: 25 },
      ]),
    );

    const result = await fetchAdminSettings('operator-bearer-token');

    expect(result.outcome).toBe('available');
    expect(result.settings?.length).toBe(2);
    expect(result.settings?.[0]).toEqual({
      key: 'ai.fresh-runes.enabled',
      description: 'Enable Fresh Runes AI jumble',
      effectiveValue: true,
    });
    expect(result.settings?.[1]).toEqual({
      key: 'ai.spend-cap-usd',
      description: undefined,
      effectiveValue: 25,
    });

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/settings$/);
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer operator-bearer-token');
    expect(init?.credentials).toBe('include');
  });

  it('sends no Authorization header when the credential is null', async () => {
    const fetchFn = mockFetch(() => okJson([]));
    await fetchAdminSettings(null);
    const [, init] = fetchFn.mock.calls[0];
    expect((init?.headers as Record<string, string> | undefined)?.Authorization).toBeUndefined();
    expect(init?.credentials).toBe('include');
  });

  it('drops array entries missing a key or effectiveValue rather than throwing', async () => {
    mockFetch(() => okJson([{ description: 'no key' }, { key: 'ok', effectiveValue: 1 }, 'not-an-object']));
    const result = await fetchAdminSettings('token');
    expect(result.outcome).toBe('available');
    expect(result.settings?.length).toBe(1);
    expect(result.settings?.[0].key).toBe('ok');
  });
});
