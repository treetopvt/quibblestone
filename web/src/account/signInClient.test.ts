// ----------------------------------------------------------------------------
//  signInClient.test.ts - covers the purchaser sign-in web client
//  (accounts-identity/03). The real issue/verify/account-lookup is server-side;
//  this proves the CLIENT contract: it POSTs to the right endpoints, parses the
//  outcome, and FAILS GRACEFULLY (never throws) so the Account surface never
//  shows a raw error (AC-06). It also pins the dev-token pass-through and the
//  no-enumeration-friendly neutral message pass-through (AC-05).
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { requestSignInLink, verifySignIn } from './signInClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, json: () => Promise.resolve(body) } as Response);

describe('requestSignInLink', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the email to the request endpoint and returns the neutral message', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ message: 'If that email has a purchase, a link is on its way.', devToken: null }),
    );

    const result = await requestSignInLink('buyer@example.com');

    expect(result.ok).toBe(true);
    expect(result.message).toMatch(/on its way/);
    expect(result.devToken).toBeUndefined();

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/signin\/request$/);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body)).email).toBe('buyer@example.com');
  });

  it('passes through a dev token when the API echoes one', async () => {
    mockFetch(() => okJson({ message: 'neutral', devToken: 'v1|abc|123|nonce.sig' }));
    const result = await requestSignInLink('buyer@example.com');
    expect(result.devToken).toBe('v1|abc|123|nonce.sig');
  });

  it('resolves a friendly fallback (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await requestSignInLink('buyer@example.com');
    expect(result.ok).toBe(false);
    expect(result.message.length).toBeGreaterThan(0);
  });

  it('resolves ok=false on a non-OK status', async () => {
    mockFetch(() => Promise.resolve({ ok: false, status: 500, json: () => Promise.resolve({}) } as Response));
    const result = await requestSignInLink('buyer@example.com');
    expect(result.ok).toBe(false);
  });
});

describe('verifySignIn', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the token and returns the signed-in outcome with the email', async () => {
    const fetchFn = mockFetch(() =>
      okJson({ outcome: 'signed-in', message: 'You are signed in.', email: 'buyer@example.com', credential: 'PROTECTED' }),
    );

    const result = await verifySignIn('a-token');

    expect(result.outcome).toBe('signed-in');
    expect(result.email).toBe('buyer@example.com');

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/accounts\/signin\/verify$/);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body)).token).toBe('a-token');
  });

  it('returns the no-account outcome (guide to purchase) without an email', async () => {
    mockFetch(() => okJson({ outcome: 'no-account', message: 'No purchase found - buy the family plan.' }));
    const result = await verifySignIn('valid-but-no-account');
    expect(result.outcome).toBe('no-account');
    expect(result.email).toBeUndefined();
  });

  it('returns link-invalid for a bad/expired token', async () => {
    mockFetch(() => okJson({ outcome: 'link-invalid', message: 'That link did not work.' }));
    const result = await verifySignIn('garbage');
    expect(result.outcome).toBe('link-invalid');
  });

  it('maps an unrecognized outcome to error rather than trusting it', async () => {
    mockFetch(() => okJson({ outcome: 'totally-unexpected', message: 'x' }));
    const result = await verifySignIn('a-token');
    expect(result.outcome).toBe('error');
  });

  it('resolves outcome=error (never throws) on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await verifySignIn('a-token');
    expect(result.outcome).toBe('error');
  });
});
