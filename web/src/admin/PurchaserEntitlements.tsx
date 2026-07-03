// ----------------------------------------------------------------------------
//  PurchaserEntitlements - the OPERATOR grant / revoke of a purchaser entitlement
//  by email (sysadmin-console/02, issue #136). The post-login back-office screen
//  that unsticks a paying customer whose entitlement did not apply, WITHOUT hand-
//  editing Table Storage: an operator searches a purchaser by email, sees their
//  account state + current grants, and grants or revokes a capability key. Every
//  write lands as the SAME lease-shaped grant (source = Operator) the session-
//  creation gate reads (see purchasersClient / AdminEntitlementsController).
//
//  SEPARATE ADMIN BUNDLE / NO KID-APP EDGE (AC-05, from story 01): this file lives
//  in the admin bundle and imports NOTHING from the kid app (pages / signalr /
//  gallery / engine / components). It opens NO SignalR connection. It shares only the
//  MUI theme (via main.tsx's ThemeProvider) and its own FontAwesome registration.
//
//  ANONYMITY FIREWALL (AC-04, non-negotiable): this screen speaks SOLELY in purchaser
//  email + capability keys / leases. It never shows or requests a player nickname,
//  room code, or session, and there is no control that navigates from a purchaser to
//  any gameplay data - the join ADR 0002 forbids is simply not reachable here.
//
//  Styling: theme tokens ONLY (no hex / raw-px literals); FontAwesome icons only; big
//  tap targets. The search + grant forms use react-hook-form with controlled MUI
//  inputs. TS strict (no any - the client narrows unknown). Adult-facing and minimal,
//  but the ONE visual language (theme-driven, not a bespoke design system).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import {
  Box,
  Button,
  Chip,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import {
  GRANTABLE_CAPABILITIES,
  PACK_PREFIX,
  grantEntitlement,
  lookupPurchaser,
  revokeEntitlement,
  type PurchaserGrant,
  type PurchaserLookup,
} from './purchasersClient';

/** Props for {@link PurchaserEntitlements}. */
interface PurchaserEntitlementsProps {
  /** The signed-in operator email (from the session check), shown in the header. */
  operatorEmail: string;
}

/** The email-search form shape. */
interface SearchForm {
  email: string;
}

/** The grant form shape: a fixed catalog key (or the pack sentinel), a pack id, and an optional expiry date. */
interface GrantForm {
  /** A fixed capability key, or PACK_SENTINEL to enter an add-on pack id. */
  capabilityChoice: string;
  /** The add-on pack id (used only when capabilityChoice is the pack sentinel). */
  packId: string;
  /** The lease end as a yyyy-mm-dd date, or '' for "no expiry" (a one-time-pack-shaped grant). */
  validThrough: string;
}

/** A basic, forgiving email shape check (client-side friendliness only - the server is authoritative). */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** The select sentinel for "enter an add-on pack id" (reveals the pack id field). */
const PACK_SENTINEL = '__pack__';

/** Formats a lease end for display: an ISO instant to a plain local date, or "no expiry" when null. */
function formatValidThrough(validThrough: string | null): string {
  if (validThrough === null) return 'No expiry';
  const parsed = new Date(validThrough);
  if (Number.isNaN(parsed.getTime())) return validThrough;
  return `Until ${parsed.toLocaleDateString()}`;
}

/** Props for {@link GrantRow}. */
interface GrantRowProps {
  grant: PurchaserGrant;
  /** True while a revoke for this grant is in flight (button disabled). */
  pending: boolean;
  onRevoke: (capabilityKey: string) => void;
}

/** Renders one capability lease (key + label + source + lease) and its revoke control. */
function GrantRow({ grant, pending, onRevoke }: GrantRowProps) {
  const theme = useTheme();
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={2}
      alignItems={{ xs: 'stretch', sm: 'center' }}
      justifyContent="space-between"
      sx={{
        p: 2.5,
        borderRadius: '18px',
        bgcolor: 'sandstone.main',
      }}
    >
      <Stack spacing={0.75}>
        <Stack direction="row" spacing={1.5} alignItems="center">
          <Typography sx={{ fontWeight: 800, fontSize: 15.5, color: 'text.primary' }}>
            {grant.label}
          </Typography>
          <Chip
            size="small"
            label={grant.active ? 'Active' : 'Lapsed'}
            sx={{
              fontWeight: 800,
              color: grant.active ? 'teal.main' : 'text.secondary',
              bgcolor: grant.active
                ? alpha(theme.palette.teal.main, 0.16)
                : alpha(theme.palette.stoneEdge.main, 0.16),
            }}
          />
        </Stack>
        <Typography sx={{ fontWeight: 600, fontSize: 12.5, color: 'text.secondary' }}>
          {grant.capabilityKey} - {grant.source} - {formatValidThrough(grant.validThrough)}
        </Typography>
      </Stack>
      <Button
        variant="outlined"
        color="error"
        disabled={pending}
        onClick={() => onRevoke(grant.capabilityKey)}
        startIcon={<FontAwesomeIcon icon="ban" style={{ width: 16, height: 16 }} />}
        sx={{ flexShrink: 0 }}
      >
        Revoke
      </Button>
    </Stack>
  );
}

export function PurchaserEntitlements({ operatorEmail }: PurchaserEntitlementsProps) {
  const theme = useTheme();

  // The current purchaser view (null until a search resolves) and the email it is for.
  const [lookup, setLookup] = useState<PurchaserLookup | null>(null);
  const [searchedEmail, setSearchedEmail] = useState<string>('');
  // A friendly message (a failure fallback, or the server's grant / revoke echo).
  const [message, setMessage] = useState<string>('');
  // The capability key whose revoke is currently in flight.
  const [pendingKey, setPendingKey] = useState<string | null>(null);

  const search = useForm<SearchForm>({ defaultValues: { email: '' }, mode: 'onChange' });
  const grant = useForm<GrantForm>({
    defaultValues: { capabilityChoice: GRANTABLE_CAPABILITIES[0].key, packId: '', validThrough: '' },
    mode: 'onChange',
  });

  const searchEmail = search.watch('email');
  const canSearch = !search.formState.isSubmitting && EMAIL_PATTERN.test(searchEmail.trim());
  const capabilityChoice = grant.watch('capabilityChoice');
  const packId = grant.watch('packId');
  const isPack = capabilityChoice === PACK_SENTINEL;

  const onSearch = search.handleSubmit(async (values) => {
    const email = values.email.trim();
    const result = await lookupPurchaser(email);
    setSearchedEmail(email);
    setMessage(result.ok ? '' : result.message);
    setLookup(result.purchaser);
  });

  const onGrant = grant.handleSubmit(async (values) => {
    if (!searchedEmail) return;
    const capabilityKey = isPack ? `${PACK_PREFIX}${values.packId.trim()}` : values.capabilityChoice;
    // An ISO instant for the day the operator picked, or null for "no expiry".
    const validThrough = values.validThrough ? new Date(values.validThrough).toISOString() : null;
    const result = await grantEntitlement(searchedEmail, capabilityKey, validThrough);
    setMessage(result.message);
    if (result.purchaser) setLookup(result.purchaser);
  });

  const handleRevoke = async (capabilityKey: string) => {
    if (!searchedEmail || pendingKey) return;
    setPendingKey(capabilityKey);
    try {
      const result = await revokeEntitlement(searchedEmail, capabilityKey);
      setMessage(result.message);
      if (result.purchaser) setLookup(result.purchaser);
    } finally {
      setPendingKey(null);
    }
  };

  // The grant control is usable once a search has resolved (so an email is in hand). A
  // grant to an email with no account CREATES it (the controller's create-or-get) - that
  // is exactly the unstick case, so it is deliberately NOT gated on accountExists. A pack
  // choice needs a non-empty id.
  const canGrant =
    !grant.formState.isSubmitting &&
    searchedEmail.length > 0 &&
    (!isPack || packId.trim().length > 0);

  return (
    <Box sx={{ minHeight: '100dvh', maxWidth: 560, mx: 'auto' }}>
      <Stack spacing={4} sx={{ px: { xs: 3, sm: 5 }, pt: 6, pb: 6 }}>
        {/* Header: reads as the operator entitlement console, not the kid app. */}
        <Stack spacing={1.5} alignItems="center" sx={{ textAlign: 'center' }}>
          <Box
            aria-hidden
            sx={{
              width: 56,
              height: 56,
              borderRadius: '50%',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              bgcolor: alpha(theme.palette.primary.main, 0.12),
              color: 'primary.main',
              fontSize: 22,
            }}
          >
            <FontAwesomeIcon icon="key" />
          </Box>
          <Typography sx={{ fontWeight: 800, fontSize: 20, color: 'text.primary' }}>
            Purchaser entitlements
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary' }}>
            Signed in as {operatorEmail}
          </Typography>
        </Stack>

        {/* Email search: look a purchaser up by email (AC-01). */}
        <Box component="form" onSubmit={onSearch} noValidate>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems="flex-start">
            <Controller
              name="email"
              control={search.control}
              rules={{ pattern: EMAIL_PATTERN }}
              render={({ field }) => (
                <TextField
                  {...field}
                  type="email"
                  fullWidth
                  label="Purchaser email"
                  placeholder="buyer@example.com"
                  autoComplete="off"
                  inputMode="email"
                />
              )}
            />
            <Button
              type="submit"
              variant="contained"
              disabled={!canSearch}
              startIcon={<FontAwesomeIcon icon="magnifying-glass" style={{ width: 16, height: 16 }} />}
              sx={{ flexShrink: 0, minHeight: 56 }}
            >
              Look up
            </Button>
          </Stack>
        </Box>

        {message && (
          <Typography
            role="status"
            sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}
          >
            {message}
          </Typography>
        )}

        {/* The clear "no account found" state (AC-01). */}
        {lookup && !lookup.accountExists && (
          <Stack spacing={2} alignItems="center" sx={{ py: 4, textAlign: 'center' }}>
            <Box aria-hidden sx={{ color: 'gold.main', fontSize: 30 }}>
              <FontAwesomeIcon icon="triangle-exclamation" />
            </Box>
            <Typography sx={{ fontWeight: 800, fontSize: 16.5, color: 'text.primary' }}>
              No account found for this email
            </Typography>
            <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary', maxWidth: 340 }}>
              No purchaser account exists for {lookup.email}. Granting a capability below will create
              the account and attach the grant.
            </Typography>
          </Stack>
        )}

        {/* The purchaser's current grants (AC-01). */}
        {lookup?.accountExists && (
          <Stack spacing={2}>
            <Typography sx={{ fontWeight: 800, fontSize: 14, color: 'text.primary' }}>
              {lookup.email}
            </Typography>
            {lookup.grants.length === 0 ? (
              <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary' }}>
                This purchaser holds no entitlements yet.
              </Typography>
            ) : (
              <Stack spacing={1.5}>
                {lookup.grants.map((g) => (
                  <GrantRow
                    key={g.capabilityKey}
                    grant={g}
                    pending={pendingKey === g.capabilityKey}
                    onRevoke={(key) => void handleRevoke(key)}
                  />
                ))}
              </Stack>
            )}
          </Stack>
        )}

        {/* The grant control: offer the fixed catalog keys (plus the open-ended pack family). */}
        {lookup && (
          <Box
            component="form"
            onSubmit={onGrant}
            noValidate
            sx={{
              p: 3,
              borderRadius: '20px',
              bgcolor: 'card.main',
              boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
            }}
          >
            <Stack spacing={2.5}>
              <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>
                Grant a capability
              </Typography>
              <Controller
                name="capabilityChoice"
                control={grant.control}
                render={({ field }) => (
                  <TextField {...field} select fullWidth label="Capability">
                    {GRANTABLE_CAPABILITIES.map((cap) => (
                      <MenuItem key={cap.key} value={cap.key}>
                        {cap.label} ({cap.key})
                      </MenuItem>
                    ))}
                    <MenuItem value={PACK_SENTINEL}>Add-on pack...</MenuItem>
                  </TextField>
                )}
              />
              {isPack && (
                <Controller
                  name="packId"
                  control={grant.control}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      fullWidth
                      label="Pack id"
                      placeholder="spooky"
                      helperText={`The grant key will be ${PACK_PREFIX}${(packId || '<id>').trim()}`}
                    />
                  )}
                />
              )}
              <Controller
                name="validThrough"
                control={grant.control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    type="date"
                    fullWidth
                    label="Valid through"
                    slotProps={{ inputLabel: { shrink: true } }}
                    helperText="Leave blank for no expiry (a one-time pack)."
                  />
                )}
              />
              <Button
                type="submit"
                variant="contained"
                fullWidth
                disabled={!canGrant}
                startIcon={<FontAwesomeIcon icon="plus" style={{ width: 16, height: 16 }} />}
              >
                {grant.formState.isSubmitting ? 'Granting...' : 'Grant capability'}
              </Button>
            </Stack>
          </Box>
        )}
      </Stack>
    </Box>
  );
}
