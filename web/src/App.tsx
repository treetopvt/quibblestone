// ----------------------------------------------------------------------------
//  App - placeholder landing page for the QuibbleStone walking skeleton.
//
//  This is intentionally NOT a game. Its only job is to prove the web client
//  can reach the API in real time: it opens the SignalR connection (useGameHub),
//  fires a single Ping once connected, and shows the connection status plus the
//  server's echo. Real screens (lobby, word entry, reveal - README section 10)
//  replace this later.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Box, Container, Stack, Typography } from '@mui/material';
import { useGameHub } from './signalr/useGameHub';
import { ConnectionStatus } from './components/ConnectionStatus';

export default function App() {
  const { status, ping } = useGameHub();
  const [echo, setEcho] = useState<string | null>(null);

  // Fire one Ping as soon as we are connected, to prove the round trip.
  useEffect(() => {
    if (status !== 'connected') return;

    let active = true;
    void ping('hello from the web client').then((response) => {
      if (active && response) setEcho(response);
    });

    return () => {
      active = false;
    };
  }, [status, ping]);

  return (
    <Container maxWidth="sm">
      <Stack spacing={4} alignItems="center" sx={{ py: 8, textAlign: 'center' }}>
        <Box sx={{ fontSize: 64, color: 'secondary.main' }}>
          <FontAwesomeIcon icon="bolt" />
        </Box>

        <Typography variant="h3" component="h1" fontWeight={800}>
          QuibbleStone
        </Typography>

        <Typography variant="body1" color="text.secondary">
          Walking skeleton. No game here yet - just proving the web app, the API,
          and the real-time hub all talk to each other.
        </Typography>

        <ConnectionStatus status={status} echo={echo} />
      </Stack>
    </Container>
  );
}
