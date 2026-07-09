// ----------------------------------------------------------------------------
//  SupportLookup - the Support job's real payload (sysadmin-console/07, issue #243,
//  ADR 0003 Layer 3): find an ACCOUNT by whatever the person in front of you can give
//  you (a purchaser email or an AccountId), see its account-plane picture in one place -
//  the account, its grants, its subscription state, an aggregate vault/tale COUNT, and a
//  linked-device COUNT - and run the support verbs: resend a magic link, comp/extend an
//  entitlement, resync a subscription, plus the two tale-targeted content verbs (extend a
//  public tale's link TTL, restore a user's own deleted keepsake).
//
//  THE CROSS-PLANE FIREWALL, ON THE UI (AC-08): the search box accepts an email or an
//  AccountId ONLY - never a claim code or a public-tale slug (a bridge input simply
//  resolves to "no account", it never finds an owner). No section EVER renders a tale
//  byline, a tale timestamp, or a list of a family's individual tales - the vault/tale
//  and linked-device figures are COUNTS only, and each is dependency-tolerant (it renders
//  "not available yet" when its backing seam is not wired). The two tale-targeted verbs
//  (extend TTL, restore keepsake) take a slug / (vaultId, taleId) as a DIRECT input to
//  their OWN action, in their own cards - they never act as a search key into the account
//  lookup, so the UI never implies a slug "finds" an account.
//
//  REUSE, NOT A SECOND WRITE PATH (AC-06): the comp/extend-entitlement control reuses
//  story 02's EXACT grant plumbing (purchasersClient.grantEntitlement / revokeEntitlement,
//  the SAME AdminEntitlementsController write the session-creation gate reads). This screen
//  adds the narrower lookup + the additional verbs alongside it; story 02's grant/revoke
//  behavior is unchanged.
//
//  Styling: theme tokens ONLY (no hex / raw-px literals); FontAwesome icons only; big tap
//  targets. TS strict (no any - the clients narrow unknown). Separate admin bundle: imports
//  NOTHING from the kid app, opens NO SignalR connection.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import type { IconProp } from '@fortawesome/fontawesome-svg-core';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Chip, MenuItem, Stack, TextField, Typography } from '@mui/material';
import {
  GRANTABLE_CAPABILITIES,
  PACK_PREFIX,
  grantEntitlement,
  revokeEntitlement,
  type PurchaserGrant,
} from './purchasersClient';
import {
  extendTaleTtl,
  lookupAccount,
  resendMagicLink,
  restoreKeepsake,
  resyncSubscription,
  type SupportAccountSummary,
  type SupportCountSection,
} from './supportClient';

/** Props for {@link SupportLookup}. */
interface SupportLookupProps {
  /** The signed-in operator email (from the session check), shown in the header. */
  operatorEmail: string;
  /**
   * The operator credential, presented as a bearer on every admin call (the cross-origin
   * path). Null on a same-site deployment, where the cookie carries the session.
   */
  credential: string | null;
}

/** The account-search form shape (an email or an AccountId - never a slug / claim code). */
interface SearchForm {
  query: string;
}

/** The grant form shape (AC-06): a fixed catalog key (or the pack sentinel), a pack id, and an optional expiry. */
interface GrantForm {
  capabilityChoice: string;
  packId: string;
  validThrough: string;
}

/** The extend-TTL form shape (AC-04): a public tale slug, a DIRECT content input. */
interface ExtendForm {
  slug: string;
}

/** The restore-keepsake form shape (AC-05): the DIRECT (vaultId, taleId) content identifiers. */
interface RestoreForm {
  vaultId: string;
  taleId: string;
}

/** A forgiving email shape check (client-side friendliness only - the server is authoritative). */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
/** A GUID shape (an AccountId) - the second accepted search input. */
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** The select sentinel for "enter an add-on pack id" (reveals the pack id field). */
const PACK_SENTINEL = '__pack__';

/** Formats a lease / expiry ISO instant to a plain local date, or a friendly fallback when null. */
function formatDate(iso: string | null, fallback: string): string {
  if (iso === null) return fallback;
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? iso : parsed.toLocaleDateString();
}

/** A soft card wrapper for a detail section, using theme tokens only. */
function SectionCard({ children }: { children: React.ReactNode }) {
  const theme = useTheme();
  return (
    <Box
      sx={{
        p: 3,
        borderRadius: '20px',
        bgcolor: 'card.main',
        boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
      }}
    >
      {children}
    </Box>
  );
}

/** A section heading with a leading icon (FontAwesome only). */
function SectionHeading({ icon, label }: { icon: IconProp; label: string }) {
  return (
    <Stack direction="row" spacing={1.5} alignItems="center">
      <Box aria-hidden sx={{ color: 'primary.main', fontSize: 18 }}>
        <FontAwesomeIcon icon={icon} />
      </Box>
      <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>{label}</Typography>
    </Stack>
  );
}

/** Renders a dependency-tolerant COUNT figure, or the "not available yet" placeholder. */
function CountFigure({ icon, label, section }: { icon: IconProp; label: string; section: SupportCountSection }) {
  return (
    <Stack
      direction="row"
      spacing={2}
      alignItems="center"
      justifyContent="space-between"
      sx={{ p: 2, borderRadius: '16px', bgcolor: 'sandstone.main' }}
    >
      <Stack direction="row" spacing={1.5} alignItems="center">
        <Box aria-hidden sx={{ color: 'text.secondary', fontSize: 16 }}>
          <FontAwesomeIcon icon={icon} />
        </Box>
        <Typography sx={{ fontWeight: 700, fontSize: 13.5, color: 'text.primary' }}>{label}</Typography>
      </Stack>
      {section.available ? (
        <Typography sx={{ fontWeight: 800, fontSize: 16, color: 'text.primary' }}>{section.count}</Typography>
      ) : (
        <Typography sx={{ fontWeight: 600, fontSize: 12.5, color: 'text.secondary' }}>Not available yet</Typography>
      )}
    </Stack>
  );
}

/** Renders one capability lease (label + source + lease) with its revoke control (AC-06). */
function GrantRow({
  grant,
  pending,
  onRevoke,
}: {
  grant: PurchaserGrant;
  pending: boolean;
  onRevoke: (capabilityKey: string) => void;
}) {
  const theme = useTheme();
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={2}
      alignItems={{ xs: 'stretch', sm: 'center' }}
      justifyContent="space-between"
      sx={{ p: 2.5, borderRadius: '18px', bgcolor: 'sandstone.main' }}
    >
      <Stack spacing={0.75}>
        <Stack direction="row" spacing={1.5} alignItems="center">
          <Typography sx={{ fontWeight: 800, fontSize: 15.5, color: 'text.primary' }}>{grant.label}</Typography>
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
          {grant.capabilityKey} - {grant.source} - {formatDate(grant.validThrough, 'No expiry')}
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

export function SupportLookup({ operatorEmail, credential }: SupportLookupProps) {
  const theme = useTheme();

  // The resolved account summary (null until a search resolves) and the query it is for.
  const [summary, setSummary] = useState<SupportAccountSummary | null>(null);
  // A friendly status message (a failure fallback, or a verb's server echo).
  const [message, setMessage] = useState<string>('');
  // The capability key whose revoke is currently in flight.
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  // Which account-scoped verb is in flight (so its button shows progress).
  const [busyVerb, setBusyVerb] = useState<'resend' | 'resync' | null>(null);

  const search = useForm<SearchForm>({ defaultValues: { query: '' }, mode: 'onChange' });
  const grant = useForm<GrantForm>({
    defaultValues: { capabilityChoice: GRANTABLE_CAPABILITIES[0].key, packId: '', validThrough: '' },
    mode: 'onChange',
  });
  const extend = useForm<ExtendForm>({ defaultValues: { slug: '' }, mode: 'onChange' });
  const restore = useForm<RestoreForm>({ defaultValues: { vaultId: '', taleId: '' }, mode: 'onChange' });

  const query = search.watch('query').trim();
  const canSearch = !search.formState.isSubmitting && (EMAIL_PATTERN.test(query) || GUID_PATTERN.test(query));
  const capabilityChoice = grant.watch('capabilityChoice');
  const packId = grant.watch('packId');
  const isPack = capabilityChoice === PACK_SENTINEL;

  // Re-fetch the account summary after a write so grants / subscription refresh. Uses the
  // resolved email (or the account id) so it re-resolves the SAME account.
  const refresh = async (account: SupportAccountSummary) => {
    const key = account.accountId ?? account.email;
    const result = await lookupAccount(key, credential);
    if (result.summary) setSummary(result.summary);
  };

  const onSearch = search.handleSubmit(async (values) => {
    const result = await lookupAccount(values.query.trim(), credential);
    setMessage(result.ok ? '' : result.message);
    setSummary(result.summary);
  });

  const onGrant = grant.handleSubmit(async (values) => {
    if (!summary) return;
    const capabilityKey = isPack ? `${PACK_PREFIX}${values.packId.trim()}` : values.capabilityChoice;
    // Pin the lease end to the END of the chosen day (UTC) so "valid through" stays active for
    // all of it (EntitlementGrant.IsActiveAt treats the lease end as exclusive).
    const validThrough = values.validThrough
      ? new Date(`${values.validThrough}T23:59:59.999Z`).toISOString()
      : null;
    const result = await grantEntitlement(summary.email, capabilityKey, validThrough, credential);
    setMessage(result.message);
    if (result.purchaser) await refresh(summary);
  });

  const handleRevoke = async (capabilityKey: string) => {
    if (!summary || pendingKey) return;
    setPendingKey(capabilityKey);
    try {
      const result = await revokeEntitlement(summary.email, capabilityKey, credential);
      setMessage(result.message);
      if (result.purchaser) await refresh(summary);
    } finally {
      setPendingKey(null);
    }
  };

  const handleResend = async () => {
    if (!summary || busyVerb) return;
    setBusyVerb('resend');
    try {
      const result = await resendMagicLink(summary.email, credential);
      setMessage(result.message);
    } finally {
      setBusyVerb(null);
    }
  };

  const handleResync = async () => {
    if (!summary?.accountId || busyVerb) return;
    setBusyVerb('resync');
    try {
      const result = await resyncSubscription(summary.accountId, credential);
      setMessage(result.message);
      await refresh(summary);
    } finally {
      setBusyVerb(null);
    }
  };

  const onExtend = extend.handleSubmit(async (values) => {
    const result = await extendTaleTtl(values.slug.trim(), credential);
    setMessage(result.message);
    if (result.ok) extend.reset({ slug: '' });
  });

  const onRestore = restore.handleSubmit(async (values) => {
    const result = await restoreKeepsake(values.vaultId.trim(), values.taleId.trim(), credential);
    setMessage(result.message);
    if (result.ok) restore.reset({ vaultId: '', taleId: '' });
  });

  const canGrant =
    !grant.formState.isSubmitting && summary !== null && (!isPack || packId.trim().length > 0);
  const extendSlug = extend.watch('slug').trim();
  const restoreVault = restore.watch('vaultId').trim();
  const restoreTale = restore.watch('taleId').trim();
  // A grant / comp is offered whenever the resolved-or-searched identity is an EMAIL: for a resolved
  // account, and ALSO for an email that has no account yet - granting there CREATES the account (the
  // unstick case, story 02's create-or-get). A not-found AccountId search offers no grant control (an
  // account cannot be created from an id).
  const grantableEmail = summary !== null && EMAIL_PATTERN.test(summary.email);

  // The comp/extend control (AC-06), reused in the resolved-account and not-found-email states so the
  // "granting creates the account" copy is always truthful. Reuses story 02's grant plumbing verbatim.
  const grantForm = (
    <Box component="form" onSubmit={onGrant} noValidate>
      <Stack spacing={2}>
        <Controller
          name="capabilityChoice"
          control={grant.control}
          render={({ field }) => (
            <TextField {...field} select fullWidth label="Comp or extend a capability">
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
  );

  return (
    <Box sx={{ minHeight: '100dvh', maxWidth: 620, mx: 'auto' }}>
      <Stack spacing={4} sx={{ px: { xs: 3, sm: 5 }, pt: 6, pb: 8 }}>
        {/* Header: reads as the operator Support console. */}
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
            <FontAwesomeIcon icon="user" />
          </Box>
          <Typography sx={{ fontWeight: 800, fontSize: 20, color: 'text.primary' }}>Support lookup</Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary' }}>
            Signed in as {operatorEmail}
          </Typography>
        </Stack>

        {/* Account search: by email OR AccountId (never a claim code or slug). */}
        <Box component="form" onSubmit={onSearch} noValidate>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems="flex-start">
            <Controller
              name="query"
              control={search.control}
              render={({ field }) => (
                <TextField
                  {...field}
                  fullWidth
                  label="Purchaser email or account id"
                  placeholder="buyer@example.com"
                  autoComplete="off"
                  helperText="A claim code or a tale link is never a search - use the tools below for those."
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

        {/* The clear "no account found" state (AC-01). For an EMAIL search, the grant control is
            offered here too - granting creates the account (the unstick case). For an AccountId
            search there is nothing to create, so no grant control is shown. */}
        {summary && !summary.accountExists && (
          <Stack spacing={3}>
            <Stack spacing={2} alignItems="center" sx={{ py: 3, textAlign: 'center' }}>
              <Box aria-hidden sx={{ color: 'gold.main', fontSize: 30 }}>
                <FontAwesomeIcon icon="triangle-exclamation" />
              </Box>
              <Typography sx={{ fontWeight: 800, fontSize: 16.5, color: 'text.primary' }}>
                No account found
              </Typography>
              <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary', maxWidth: 360 }}>
                {grantableEmail
                  ? `No account exists for ${summary.email}. Granting a capability below will create the account and attach the grant.`
                  : 'No account exists for that id.'}
              </Typography>
            </Stack>
            {grantableEmail && (
              <SectionCard>
                <Stack spacing={2}>
                  <SectionHeading icon="key" label="Comp an entitlement" />
                  {grantForm}
                </Stack>
              </SectionCard>
            )}
          </Stack>
        )}

        {/* The resolved account detail (AC-02). */}
        {summary?.accountExists && (
          <Stack spacing={3}>
            {/* Account facts. */}
            <SectionCard>
              <Stack spacing={1}>
                <SectionHeading icon="user" label="Account" />
                <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>{summary.email}</Typography>
                {summary.accountId && (
                  <Typography sx={{ fontWeight: 600, fontSize: 12, color: 'text.secondary', wordBreak: 'break-all' }}>
                    {summary.accountId}
                  </Typography>
                )}
                <Typography sx={{ fontWeight: 600, fontSize: 12.5, color: 'text.secondary' }}>
                  Created {formatDate(summary.createdUtc, 'unknown')}
                </Typography>
              </Stack>
            </SectionCard>

            {/* Subscription state (AC-02). */}
            <SectionCard>
              <Stack spacing={1.5}>
                <SectionHeading icon="credit-card" label="Subscription" />
                {summary.subscription.hasSubscription ? (
                  <Stack spacing={0.5}>
                    <Stack direction="row" spacing={1.5} alignItems="center">
                      <Typography sx={{ fontWeight: 700, fontSize: 14, color: 'text.primary' }}>
                        {summary.subscription.plan ?? 'Subscription'}
                      </Typography>
                      <Chip
                        size="small"
                        label={summary.subscription.status === 'active' ? 'Active' : 'Lapsed'}
                        sx={{
                          fontWeight: 800,
                          color: summary.subscription.status === 'active' ? 'teal.main' : 'text.secondary',
                          bgcolor:
                            summary.subscription.status === 'active'
                              ? alpha(theme.palette.teal.main, 0.16)
                              : alpha(theme.palette.stoneEdge.main, 0.16),
                        }}
                      />
                    </Stack>
                    <Typography sx={{ fontWeight: 600, fontSize: 12.5, color: 'text.secondary' }}>
                      Renews / ends {formatDate(summary.subscription.validThrough, 'no expiry')}
                      {summary.subscription.mode ? ` - ${summary.subscription.mode} mode` : ''}
                    </Typography>
                    {summary.subscription.stripeSubscriptionId && (
                      <Typography sx={{ fontWeight: 600, fontSize: 11.5, color: 'text.secondary', wordBreak: 'break-all' }}>
                        {summary.subscription.stripeSubscriptionId}
                      </Typography>
                    )}
                  </Stack>
                ) : (
                  <Typography sx={{ fontWeight: 600, fontSize: 13, color: 'text.secondary' }}>
                    No subscription on file.
                  </Typography>
                )}
                <Button
                  variant="outlined"
                  disabled={busyVerb !== null || !summary.accountId}
                  onClick={() => void handleResync()}
                  startIcon={<FontAwesomeIcon icon="arrows-rotate" style={{ width: 16, height: 16 }} />}
                  sx={{ alignSelf: 'flex-start', minHeight: 48 }}
                >
                  {busyVerb === 'resync' ? 'Resyncing...' : 'Resync from Stripe'}
                </Button>
              </Stack>
            </SectionCard>

            {/* Aggregate counts (AC-02, count-only, dependency-tolerant). */}
            <SectionCard>
              <Stack spacing={1.5}>
                <SectionHeading icon="box-archive" label="Keepsakes and devices" />
                <CountFigure icon="box-archive" label="Vault tales" section={summary.vaultTales} />
                <CountFigure icon="mobile-screen" label="Linked devices" section={summary.linkedDevices} />
              </Stack>
            </SectionCard>

            {/* Grants + the comp/extend control (AC-06 - reuses story 02's grant plumbing). */}
            <SectionCard>
              <Stack spacing={2}>
                <SectionHeading icon="key" label="Entitlements" />
                {summary.grants.length === 0 ? (
                  <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary' }}>
                    This account holds no entitlements yet.
                  </Typography>
                ) : (
                  <Stack spacing={1.5}>
                    {summary.grants.map((g) => (
                      <GrantRow
                        key={g.capabilityKey}
                        grant={g}
                        pending={pendingKey === g.capabilityKey}
                        onRevoke={(key) => void handleRevoke(key)}
                      />
                    ))}
                  </Stack>
                )}

                {grantForm}
              </Stack>
            </SectionCard>

            {/* Resend the account's sign-in link (AC-03). */}
            <SectionCard>
              <Stack spacing={1.5}>
                <SectionHeading icon="envelope" label="Sign-in link" />
                <Typography sx={{ fontWeight: 600, fontSize: 13, color: 'text.secondary' }}>
                  Send a fresh magic link to {summary.email} - bounded per account so it can never flood an inbox.
                </Typography>
                <Button
                  variant="outlined"
                  disabled={busyVerb !== null}
                  onClick={() => void handleResend()}
                  startIcon={<FontAwesomeIcon icon="envelope" style={{ width: 16, height: 16 }} />}
                  sx={{ alignSelf: 'flex-start', minHeight: 48 }}
                >
                  {busyVerb === 'resend' ? 'Sending...' : 'Resend magic link'}
                </Button>
              </Stack>
            </SectionCard>
          </Stack>
        )}

        {/* Tale-targeted content verbs (AC-04/AC-05): DIRECT slug / (vaultId, taleId) inputs, NOT a
            search key into the account lookup. Always available, in their own cards. */}
        <Stack spacing={3}>
          <SectionCard>
            <Box component="form" onSubmit={onExtend} noValidate>
              <Stack spacing={1.5}>
                <SectionHeading icon="clock" label="Extend a shared tale link" />
                <Typography sx={{ fontWeight: 600, fontSize: 13, color: 'text.secondary' }}>
                  Push out an expiring public tale link by its slug. This acts on the tale directly - it does
                  not look up an account.
                </Typography>
                <Controller
                  name="slug"
                  control={extend.control}
                  render={({ field }) => <TextField {...field} fullWidth label="Public tale slug" autoComplete="off" />}
                />
                <Button
                  type="submit"
                  variant="contained"
                  disabled={extend.formState.isSubmitting || extendSlug.length === 0}
                  startIcon={<FontAwesomeIcon icon="clock" style={{ width: 16, height: 16 }} />}
                  sx={{ alignSelf: 'flex-start', minHeight: 48 }}
                >
                  {extend.formState.isSubmitting ? 'Extending...' : 'Extend link TTL'}
                </Button>
              </Stack>
            </Box>
          </SectionCard>

          <SectionCard>
            <Box component="form" onSubmit={onRestore} noValidate>
              <Stack spacing={1.5}>
                <SectionHeading icon="trash-arrow-up" label="Restore a deleted keepsake" />
                <Typography sx={{ fontWeight: 600, fontSize: 13, color: 'text.secondary' }}>
                  Undo a family's own accidental delete within its recovery window, by vault id and tale id.
                  A courtesy action - a moderation takedown is restored from the Content tab with more friction.
                </Typography>
                <Controller
                  name="vaultId"
                  control={restore.control}
                  render={({ field }) => <TextField {...field} fullWidth label="Vault id" autoComplete="off" />}
                />
                <Controller
                  name="taleId"
                  control={restore.control}
                  render={({ field }) => <TextField {...field} fullWidth label="Tale id" autoComplete="off" />}
                />
                <Button
                  type="submit"
                  variant="contained"
                  disabled={restore.formState.isSubmitting || restoreVault.length === 0 || restoreTale.length === 0}
                  startIcon={<FontAwesomeIcon icon="trash-arrow-up" style={{ width: 16, height: 16 }} />}
                  sx={{ alignSelf: 'flex-start', minHeight: 48 }}
                >
                  {restore.formState.isSubmitting ? 'Restoring...' : 'Restore keepsake'}
                </Button>
              </Stack>
            </Box>
          </SectionCard>
        </Stack>
      </Stack>
    </Box>
  );
}
