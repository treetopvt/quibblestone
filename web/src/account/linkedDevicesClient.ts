// ----------------------------------------------------------------------------
//  linkedDevicesClient.ts - the AUTHENTICATED web client for the Account
//  page's "Linked devices" section (accounts-identity/09, AC-01/AC-04/AC-07).
//  A thin REST client, NOT the feature: minting/listing/revoking a family-
//  device token and toggling its adult-unlock signal all live server-side in
//  `api/src/Controllers/AccountsController.cs`.
//
//  Mirrors entitlementsClient.ts exactly: the signed-in purchaser's credential
//  (accounts-identity/03's in-memory `PurchaserSession`) travels as a bearer,
//  the API base comes from `import.meta.env.VITE_API_BASE_URL` (never
//  hardcoded), and every call FAILS GRACEFULLY - a network error, non-OK
//  status, or unparseable body resolves to a friendly result, never a throw.
//  A 401 means "not signed in" (the same posture as entitlementsClient's
//  'signed-out' status) - the caller shows a sign-in prompt, never device data.
//
//  NO PII (AC-04/AC-05): a `LinkedDeviceSummary` carries only a short, random,
//  non-identifying label + timestamps + the adult-unlock flag + revoked state -
//  never an IP address, user agent, or any other device fingerprint. This
//  client only shapes what the server already scoped down; it adds nothing.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** One linked family device, as listed on the Account page (AC-04). No PII. */
export interface LinkedDeviceSummary {
  deviceTokenId: string;
  label: string;
  createdUtc: string;
  /** ISO timestamp of last use, or null when the device has never resolved a room since linking. */
  lastUsedUtc: string | null;
  /** The AC-07 adult-unlock signal for this device - defaults false on every newly redeemed device. */
  isAdultConfirmedDevice: boolean;
  revoked: boolean;
}

/** Result of minting a new link code (AC-01) to hand to a kid's device. */
export interface MintLinkCodeResult {
  status: 'ok' | 'signed-out' | 'error';
  code: string | null;
  /** ISO expiry - the code's short redeem window. */
  expiresUtc: string | null;
}

/** Result of listing the signed-in account's linked devices. */
export interface LinkedDevicesResult {
  status: 'ok' | 'signed-out' | 'error';
  devices: LinkedDeviceSummary[];
}

function apiBase(): string {
  return import.meta.env.VITE_API_BASE_URL;
}

function authHeaders(credential: string): HeadersInit {
  return { Authorization: `Bearer ${credential}` };
}

function asLinkedDeviceSummary(value: unknown): LinkedDeviceSummary | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (
    typeof r.deviceTokenId !== 'string' ||
    typeof r.label !== 'string' ||
    typeof r.createdUtc !== 'string' ||
    typeof r.isAdultConfirmedDevice !== 'boolean' ||
    typeof r.revoked !== 'boolean'
  ) {
    return null;
  }
  const lastUsedUtc = typeof r.lastUsedUtc === 'string' ? r.lastUsedUtc : null;
  return {
    deviceTokenId: r.deviceTokenId,
    label: r.label,
    createdUtc: r.createdUtc,
    lastUsedUtc,
    isAdultConfirmedDevice: r.isAdultConfirmedDevice,
    revoked: r.revoked,
  };
}

/**
 * Mints a short-lived, human-enterable link code tied to the signed-in
 * account (POST /api/accounts/devices/link, AC-01), for the parent to read
 * onto the kid's device. Resolves 'signed-out' on a 401, 'error' on any
 * transport/parse failure, 'ok' with the code + expiry otherwise. Never throws.
 */
export async function mintDeviceLinkCode(credential: string): Promise<MintLinkCodeResult> {
  try {
    const response = await fetch(`${apiBase()}/api/accounts/devices/link`, {
      method: 'POST',
      headers: authHeaders(credential),
    });

    if (response.status === 401) {
      return { status: 'signed-out', code: null, expiresUtc: null };
    }
    if (!response.ok) {
      return { status: 'error', code: null, expiresUtc: null };
    }

    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return { status: 'error', code: null, expiresUtc: null };
    const record = body as Record<string, unknown>;
    if (typeof record.code !== 'string' || typeof record.expiresUtc !== 'string') {
      return { status: 'error', code: null, expiresUtc: null };
    }
    return { status: 'ok', code: record.code, expiresUtc: record.expiresUtc };
  } catch {
    return { status: 'error', code: null, expiresUtc: null };
  }
}

/**
 * Loads the signed-in account's linked devices (GET /api/accounts/devices,
 * AC-04). Resolves 'signed-out' on a 401, 'error' on any transport/parse
 * failure, 'ok' with the list otherwise. Never throws.
 */
export async function fetchLinkedDevices(credential: string): Promise<LinkedDevicesResult> {
  try {
    const response = await fetch(`${apiBase()}/api/accounts/devices`, {
      headers: authHeaders(credential),
    });

    if (response.status === 401) {
      return { status: 'signed-out', devices: [] };
    }
    if (!response.ok) {
      return { status: 'error', devices: [] };
    }

    const body: unknown = await response.json();
    if (!Array.isArray(body)) return { status: 'error', devices: [] };
    const devices = body.map(asLinkedDeviceSummary).filter((d): d is LinkedDeviceSummary => d !== null);
    return { status: 'ok', devices };
  } catch {
    return { status: 'error', devices: [] };
  }
}

/**
 * Revokes one linked device (POST /api/accounts/devices/{id}/revoke, AC-04).
 * Resolves true on success, false on any failure (a 401, a non-2xx, or a
 * transport error) - never throws. The caller re-fetches the list afterward.
 */
export async function revokeLinkedDevice(credential: string, deviceTokenId: string): Promise<boolean> {
  try {
    const response = await fetch(
      `${apiBase()}/api/accounts/devices/${encodeURIComponent(deviceTokenId)}/revoke`,
      { method: 'POST', headers: authHeaders(credential) },
    );
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Sets one linked device's AC-07 adult-unlock signal (POST
 * /api/accounts/devices/{id}/adult-confirm { confirmed }). This is the
 * load-bearing opt-in that lets teen-plus content resolve on that device's
 * next room - defaulting off on every newly redeemed device, an adult flips
 * it explicitly here. Resolves true on success, false on any failure - never
 * throws. The caller re-fetches the list afterward.
 */
export async function setDeviceAdultConfirmed(
  credential: string,
  deviceTokenId: string,
  confirmed: boolean,
): Promise<boolean> {
  try {
    const response = await fetch(
      `${apiBase()}/api/accounts/devices/${encodeURIComponent(deviceTokenId)}/adult-confirm`,
      {
        method: 'POST',
        headers: { ...authHeaders(credential), 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmed }),
      },
    );
    return response.ok;
  } catch {
    return false;
  }
}
