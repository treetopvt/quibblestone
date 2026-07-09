// ----------------------------------------------------------------------------
//  vaultClient.test.ts - covers the web vault client for keepsake-vault/01 (issue
//  #196). The real vault (re-vet, tale-id minting, storage, TTL, cap) is server-
//  side; this proves the CLIENT contract:
//    - AC-02 HEADER: the vault id rides the X-Vault-Id HEADER, never a URL path.
//    - AC-02 SERVER STAMP: the body carries no createdUtc (the server stamps it).
//    - AC-02 FIRE-AND-FORGET: a rejected / slow POST never throws and never blocks
//      the caller (autoSaveTaleToVault always resolves, even offline).
//    - graceful failure: save/list resolve false/null (never throw) on any fault.
//  Runs in the default `node` env (no jsdom dependency): we stub localStorage +
//  fetch ourselves (localStorage backs the getVaultId the auto-save resolves).
// ----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { autoSaveTaleToVault, listVaultTales, saveTaleToVault } from './vaultClient';
import { VAULT_ID_STORAGE_KEY } from './vaultId';

// A tiny in-memory localStorage stand-in (the node test env has no DOM).
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

const VAULT_ID = '11111111-1111-4111-8111-111111111111';

const cleanInput = {
  title: 'A tale',
  parts: [
    { isWord: false, text: 'Once ' },
    { isWord: true, text: 'banana' },
  ],
  bylineNames: 'Sam & Mia',
};

describe('saveTaleToVault', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs to /api/vault/tales with the vault id in the X-Vault-Id header (not the URL)', async () => {
    const fetchFn = mockFetch(() => Promise.resolve({ ok: true } as Response));

    const ok = await saveTaleToVault(VAULT_ID, cleanInput);

    expect(ok).toBe(true);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/tales$/);
    expect(url).not.toContain(VAULT_ID); // the id is NEVER in the URL (bearer secret)
    expect(init?.method).toBe('POST');
    const headers = init?.headers as Record<string, string>;
    expect(headers['X-Vault-Id']).toBe(VAULT_ID);
  });

  it('sends no createdUtc field (the server stamps it, AC-02)', async () => {
    const fetchFn = mockFetch(() => Promise.resolve({ ok: true } as Response));
    await saveTaleToVault(VAULT_ID, cleanInput);
    const sent = JSON.parse(String(fetchFn.mock.calls[0][1]?.body));
    expect(sent).not.toHaveProperty('createdUtc');
    expect(sent.title).toBe('A tale');
    expect(sent.parts).toHaveLength(2);
    expect(sent.bylineNames).toBe('Sam & Mia');
  });

  it('resolves false on a non-OK status (e.g. the 409 vault-full)', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 409 } as Response));
    expect(await saveTaleToVault(VAULT_ID, cleanInput)).toBe(false);
  });

  it('resolves false (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(saveTaleToVault(VAULT_ID, cleanInput)).resolves.toBe(false);
  });

  it('resolves false for an empty vault id without calling fetch', async () => {
    const fetchFn = mockFetch(() => Promise.resolve({ ok: true } as Response));
    expect(await saveTaleToVault('', cleanInput)).toBe(false);
    expect(fetchFn).not.toHaveBeenCalled();
  });
});

describe('autoSaveTaleToVault (fire-and-forget)', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', fakeLocalStorage());
    localStorage.setItem(VAULT_ID_STORAGE_KEY, VAULT_ID); // a vault id already exists
  });
  afterEach(() => vi.unstubAllGlobals());

  it('fires the save but the caller need not await it (a slow POST never blocks)', async () => {
    // A never-resolving fetch stands in for a slow network. The caller pattern is
    // `void autoSaveTaleToVault(...)` - it does NOT await. We capture the promise
    // but deliberately do not await it, flush microtasks so the internal
    // getVaultId + fetch dispatch runs, and prove the POST fired while still
    // in flight (forget). A caller that awaited would hang - exactly why the reveal
    // uses `void`.
    let resolveFetch: ((r: Response) => void) | undefined;
    const fetchFn = mockFetch(
      () => new Promise<Response>((resolve) => { resolveFetch = resolve; }),
    );

    const pending = autoSaveTaleToVault(cleanInput); // NOT awaited (fire-and-forget)
    await Promise.resolve();
    await Promise.resolve();

    expect(fetchFn).toHaveBeenCalledTimes(1); // the POST was issued...
    expect(resolveFetch).toBeDefined();        // ...and is still in flight

    // Settle it to clean up; the background promise resolves without throwing.
    resolveFetch?.({ ok: true } as Response);
    await expect(pending).resolves.toBeUndefined();
  });

  it('never throws when the network fails outright', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(autoSaveTaleToVault(cleanInput)).resolves.toBeUndefined();
  });
});

describe('listVaultTales', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs with the X-Vault-Id header and parses the tales', async () => {
    const fetchFn = mockFetch(() =>
      Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            tales: [
              {
                taleId: 'T1',
                title: 'A tale',
                parts: [{ isWord: false, text: 'Once ' }],
                bylineNames: 'Sam',
                createdUtc: '2026-07-09T00:00:00Z',
              },
            ],
          }),
      } as Response),
    );

    const tales = await listVaultTales(VAULT_ID);

    expect(tales).toHaveLength(1);
    expect(tales?.[0].taleId).toBe('T1');
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/tales$/);
    expect(init?.method).toBe('GET');
    expect((init?.headers as Record<string, string>)['X-Vault-Id']).toBe(VAULT_ID);
  });

  it('resolves null (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(listVaultTales(VAULT_ID)).resolves.toBeNull();
  });

  it('resolves null for an empty vault id without calling fetch', async () => {
    const fetchFn = mockFetch(() => Promise.resolve({ ok: true } as Response));
    expect(await listVaultTales('')).toBeNull();
    expect(fetchFn).not.toHaveBeenCalled();
  });
});
