// ----------------------------------------------------------------------------
//  OperationsPanel - the Operations JOB tab (sysadmin-console/05, AC-04): the
//  "keep the app running smoothly" surface, composing the two screens that job
//  covers today - Stripe mode (story 04's <StripeModePanel/>, relocated as-is,
//  unchanged) stacked ABOVE the new read-only runtime-settings view
//  (<SettingsPanel/>, dependency-tolerant of `control-plane/01`, not yet built).
//  A future AI-spend snapshot (ADR 0003 Layer 3) joins this same stack rather
//  than minting its own tab.
//
//  SEPARATE ADMIN BUNDLE (from story 01): imports NOTHING from the kid app
//  (pages / signalr / gallery / engine / components); opens no SignalR
//  connection. Styling is theme-driven only (MUI Stack + theme spacing, no
//  hardcoded px/colors).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { Stack } from '@mui/material';
import { StripeModePanel } from './StripeModePanel';
import { SettingsPanel } from './SettingsPanel';

/** Props for {@link OperationsPanel}. */
interface OperationsPanelProps {
  /** The signed-in operator email (from the session check), passed through to both panels. */
  operatorEmail: string;
  /**
   * The operator credential, presented as a bearer on every admin call (the cross-
   * origin path). Null on a same-site deployment, where the cookie carries the session.
   */
  credential: string | null;
}

export function OperationsPanel({ operatorEmail, credential }: OperationsPanelProps) {
  return (
    <Stack spacing={4}>
      <StripeModePanel operatorEmail={operatorEmail} credential={credential} />
      <SettingsPanel operatorEmail={operatorEmail} credential={credential} />
    </Stack>
  );
}
