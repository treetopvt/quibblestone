// ----------------------------------------------------------------------------
//  billingClient.ts - the web client for the purchaser billing surfaces: the gated
//  purchase paywall (billing-entitlements/04) and the goodwill tip jar (billing-
//  entitlements/02). A thin REST client, NOT the feature: the real Stripe checkout,
//  the product -> capability map, and the entitlement grant all live server-side
//  (api/src/Billing + api/src/Controllers/BillingController.cs). This module only
//  fetches the paywall products and starts a checkout, then the caller redirects the
//  browser to the returned Stripe-hosted URL.
//
//  Mirrors web/src/account/signInClient.ts + web/src/safety/checkWord.ts: the API base
//  URL comes from `import.meta.env.VITE_API_BASE_URL` (never hardcoded, never a secret
//  in a VITE_ var - CLAUDE.md section 4), and every call FAILS GRACEFULLY - a network
//  error, non-OK status, or unparseable body resolves to a friendly not-enabled result
//  rather than throwing, so a purchase surface never shows a raw error.
//
//  CONFIG-OFF: when Stripe is not configured server-side, every call resolves to
//  `{ enabled: false }` and the UI shows a warm "not available yet" state - free play
//  is completely unaffected either way.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** A paywall product for display (billing-entitlements/04). */
export interface BillingProduct {
  productId: string;
  displayName: string;
  description: string;
  /** "payment" (one-time) or "subscription" (recurring). */
  mode: string;
  /** False when no price is configured yet - shown but not buyable. */
  purchasable: boolean;
}

/** Result of fetching the paywall products. */
export interface ProductsResult {
  /** False when billing is not configured server-side (show a friendly "not available"). */
  enabled: boolean;
  products: BillingProduct[];
}

/** Result of starting a checkout / tip: where to redirect, or why not. */
export interface CheckoutStartResult {
  /** False when billing is off - show a friendly note, do not treat as an error. */
  enabled: boolean;
  /** The Stripe-hosted checkout URL to redirect to (present on success). */
  url?: string;
  /** A friendly message for a not-enabled or blocked outcome (e.g. a filtered tip message). */
  message?: string;
}

const UNAVAILABLE: CheckoutStartResult = {
  enabled: false,
  message: 'Purchases are not available just now - free play is always on.',
};

const base = () => import.meta.env.VITE_API_BASE_URL;

/** Narrows an unknown products body. */
function asProductsResult(value: unknown): ProductsResult | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.enabled !== 'boolean' || !Array.isArray(record.products)) return null;
  const products: BillingProduct[] = [];
  for (const raw of record.products) {
    if (typeof raw !== 'object' || raw === null) continue;
    const p = raw as Record<string, unknown>;
    if (
      typeof p.productId === 'string' &&
      typeof p.displayName === 'string' &&
      typeof p.description === 'string' &&
      typeof p.mode === 'string' &&
      typeof p.purchasable === 'boolean'
    ) {
      products.push({
        productId: p.productId,
        displayName: p.displayName,
        description: p.description,
        mode: p.mode,
        purchasable: p.purchasable,
      });
    }
  }
  return { enabled: record.enabled, products };
}

/** Narrows an unknown checkout-start body. */
function asCheckoutStartResult(value: unknown): CheckoutStartResult | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.enabled !== 'boolean') return null;
  return {
    enabled: record.enabled,
    url: typeof record.url === 'string' && record.url.length > 0 ? record.url : undefined,
    message: typeof record.message === 'string' && record.message.length > 0 ? record.message : undefined,
  };
}

/** Fetches the paywall products (GET /api/billing/products). Never throws. */
export async function fetchProducts(): Promise<ProductsResult> {
  try {
    const response = await fetch(`${base()}/api/billing/products`);
    if (!response.ok) return { enabled: false, products: [] };
    const parsed = asProductsResult(await response.json());
    return parsed ?? { enabled: false, products: [] };
  } catch {
    return { enabled: false, products: [] };
  }
}

/**
 * Starts a gated-purchase checkout for a product id (POST /api/billing/checkout).
 * Resolves the redirect URL on success, or a friendly not-enabled result otherwise.
 * Never throws.
 */
export async function startCheckout(productId: string, purchaserEmail?: string): Promise<CheckoutStartResult> {
  try {
    const response = await fetch(`${base()}/api/billing/checkout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ productId, purchaserEmail: purchaserEmail ?? null }),
    });
    if (!response.ok) return UNAVAILABLE;
    return asCheckoutStartResult(await response.json()) ?? UNAVAILABLE;
  } catch {
    return UNAVAILABLE;
  }
}

/**
 * Starts a goodwill tip checkout (POST /api/billing/tip). An optional message is
 * safety-filtered server-side (billing-02 AC-05); a blocked message resolves to a
 * not-enabled result carrying the friendly reason. Never throws.
 */
export async function startTip(message?: string): Promise<CheckoutStartResult> {
  try {
    const response = await fetch(`${base()}/api/billing/tip`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: message ?? null }),
    });
    if (!response.ok) return UNAVAILABLE;
    return asCheckoutStartResult(await response.json()) ?? UNAVAILABLE;
  } catch {
    return UNAVAILABLE;
  }
}
