// ----------------------------------------------------------------------------
//  SettingsPanel - the read-only runtime-settings view living in the Operations
//  tab (sysadmin-console/05, AC-04). Its backing endpoint (`control-plane/01`)
//  has NOT been built yet as of this story, so this panel is DELIBERATELY
//  dependency-tolerant, mirroring the exact fail-safe pattern main.tsx's own
//  session-check already uses ("falls back to <AdminLogin/> - the fail-safe
//  default"): it attempts a fetch on mount and holds three states -
//
//    - loading:      a CircularProgress while the fetch is in flight.
//    - available:    the backing endpoint answered with a 2xx array body, so a
//                     minimal read-only list of key / effectiveValue / description
//                     renders (NOT an editor - this story ships no settings-
//                     editing UI; that stays parked, per feature.md AC-07).
//    - unavailable:  ANY network failure, non-2xx status, or unparseable body
//                     collapses here - a calm, plain "not wired up yet" message,
//                     NEVER a thrown error, a blank screen, or a crash of the
//                     surrounding shell.
//
//  SEPARATE ADMIN BUNDLE (from story 01): imports NOTHING from the kid app
//  (pages / signalr / gallery / engine / components); opens no SignalR
//  connection. COLORS are theme-driven only (palette tokens - text.secondary,
//  card.main, stoneEdge - never a hex literal); glyph / spacing SIZING uses the
//  same raw fontSize / borderRadius / boxShadow house style the sibling admin
//  panels use (StripeModePanel, PurchaserEntitlements), kept consistent across the
//  bundle rather than reinvented per file. FontAwesome icons only.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, CircularProgress, Stack, Typography } from '@mui/material';
import { fetchAdminSettings, type AdminSettingView } from './settingsClient';

/** Props for {@link SettingsPanel}. */
interface SettingsPanelProps {
  /** The signed-in operator email (from the session check); not shown here, kept for
   *  parity with the other Operations-tab panels and any future audit trail. */
  operatorEmail: string;
  /**
   * The operator credential, presented as a bearer on every admin call (the cross-
   * origin path). Null on a same-site deployment, where the cookie carries the session.
   */
  credential: string | null;
}

/** Renders one setting's effective value as a short, readable string - never `[object Object]`. */
function formatValue(value: unknown): string {
  if (value === null) return 'null';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

export function SettingsPanel({ credential }: SettingsPanelProps) {
  const theme = useTheme();

  const [loading, setLoading] = useState(true);
  const [settings, setSettings] = useState<AdminSettingView[] | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    const result = await fetchAdminSettings(credential);
    // Dependency-tolerant: an 'unavailable' outcome simply leaves settings null,
    // which renders the calm fallback message below - never an error state.
    setSettings(result.outcome === 'available' ? result.settings ?? [] : null);
    setLoading(false);
  }, [credential]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <Box sx={{ maxWidth: 620, mx: 'auto', width: '100%', px: { xs: 2, md: 3 }, py: { xs: 3, md: 4 } }}>
      <Stack spacing={3}>
        <Typography component="h2" sx={{ fontWeight: 800, fontSize: 24, color: 'text.primary' }}>
          Runtime settings
        </Typography>

        {loading && (
          <Stack alignItems="center" sx={{ py: 4 }}>
            <CircularProgress color="primary" />
          </Stack>
        )}

        {!loading && settings === null && (
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
              <FontAwesomeIcon icon="gear" />
            </Box>
            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary' }}>
              Runtime settings are not wired up yet.
            </Typography>
          </Stack>
        )}

        {!loading && settings !== null && settings.length === 0 && (
          <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary' }}>
            No settings are configured yet.
          </Typography>
        )}

        {!loading && settings !== null && settings.length > 0 && (
          <Stack spacing={1.5}>
            {settings.map((setting) => (
              <Box
                key={setting.key}
                sx={{
                  p: 2,
                  borderRadius: '16px',
                  bgcolor: 'card.main',
                  boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
                }}
              >
                <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>{setting.key}</Typography>
                {setting.description && (
                  <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.secondary', mt: 0.25 }}>
                    {setting.description}
                  </Typography>
                )}
                <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.primary', mt: 0.5 }}>
                  {formatValue(setting.effectiveValue)}
                </Typography>
              </Box>
            ))}
          </Stack>
        )}
      </Stack>
    </Box>
  );
}
