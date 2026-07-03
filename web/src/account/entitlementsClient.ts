// ----------------------------------------------------------------------------
//  entitlementsClient.ts - the web client for the restore/manage read endpoint
//  (billing-entitlements/05, issue #74). A thin REST client: it presents the
//  purchaser credential (from sign-in) as a bearer to GET /api/account/entitlements
//  and shapes the response for the Account page's "what's unlocked" list.
//
//  Mirrors signInClient.ts: API base from `import.meta.env.VITE_API_BASE_URL` (never
//  hardcoded), fails GRACEFULLY (never throws). A 401 means "not signed in" (AC-06) -
//  the caller shows a sign-in prompt, never entitlement state. Any other failure
//  resolves to a friendly unavailable result.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** One owned entitlement for display (billing-entitlements/05). No player/session data (AC-05). */
export interface OwnedEntitlement {
  key: string;
  label: string;
  /** "Subscription" | "OneTime" | "Operator". */
  source: string;
  /** ISO lease end, or null for a permanent grant. */
  validThrough: string | null;
}

/** Result of loading the signed-in purchaser's entitlements. */
export interface EntitlementsResult {
  /** 'ok' with the list, 'signed-out' (401 - prompt to sign in), or 'error' (transport). */
  status: 'ok' | 'signed-out' | 'error';
  entitlements: OwnedEntitlement[];
}

/** Narrows one unknown entitlement item. */
function asEntitlement(value: unknown): OwnedEntitlement | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.key !== 'string' || typeof r.label !== 'string' || typeof r.source !== 'string') return null;
  const validThrough = typeof r.validThrough === 'string' ? r.validThrough : null;
  return { key: r.key, label: r.label, source: r.source, validThrough };
}

/**
 * Loads the signed-in purchaser's active entitlements, presenting `credential` as a
 * bearer (POST-sign-in the SPA holds it in memory). Resolves 'signed-out' on a 401
 * (AC-06), 'error' on any transport/parse failure, 'ok' with the list otherwise.
 * Never throws.
 */
export async function fetchEntitlements(credential: string): Promise<EntitlementsResult> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/account/entitlements`, {
      headers: { Authorization: `Bearer ${credential}` },
      // Also send the same-site cookie if present (harmless cross-origin in dev).
      credentials: 'include',
    });

    if (response.status === 401) {
      return { status: 'signed-out', entitlements: [] };
    }
    if (!response.ok) {
      return { status: 'error', entitlements: [] };
    }

    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return { status: 'error', entitlements: [] };
    const list = (body as Record<string, unknown>).entitlements;
    if (!Array.isArray(list)) return { status: 'error', entitlements: [] };

    const entitlements = list.map(asEntitlement).filter((e): e is OwnedEntitlement => e !== null);
    return { status: 'ok', entitlements };
  } catch {
    return { status: 'error', entitlements: [] };
  }
}
