// ----------------------------------------------------------------------------
//  vaultClaimClient.test.ts - covers the web client for keepsake-vault/03's
//  claim + recovery surface (issue #230). The real claim/redeem logic, the
//  claim-code generator, and the anti-brute-force controls are server-side;
//  this proves the CLIENT contract:
//    - AC-02 HEADER: the vault id rides the X-Vault-Id HEADER, never a URL path.
//    - AC-01/AC-02: claim sends the family credential as `Authorization: Bearer`.
//    - AC-02 BODY: redeem carries the code in the request BODY, never a URL.
//    - AC-06: redeem resolves this device's OWN vault id via getVaultId (minting
//      one if absent) rather than trusting a caller-supplied id.
//    - graceful failure: every call resolves null/false (never throws) on any
//      non-OK status or thrown fetch error.
//  Runs in the default `node` env (no jsdom dependency): we stub localStorage +
//  fetch ourselves (localStorage backs the getVaultId that redeemClaimCode
//  resolves). Mirrors vaultClient.test.ts / cloudGalleryClient.test.ts.
// ----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  claimVault,
  getVaultClaim,
  redeemClaimCode,
  regenerateClaimCode,
} from './vaultClaimClient';
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

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);

const status = (code: number): Promise<Response> =>
  Promise.resolve({ ok: code >= 200 && code < 300, status: code, json: () => Promise.resolve({}) } as Response);

const VAULT_ID = '11111111-1111-4111-8111-111111111111';
const CRED = 'FAMILY-CREDENTIAL';
const CODE_VIEW = { claimCode: 'K5Q-2NX-8CP', claimCodeExpiresUtc: '2026-07-16T00:00:00Z' };

describe('getVaultClaim', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs with the X-Vault-Id header and parses an unclaimed vault', async () => {
    const fetchFn = mockFetch(() => okJson({ claimed: false, code: null }));
    const result = await getVaultClaim(VAULT_ID);

    expect(result).toEqual({ claimed: false, code: null });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/claim$/);
    expect(url).not.toContain(VAULT_ID);
    expect(init?.method).toBe('GET');
    expect((init?.headers as Record<string, string>)['X-Vault-Id']).toBe(VAULT_ID);
  });

  it('parses a claimed vault with its live code', async () => {
    mockFetch(() => okJson({ claimed: true, code: CODE_VIEW }));
    const result = await getVaultClaim(VAULT_ID);
    expect(result).toEqual({ claimed: true, code: CODE_VIEW });
  });

  it('resolves null when claimed is true but the code shape is malformed', async () => {
    // A claimed vault MUST carry a well-formed code; a malformed one is an
    // unparseable body (resolves null), never { claimed: true, code: null } which
    // would leave the UI with neither a claim CTA nor a code.
    mockFetch(() => okJson({ claimed: true, code: { claimCode: 'K5Q-2NX-8CP' } }));
    await expect(getVaultClaim(VAULT_ID)).resolves.toBeNull();
  });

  it('resolves null (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(getVaultClaim(VAULT_ID)).resolves.toBeNull();
  });

  it('resolves null on a non-OK status', async () => {
    mockFetch(() => status(500));
    await expect(getVaultClaim(VAULT_ID)).resolves.toBeNull();
  });

  it('resolves null for an empty vault id without calling fetch', async () => {
    const fetchFn = mockFetch(() => okJson({ claimed: false, code: null }));
    expect(await getVaultClaim('')).toBeNull();
    expect(fetchFn).not.toHaveBeenCalled();
  });
});

describe('claimVault', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs with the vault id header and the family credential as Authorization: Bearer', async () => {
    const fetchFn = mockFetch(() => okJson(CODE_VIEW));
    const result = await claimVault(VAULT_ID, CRED);

    expect(result).toEqual(CODE_VIEW);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/claim$/);
    expect(url).not.toContain(VAULT_ID);
    expect(init?.method).toBe('POST');
    const headers = init?.headers as Record<string, string>;
    expect(headers['X-Vault-Id']).toBe(VAULT_ID);
    expect(headers.Authorization).toBe(`Bearer ${CRED}`);
  });

  it('resolves null (never throws) on a 401 (not signed in / no account)', async () => {
    mockFetch(() => status(401));
    await expect(claimVault(VAULT_ID, CRED)).resolves.toBeNull();
  });

  it('resolves null on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(claimVault(VAULT_ID, CRED)).resolves.toBeNull();
  });

  it('resolves null without calling fetch when the vault id or credential is empty', async () => {
    const fetchFn = mockFetch(() => okJson(CODE_VIEW));
    expect(await claimVault('', CRED)).toBeNull();
    expect(await claimVault(VAULT_ID, '')).toBeNull();
    expect(fetchFn).not.toHaveBeenCalled();
  });
});

describe('regenerateClaimCode', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs with the vault id header and returns the fresh code', async () => {
    const fetchFn = mockFetch(() => okJson(CODE_VIEW));
    const result = await regenerateClaimCode(VAULT_ID);

    expect(result).toEqual(CODE_VIEW);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/claim-code\/regenerate$/);
    expect(url).not.toContain(VAULT_ID);
    expect(init?.method).toBe('POST');
    expect((init?.headers as Record<string, string>)['X-Vault-Id']).toBe(VAULT_ID);
  });

  it('resolves null (never throws) on a 404 (vault never claimed)', async () => {
    mockFetch(() => status(404));
    await expect(regenerateClaimCode(VAULT_ID)).resolves.toBeNull();
  });

  it('resolves null on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(regenerateClaimCode(VAULT_ID)).resolves.toBeNull();
  });
});

describe('redeemClaimCode', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', fakeLocalStorage());
    localStorage.setItem(VAULT_ID_STORAGE_KEY, VAULT_ID); // this device already has a vault id
  });
  afterEach(() => vi.unstubAllGlobals());

  it('sends the code in the BODY (never the URL) with this device\'s own vault id in the header', async () => {
    const fetchFn = mockFetch(() => okJson({ redeemed: true }));
    const result = await redeemClaimCode('K5Q-2NX-8CP');

    expect(result).toBe(true);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/vault\/claim-code\/redeem$/);
    expect(url).not.toContain('K5Q-2NX-8CP');
    expect(init?.method).toBe('POST');
    expect((init?.headers as Record<string, string>)['X-Vault-Id']).toBe(VAULT_ID);
    const sent = JSON.parse(String(init?.body));
    expect(sent).toEqual({ code: 'K5Q-2NX-8CP' });
  });

  it('resolves false when the server reports the code not redeemed', async () => {
    mockFetch(() => okJson({ redeemed: false }));
    expect(await redeemClaimCode('WRONG-CODE')).toBe(false);
  });

  it('resolves false (never throws) on a non-OK status', async () => {
    mockFetch(() => status(429));
    expect(await redeemClaimCode('K5Q-2NX-8CP')).toBe(false);
  });

  it('resolves false (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    await expect(redeemClaimCode('K5Q-2NX-8CP')).resolves.toBe(false);
  });

  it('mints a vault id when this device has none yet, so the redeem still has a target', async () => {
    localStorage.clear();
    vi.stubGlobal('crypto', { randomUUID: () => 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' });
    const fetchFn = mockFetch(() => okJson({ redeemed: true }));

    const result = await redeemClaimCode('K5Q-2NX-8CP');

    expect(result).toBe(true);
    const [, init] = fetchFn.mock.calls[0];
    expect((init?.headers as Record<string, string>)['X-Vault-Id']).toBe(
      'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    );
  });
});
