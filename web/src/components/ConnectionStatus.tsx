// ----------------------------------------------------------------------------
//  ConnectionStatus - presentational readout of the real-time connection.
//
//  Shows whether the SignalR connection to the API is live and, once a Ping
//  round trip completes, the server's echo response. Purely presentational: it
//  takes status + echo as props (App owns the connection via useGameHub).
// ----------------------------------------------------------------------------

import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Chip, Stack, Typography } from '@mui/material';
import type { ConnectionStatus as Status } from '../signalr/useGameHub';

interface ConnectionStatusProps {
  status: Status;
  echo: string | null;
}

type StatusMeta = {
  label: string;
  color: 'success' | 'warning' | 'default';
  icon: 'circle-check' | 'plug' | 'circle-xmark';
};

const STATUS_META: Record<Status, StatusMeta> = {
  connected: { label: 'Connected', color: 'success', icon: 'circle-check' },
  connecting: { label: 'Connecting...', color: 'warning', icon: 'plug' },
  disconnected: { label: 'Disconnected', color: 'default', icon: 'circle-xmark' },
};

export function ConnectionStatus({ status, echo }: ConnectionStatusProps) {
  const meta = STATUS_META[status];

  return (
    <Stack spacing={2} alignItems="center">
      <Chip
        color={meta.color}
        icon={<FontAwesomeIcon icon={meta.icon} />}
        label={meta.label}
        sx={{ px: 1, fontWeight: 700 }}
      />
      {echo && (
        <Typography variant="body1">
          Server says: <strong>{echo}</strong>
        </Typography>
      )}
    </Stack>
  );
}
