// ----------------------------------------------------------------------------
//  GetMore - the gated-purchase paywall (billing-entitlements/04, issue #73). The
//  ONE place a purchaser can buy the family plan or an add-on pack. Reachable ONLY
//  from a purchaser-facing Home entry link (AC-05) - never inside the join / lobby /
//  word-entry / reveal flow a child uses. Free play is never narrowed (AC-03); this
//  is purely additive.
//
//  NO DARK PATTERNS (AC-04): plain pricing/value copy, a clear gold CTA, and an
//  always-available back-out (the shared AppBar back action) - no countdown timers,
//  no "X people just bought this", no pre-checked upsells, no guilt.
//
//  ACCOUNT-FREE START (AC-06): tapping "Get it" starts a Stripe Checkout Session and
//  redirects; the lightweight purchaser account is created by the webhook on
//  completion (billing-03), never a forced "sign up first" step here.
//
//  CONFIG-OFF: when billing is not configured, the client reports not-enabled and
//  this screen shows a warm "not available yet" state - never an error, and free play
//  is untouched. UNLOCK-ON-NEXT-SESSION: a purchase reflects on the NEXT room/solo
//  session created, not mid-round (billing-01 AC-03 / story 04 AC-02) - so there is
//  deliberately no live "you're unlocked!" mutation of an open game here.
//
//  Styling: theme tokens only (web/src/theme.ts); FontAwesome icons only. Big tap
//  targets, one visual language with the rest of the app.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, CircularProgress, Stack, Typography } from '@mui/material';
import { AppBar } from '../components';
import { fetchProducts, startCheckout, type BillingProduct } from '../billing/billingClient';

export interface GetMoreProps {
  /** Return to Home (the shared app-bar back action - the friction-free back-out, AC-04). */
  onBack: () => void;
}

export function GetMore({ onBack }: GetMoreProps) {
  const theme = useTheme();
  const [searchParams] = useSearchParams();
  const purchaseState = searchParams.get('purchase'); // "success" | "cancel" | null

  const [loading, setLoading] = useState(true);
  const [enabled, setEnabled] = useState(false);
  const [products, setProducts] = useState<BillingProduct[]>([]);
  const [busyProductId, setBusyProductId] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    void fetchProducts().then((result) => {
      if (!active) return;
      setEnabled(result.enabled);
      setProducts(result.products);
      setLoading(false);
    });
    return () => {
      active = false;
    };
  }, []);

  const onGet = async (product: BillingProduct) => {
    setBusyProductId(product.productId);
    setNote(null);
    const result = await startCheckout(product.productId);
    if (result.url) {
      // Redirect to Stripe's hosted checkout (AC-06: account created on completion).
      window.location.href = result.url;
      return;
    }
    // Not enabled / not purchasable - a friendly note, never an error.
    setNote(result.message ?? 'That is not available just now - free play is always on.');
    setBusyProductId(null);
  };

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar title="Get more" leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }} />

      <Stack spacing={4} sx={{ px: 5.5, pt: 3, pb: 6 }}>
        <Stack spacing={1.5} sx={{ textAlign: 'center' }}>
          <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>
            More ways to play
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary' }}>
            Everything you play today stays free. These just add more.
          </Typography>
        </Stack>

        {/* Returned-from-checkout states (AC-02: the unlock shows on your NEXT game). */}
        {purchaseState === 'success' && (
          <ReturnBanner
            tint={theme.palette.teal.main}
            icon="circle-check"
            text="Thank you! Your unlock will be ready the next time you start a game."
          />
        )}
        {purchaseState === 'cancel' && (
          <ReturnBanner
            tint={theme.palette.gold.main}
            icon="circle-info"
            text="No worries - nothing was purchased. Free play is always on."
          />
        )}

        {loading && (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
            <CircularProgress />
          </Box>
        )}

        {!loading && !enabled && (
          <Box
            sx={{
              p: 4,
              borderRadius: '24px',
              bgcolor: 'card.main',
              textAlign: 'center',
            }}
          >
            <Typography sx={{ fontWeight: 700, fontSize: 14.5, color: 'text.secondary' }}>
              Purchases are not available just now - but everything in QuibbleStone is free to play.
            </Typography>
          </Box>
        )}

        {!loading &&
          enabled &&
          products.map((product) => (
            <Box
              key={product.productId}
              sx={{
                p: 4,
                borderRadius: '24px',
                bgcolor: 'card.main',
                boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
              }}
            >
              <Stack spacing={2}>
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Box
                    aria-hidden
                    sx={{
                      width: 44,
                      height: 44,
                      borderRadius: '50%',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      bgcolor: alpha(theme.palette.gold.main, 0.16),
                      color: 'gold.main',
                      fontSize: 20,
                    }}
                  >
                    <FontAwesomeIcon icon={product.mode === 'subscription' ? 'crown' : 'gift'} />
                  </Box>
                  <Box sx={{ flex: 1 }}>
                    <Typography sx={{ fontWeight: 800, fontSize: 16, color: 'text.primary' }}>
                      {product.displayName}
                    </Typography>
                    <Typography sx={{ fontWeight: 700, fontSize: 12, color: 'text.secondary' }}>
                      {product.mode === 'subscription' ? 'Subscription' : 'One-time'}
                    </Typography>
                  </Box>
                </Stack>
                <Typography sx={{ fontWeight: 600, fontSize: 14, color: 'text.secondary' }}>
                  {product.description}
                </Typography>
                <Button
                  variant="contained"
                  color="secondary"
                  fullWidth
                  disabled={!product.purchasable || busyProductId !== null}
                  onClick={() => void onGet(product)}
                  startIcon={
                    busyProductId === product.productId ? undefined : (
                      <FontAwesomeIcon icon="unlock" style={{ width: 16, height: 16 }} />
                    )
                  }
                >
                  {busyProductId === product.productId ? 'Starting...' : product.purchasable ? 'Get it' : 'Coming soon'}
                </Button>
              </Stack>
            </Box>
          ))}

        {note && (
          <Typography role="status" sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
            {note}
          </Typography>
        )}
      </Stack>
    </Box>
  );
}

/** A small returned-from-checkout banner (success / cancel), theme-tinted. */
function ReturnBanner({ tint, icon, text }: { tint: string; icon: 'circle-check' | 'circle-info'; text: string }) {
  return (
    <Stack
      direction="row"
      spacing={1.5}
      alignItems="center"
      sx={{ p: 2.5, borderRadius: '18px', bgcolor: alpha(tint, 0.12) }}
    >
      <Box sx={{ color: tint, fontSize: 20, display: 'flex' }}>
        <FontAwesomeIcon icon={icon} />
      </Box>
      <Typography sx={{ fontWeight: 700, fontSize: 13.5, color: 'text.primary' }}>{text}</Typography>
    </Stack>
  );
}
