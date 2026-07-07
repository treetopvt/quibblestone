// ----------------------------------------------------------------------------
//  operatorClient.test.ts - covers the operator back-office login web client
//  (sysadmin-console/01, #135). The real issue/verify/allowlist is server-side;
//  this proves the CLIENT contract: it POSTs to the SEPARATE admin endpoints
//  (/api/admin/login/*), parses the outcome, and FAILS GRACEFULLY (never throws)
//  so the login screen never shows a raw error. It also pins the dev-token
//  pass-through and the neutral, no-enumeration message pass-through (AC-02), and
//  that the 'not-authorized' outcome (a valid link for a non-operator) is surfaced
//  distinctly.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { getOperatorSession, requestOperatorLink, verifyOperatorLink } from './operatorClient';

/** Reads the Authorization header off a captured fetch init (headers is a plain object here). */
function authHeader(init?: RequestInit): string | undefined {
  return (init?.headers as Record<string, string> | undefined)?.Authorization;
}

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, json: () => Promise.resolve(body) } as Response);

describe('requestOperatorLink', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the email to the admin request endpoint and returns the neutral message', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ message: 'If that email is an operator, a link is on its way.', devToken: null }),
    );

    const result = await requestOperatorLink('ops@quibblestone.com');

    expect(result.ok).toBe(true);
    expect(result.message).toMatch(/on its way/);
    expect(result.devToken).toBeUndefined();

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/login\/request$/);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body)).email).toBe('ops@quibblestone.com');
  });

  it('passes through a dev token when the API echoes one', async () => {
    mockFetch(() => okJson({ message: 'neutral', devToken: 'v1|abc|123|nonce.sig' }));
    const result = await requestOperatorLink('ops@quibblestone.com');
    expect(result.devToken).toBe('v1|abc|123|nonce.sig');
  });

  it('resolves a friendly fallback (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await requestOperatorLink('ops@quibblestone.com');
    expect(result.ok).toBe(false);
    expect(result.message.length).toBeGreaterThan(0);
  });

  it('resolves ok=false on a non-OK status', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 500, json: () => Promise.resolve({}) } as Response));
    const result = await requestOperatorLink('ops@quibblestone.com');
    expect(result.ok).toBe(false);
  });
});

describe('verifyOperatorLink', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the token and returns the signed-in outcome with the operator email', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ outcome: 'signed-in', message: 'You are signed in.', email: 'ops@quibblestone.com', credential: 'PROTECTED' }),
    );

    const result = await verifyOperatorLink('a-token');

    expect(result.outcome).toBe('signed-in');
    expect(result.email).toBe('ops@quibblestone.com');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/login\/verify$/);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body)).token).toBe('a-token');
  });

  it('surfaces the operator credential on signed-in (the shell holds it as a bearer)', async () => {
    mockFetch(() =>
      okJson({ outcome: 'signed-in', message: 'ok', email: 'ops@quibblestone.com', credential: 'PROTECTED-CRED' }),
    );
    const result = await verifyOperatorLink('a-token');
    expect(result.outcome).toBe('signed-in');
    expect(result.credential).toBe('PROTECTED-CRED');
  });

  it('surfaces not-authorized for a valid link whose email is not an operator', async () => {
    mockFetch(() => okJson({ outcome: 'not-authorized', message: 'That email is not authorized.' }));
    const result = await verifyOperatorLink('valid-but-not-operator');
    expect(result.outcome).toBe('not-authorized');
    expect(result.email).toBeUndefined();
  });

  it('returns link-invalid for a bad/expired token', async () => {
    mockFetch(() => okJson({ outcome: 'link-invalid', message: 'That link did not work.' }));
    const result = await verifyOperatorLink('garbage');
    expect(result.outcome).toBe('link-invalid');
  });

  it('maps an unrecognized outcome to error rather than trusting it', async () => {
    mockFetch(() => okJson({ outcome: 'totally-unexpected', message: 'x' }));
    const result = await verifyOperatorLink('a-token');
    expect(result.outcome).toBe('error');
  });

  it('resolves outcome=error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await verifyOperatorLink('a-token');
    expect(result.outcome).toBe('error');
  });
});

describe('getOperatorSession', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('presents the operator credential as a bearer when the shell holds one (cross-origin path)', async () => {
    const fetchFn = mockFetch(() => okJson({ email: 'ops@quibblestone.com' }));

    const result = await getOperatorSession('PROTECTED-CRED');

    expect(result.signedIn).toBe(true);
    expect(result.email).toBe('ops@quibblestone.com');
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/session$/);
    expect(init?.method).toBe('GET');
    expect(init?.credentials).toBe('include');
    expect(authHeader(init)).toBe('Bearer PROTECTED-CRED');
  });

  it('sends no Authorization header when no credential is held (same-site cookie path)', async () => {
    const fetchFn = mockFetch(() => okJson({ email: 'ops@quibblestone.com' }));
    await getOperatorSession();
    const [, init] = fetchFn.mock.calls[0];
    expect(authHeader(init)).toBeUndefined();
    expect(init?.credentials).toBe('include');
  });

  it('resolves signedIn=false (never throws) on a 401', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 401, json: () => Promise.resolve({}) } as Response));
    const result = await getOperatorSession('cred');
    expect(result.signedIn).toBe(false);
  });
});
