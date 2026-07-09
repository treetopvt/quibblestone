// ----------------------------------------------------------------------------
//  ActionLogView - the READ-ONLY operator action-log view living in the
//  Operations tab (sysadmin-console/06, issue #233), stacked below
//  <SettingsPanel/>. Its backing endpoint (`GET /api/admin/action-log`) IS
//  already built and Ops-scope-guarded, but this view keeps the SAME
//  dependency-tolerant posture the sibling Operations-tab panels use
//  (mirroring SettingsPanel.tsx's three states):
//
//    - loading:      a CircularProgress while the fetch is in flight.
//    - available:    the endpoint answered with a 2xx `{ rows }` body, so a
//                     read-only table renders (operator, action, target, note,
//                     timestamp) - newest-first, exactly as the server returned
//                     it (this view does NOT re-sort or re-cap the 200-row list).
//    - unavailable:  ANY network failure, non-2xx status, or unparseable body
//                     collapses here - a calm, plain message, NEVER a thrown
//                     error, a blank screen, or a crash of the surrounding shell.
//
//  CHILD-SAFETY / XSS NOTE (AC-07): `target` and `note` are OPERATOR-INFLUENCED
//  free text (an operator can type a note, or target an arbitrary email/tale id).
//  Both are rendered as PLAIN React text children - never via
//  `dangerouslySetInnerHTML` or any other raw-HTML injection - so React's default
//  escaping applies and a value like `<img src=x onerror=alert(1)>` renders as
//  inert literal text, never as markup.
//
//  SEPARATE ADMIN BUNDLE (from story 01): imports NOTHING from the kid app
//  (pages / signalr / gallery / engine / components); opens no SignalR
//  connection. Styling is theme-driven only (palette tokens, MUI spacing scale -
//  never a hex literal or a raw px value). FontAwesome icons only, registered in
//  `./fontawesome.ts`. This is an adult operator tool: minimal and functional,
//  not over-polished.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import {
  Box,
  CircularProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { fetchActionLog, type ActionLogRow } from './actionLogClient';

/** Props for {@link ActionLogView}. */
interface ActionLogViewProps {
  /** The signed-in operator email (from the session check); not shown here, kept for
   *  parity with the other Operations-tab panels and any future audit trail. */
  operatorEmail: string;
  /**
   * The operator credential, presented as a bearer on every admin call (the cross-
   * origin path). Null on a same-site deployment, where the cookie carries the session.
   */
  credential: string | null;
}

/** A calm, readable rendering of an ISO timestamp - never the raw ISO string. */
function formatTimestamp(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString();
}

export function ActionLogView({ credential }: ActionLogViewProps) {
  const theme = useTheme();

  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<ActionLogRow[] | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    const result = await fetchActionLog(credential);
    // Dependency-tolerant: an 'unavailable' outcome simply leaves rows null,
    // which renders the calm fallback message below - never an error state.
    setRows(result.outcome === 'available' ? result.rows ?? [] : null);
    setLoading(false);
  }, [credential]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', width: '100%', px: { xs: 2, md: 3 }, py: { xs: 3, md: 4 } }}>
      <Stack spacing={3}>
        <Typography component="h2" sx={{ fontWeight: 800, fontSize: 24, color: 'text.primary' }}>
          Operator action log
        </Typography>

        {loading && (
          <Stack alignItems="center" sx={{ py: 4 }}>
            <CircularProgress color="primary" />
          </Stack>
        )}

        {!loading && rows === null && (
          <Stack
            direction="row"
            spacing={1.5}
            sx={{
              p: 2,
              borderRadius: '16px',
              bgcolor: alpha(theme.palette.text.secondary, 0.08),
              alignItems: 'center',
            }}
          >
            <Box sx={{ color: 'text.secondary', fontSize: 18, display: 'flex' }}>
              <FontAwesomeIcon icon="clipboard-list" />
            </Box>
            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary' }}>
              The operator action log is not available right now.
            </Typography>
          </Stack>
        )}

        {!loading && rows !== null && rows.length === 0 && (
          <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary' }}>
            No operator actions logged yet.
          </Typography>
        )}

        {!loading && rows !== null && rows.length > 0 && (
          <TableContainer
            sx={{
              borderRadius: '16px',
              bgcolor: 'card.main',
              boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
            }}
          >
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell sx={{ fontWeight: 800 }}>Operator</TableCell>
                  <TableCell sx={{ fontWeight: 800 }}>Action</TableCell>
                  <TableCell sx={{ fontWeight: 800 }}>Target</TableCell>
                  <TableCell sx={{ fontWeight: 800 }}>Note</TableCell>
                  <TableCell sx={{ fontWeight: 800 }}>When</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {rows.map((row, index) => (
                  // No stable id in the contract - the row order is server-fixed
                  // (newest-first) and rows are not edited or removed, so the
                  // positional index is a safe key for this read-only list.
                  <TableRow key={index}>
                    <TableCell sx={{ fontWeight: 700 }}>{row.operatorEmail}</TableCell>
                    <TableCell>{row.action}</TableCell>
                    {/* AC-07: plain text children only - operator free text must never
                        be interpreted as HTML. */}
                    <TableCell>{row.target}</TableCell>
                    <TableCell>{row.note}</TableCell>
                    <TableCell sx={{ color: 'text.secondary', fontSize: 12.5 }}>
                      {formatTimestamp(row.timestampUtc)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </Stack>
    </Box>
  );
}
