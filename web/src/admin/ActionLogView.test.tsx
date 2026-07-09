// @vitest-environment jsdom
// ----------------------------------------------------------------------------
//  ActionLogView.test.tsx - covers the operator-console action-log view
//  (sysadmin-console/06, issue #233). The KEY assertion here is AC-07: operator-
//  supplied free text (`target` / `note`) must render as LITERAL text, never as
//  interpreted HTML - proving the view never uses `dangerouslySetInnerHTML` or
//  any other raw-HTML injection path. Also covers the loading / unavailable /
//  empty states and the newest-first, no-re-sort row ordering.
//
//  This is the first component-rendering spec in the admin bundle (the rest of
//  the suite tests pure functions per vitest.config.ts's house style), so it
//  opts into jsdom locally via the `@vitest-environment jsdom` pragma above
//  rather than flipping the whole (otherwise DOM-free, `node`-environment) suite.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { ThemeProvider } from '@mui/material/styles';
import { theme } from '../theme';
import { ActionLogView } from './ActionLogView';
import type { ActionLogRow } from './actionLogClient';

function renderView() {
  return render(
    <ThemeProvider theme={theme}>
      <ActionLogView operatorEmail="ops@quibblestone.com" credential="token" />
    </ThemeProvider>,
  );
}

function mockFetchOnce(body: unknown, ok = true) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve({
        ok,
        status: ok ? 200 : 500,
        json: () => Promise.resolve(body),
      } as Response),
    ),
  );
}

describe('ActionLogView', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('renders operator free text as literal text, never as HTML markup (AC-07)', async () => {
    const rows: ActionLogRow[] = [
      {
        operatorEmail: 'ops@quibblestone.com',
        action: 'tale.takedown',
        target: '<img src=x onerror=alert(1)>',
        note: '<b>bold</b>',
        timestampUtc: '2026-07-09T12:34:56+00:00',
      },
    ];
    mockFetchOnce({ rows });

    renderView();

    // The literal, escaped strings must appear as visible text...
    await waitFor(() => {
      expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeTruthy();
      expect(screen.getByText('<b>bold</b>')).toBeTruthy();
    });

    // ...and must NEVER have been interpreted as real markup: no actual <img> or
    // <b> element was created from the note/target text.
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('b')).toBeNull();
  });

  it('shows a loading state before the fetch resolves', () => {
    let resolveFetch: ((value: Response) => void) | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn(
        () =>
          new Promise<Response>((resolve) => {
            resolveFetch = resolve;
          }),
      ),
    );

    renderView();
    expect(screen.getByRole('progressbar')).toBeTruthy();

    // Resolve so the pending promise does not leak into the next test.
    resolveFetch?.({ ok: true, status: 200, json: () => Promise.resolve({ rows: [] }) } as Response);
  });

  it('shows the empty-state message when the log has no rows', async () => {
    mockFetchOnce({ rows: [] });
    renderView();
    await waitFor(() => {
      expect(screen.getByText('No operator actions logged yet.')).toBeTruthy();
    });
  });

  it('shows the unavailable message on a non-2xx response, without crashing', async () => {
    mockFetchOnce({}, false);
    renderView();
    await waitFor(() => {
      expect(screen.getByText('The operator action log is not available right now.')).toBeTruthy();
    });
  });

  it('renders rows newest-first exactly as returned, without re-sorting', async () => {
    const rows: ActionLogRow[] = [
      {
        operatorEmail: 'ops@quibblestone.com',
        action: 'entitlement.grant',
        target: 'buyer@example.com',
        note: 'library.full',
        timestampUtc: '2026-07-09T12:00:00+00:00',
      },
      {
        operatorEmail: 'ops@quibblestone.com',
        action: 'entitlement.revoke',
        target: 'buyer@example.com',
        note: '',
        timestampUtc: '2026-07-08T09:00:00+00:00',
      },
    ];
    mockFetchOnce({ rows });

    renderView();

    await waitFor(() => {
      expect(screen.getByText('entitlement.grant')).toBeTruthy();
    });

    const actionCells = screen.getAllByText(/entitlement\.(grant|revoke)/);
    expect(actionCells.map((el) => el.textContent)).toEqual(['entitlement.grant', 'entitlement.revoke']);
  });
});
