// ----------------------------------------------------------------------------
//  purchasersClient.test.ts - covers the operator grant / revoke web client
//  (sysadmin-console/02, #136). The real account lookup, grant store, and operator
//  authorization are server-side; this proves the CLIENT contract: it GETs the
//  purchaser lookup, POSTs a grant, DELETEs a revoke against the SEPARATE admin
//  endpoints, narrows the responses, and FAILS GRACEFULLY (never throws).
//
//  ANONYMITY (AC-04): every shape here is email + capability keys / leases. There is
//  no player / room / session field on the wire, so none can leak into the client.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { grantEntitlement, lookupPurchaser, revokeEntitlement } from './purchasersClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, json: () => Promise.resolve(body) } as Response);

/** Reads a header off a captured fetch init (headers is a plain object here). */
function header(init: RequestInit | undefined, name: string): string | undefined {
  return (init?.headers as Record<string, string> | undefined)?.[name];
}

const lookupBody = {
  accountExists: true,
  email: 'buyer@example.com',
  grants: [
    {
      capabilityKey: 'library.full',
      label: 'Full Library',
      validThrough: null,
      source: 'Operator',
      active: true,
    },
  ],
};

describe('lookupPurchaser', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('GETs the purchaser endpoint and returns the account + grants', async () => {
    const fetchFn = mockFetch(() => okJson(lookupBody));

    const result = await lookupPurchaser('buyer@example.com');

    expect(fetchFn).toHaveBeenCalledOnce();
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/purchasers/buyer%40example.com');
    expect(init?.method).toBe('GET');
    expect(init?.credentials).toBe('include');
    expect(result.ok).toBe(true);
    expect(result.purchaser?.accountExists).toBe(true);
    expect(result.purchaser?.grants[0]?.capabilityKey).toBe('library.full');
    expect(result.purchaser?.grants[0]?.source).toBe('Operator');
  });

  it('attaches the operator credential as a bearer when the shell holds one (cross-origin path)', async () => {
    const fetchFn = mockFetch(() => okJson(lookupBody));
    await lookupPurchaser('buyer@example.com', 'PROTECTED-CRED');
    const [, init] = fetchFn.mock.calls[0];
    expect(header(init, 'Authorization')).toBe('Bearer PROTECTED-CRED');
    expect(init?.credentials).toBe('include');
  });

  it('surfaces the clear not-found state (accountExists false) without erroring', async () => {
    mockFetch(() =>
      okJson({ accountExists: false, email: 'nobody@example.com', grants: [] }),
    );

    const result = await lookupPurchaser('nobody@example.com');

    expect(result.ok).toBe(true);
    expect(result.purchaser?.accountExists).toBe(false);
    expect(result.purchaser?.grants).toEqual([]);
  });

  it('fails gracefully on a non-OK status (never throws)', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401 } as Response));

    const result = await lookupPurchaser('buyer@example.com');

    expect(result.ok).toBe(false);
    expect(result.purchaser).toBeNull();
    expect(result.message.length).toBeGreaterThan(0);
  });
});

describe('grantEntitlement', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the capability key + validThrough and returns the refreshed view', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ purchaser: lookupBody, message: 'Granted Full Library to buyer@example.com.' }),
    );

    const result = await grantEntitlement('buyer@example.com', 'library.full', null);

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/purchasers/buyer%40example.com/entitlements');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ capabilityKey: 'library.full', validThrough: null });
    expect(result.ok).toBe(true);
    expect(result.purchaser?.grants[0]?.capabilityKey).toBe('library.full');
    expect(result.message).toContain('Granted');
  });

  it('attaches the operator credential as a bearer alongside the JSON content type', async () => {
    const fetchFn = mockFetch(() => okJson({ purchaser: lookupBody, message: 'ok' }));
    await grantEntitlement('buyer@example.com', 'library.full', null, 'PROTECTED-CRED');
    const [, init] = fetchFn.mock.calls[0];
    expect(header(init, 'Authorization')).toBe('Bearer PROTECTED-CRED');
    expect(header(init, 'Content-Type')).toBe('application/json');
    expect(init?.credentials).toBe('include');
  });
});

describe('revokeEntitlement', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('DELETEs the capability key path and returns the refreshed view', async () => {
    const lapsed = {
      ...lookupBody,
      grants: [{ ...lookupBody.grants[0], active: false }],
    };
    const fetchFn = mockFetch(() =>
      okJson({ purchaser: lapsed, message: 'Revoked Full Library for buyer@example.com.' }),
    );

    const result = await revokeEntitlement('buyer@example.com', 'library.full');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toContain('/api/admin/purchasers/buyer%40example.com/entitlements/library.full');
    expect(init?.method).toBe('DELETE');
    expect(result.ok).toBe(true);
    expect(result.purchaser?.grants[0]?.active).toBe(false);
    expect(result.message).toContain('Revoked');
  });
});
