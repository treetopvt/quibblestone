// ----------------------------------------------------------------------------
//  actionLogClient.test.ts - covers the operator-console action-log web client
//  (sysadmin-console/06, issue #233). Proves the DEPENDENCY-TOLERANCE contract:
//  a network failure, any non-2xx status, or an unparseable/unexpected-shape body
//  all collapse to 'unavailable' - never a thrown error - while a genuine 2xx
//  `{ rows: [...] }` response parses into the row list, defensively narrowed.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchActionLog } from './actionLogClient';

function mockFetch(impl: (url: string, init?: RequestInit) => Promise<Response>) {
  const fn = vi.fn(impl);
  vi.stubGlobal('fetch', fn);
  return fn;
}

const okJson = (body: unknown): Promise<Response> =>
  Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);

const statusOnly = (status: number): Promise<Response> =>
  Promise.resolve({ ok: false, status, json: () => Promise.resolve({}) } as Response);

describe('fetchActionLog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('resolves unavailable on a network failure', async () => {
    mockFetch(() => Promise.reject(new Error('offline')));
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 404', async () => {
    mockFetch(() => statusOnly(404));
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 500', async () => {
    mockFetch(() => statusOnly(500));
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 200 with a body missing rows', async () => {
    mockFetch(() => okJson({}));
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable on a 200 whose rows field is not an array', async () => {
    mockFetch(() => okJson({ rows: 'not-an-array' }));
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves unavailable when the 2xx body cannot be parsed as JSON', async () => {
    mockFetch(() =>
      Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.reject(new SyntaxError('Unexpected end of JSON input')),
      } as Response),
    );
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('unavailable');
  });

  it('resolves available with parsed rows on a 200 { rows } body', async () => {
    const fetchFn = mockFetch(() =>
      okJson({
        rows: [
          {
            operatorEmail: 'ops@quibblestone.com',
            action: 'entitlement.grant',
            target: 'buyer@example.com',
            note: 'library.full',
            timestampUtc: '2026-07-09T12:34:56+00:00',
          },
        ],
      }),
    );

    const result = await fetchActionLog('operator-bearer-token');

    expect(result.outcome).toBe('available');
    expect(result.rows).toEqual([
      {
        operatorEmail: 'ops@quibblestone.com',
        action: 'entitlement.grant',
        target: 'buyer@example.com',
        note: 'library.full',
        timestampUtc: '2026-07-09T12:34:56+00:00',
      },
    ]);

    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toMatch(/\/api\/admin\/action-log$/);
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer operator-bearer-token');
    expect(init?.credentials).toBe('include');
  });

  it('sends no Authorization header when the credential is null', async () => {
    const fetchFn = mockFetch(() => okJson({ rows: [] }));
    await fetchActionLog(null);
    const [, init] = fetchFn.mock.calls[0];
    expect((init?.headers as Record<string, string> | undefined)?.Authorization).toBeUndefined();
    expect(init?.credentials).toBe('include');
  });

  it('allows an empty note (per the contract) but drops rows missing other required fields', async () => {
    mockFetch(() =>
      okJson({
        rows: [
          {
            operatorEmail: 'ops@quibblestone.com',
            action: 'settings.put',
            target: 'ai.spend-cap-usd',
            note: '',
            timestampUtc: '2026-07-09T00:00:00+00:00',
          },
          { operatorEmail: 'ops@quibblestone.com', action: 'settings.put' },
          'not-an-object',
        ],
      }),
    );
    const result = await fetchActionLog('token');
    expect(result.outcome).toBe('available');
    expect(result.rows?.length).toBe(1);
    expect(result.rows?.[0].note).toBe('');
  });
});
