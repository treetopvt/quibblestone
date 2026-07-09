// ----------------------------------------------------------------------------
//  vaultId.test.ts - covers the durable device vault-id mint/read for
//  keepsake-vault/01 (issue #196, AC-01). Proves the bearer-credential entropy
//  rules: mint/read is idempotent (a second call returns the SAME stored id), the
//  only local mint path is crypto.randomUUID(), and when that is unavailable the
//  client asks the server (POST /api/vault/mint) rather than generating a weak
//  Math.random-based id. Runs in the default `node` env (no jsdom dependency): we
//  stub localStorage + crypto + fetch ourselves.
// ----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getVaultId, VAULT_ID_STORAGE_KEY } from './vaultId';

// A tiny in-memory localStorage stand-in (the node test env has no DOM). Only the
// methods this module uses (getItem / setItem / clear) are implemented.
function fakeLocalStorage(): Storage {
  const map = new Map<string, string>();
  return {
    getItem: (k: string) => (map.has(k) ? map.get(k)! : null),
    setItem: (k: string, v: string) => void map.set(k, v),
    removeItem: (k: string) => void map.delete(k),
    clear: () => map.clear(),
    key: (i: number) => Array.from(map.keys())[i] ?? null,
    get length() {
      return map.size;
    },
  } as Storage;
}

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, json: () => Promise.resolve(body) } as Response);

describe('getVaultId', () => {
  beforeEach(() => vi.stubGlobal('localStorage', fakeLocalStorage()));
  afterEach(() => vi.unstubAllGlobals());

  it('mints with crypto.randomUUID and persists it durably', async () => {
    const uuid = '11111111-1111-4111-8111-111111111111';
    vi.stubGlobal('crypto', { randomUUID: () => uuid });

    const id = await getVaultId();

    expect(id).toBe(uuid);
    expect(localStorage.getItem(VAULT_ID_STORAGE_KEY)).toBe(uuid);
  });

  it('is idempotent: a second call returns the SAME stored id', async () => {
    let calls = 0;
    vi.stubGlobal('crypto', {
      randomUUID: () => `id-${(calls += 1)}-1111-4111-8111-111111111111`,
    });

    const first = await getVaultId();
    const second = await getVaultId();

    expect(second).toBe(first);
    expect(calls).toBe(1); // the second read never re-mints
  });

  it('never uses a Math.random fallback: with no crypto.randomUUID it asks the server', async () => {
    // crypto present but WITHOUT randomUUID (an insecure origin / old engine).
    vi.stubGlobal('crypto', {});
    const serverId = '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF';
    const fetchFn = mockFetch(() => okJson({ vaultId: serverId }));

    const id = await getVaultId();

    expect(id).toBe(serverId);
    // It hit the mint endpoint with a POST - never generated an id locally.
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/mint$/);
    expect(init?.method).toBe('POST');
    expect(localStorage.getItem(VAULT_ID_STORAGE_KEY)).toBe(serverId);
  });

  it('returns null (skips the save) when there is no crypto AND the server mint fails', async () => {
    vi.stubGlobal('crypto', {});
    mockFetch(() => Promise.reject(new Error('offline')));

    const id = await getVaultId();

    expect(id).toBeNull();
    expect(localStorage.getItem(VAULT_ID_STORAGE_KEY)).toBeNull();
  });
});
