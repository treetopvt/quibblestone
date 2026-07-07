/// <reference types="vite/client" />

// Typed view of the VITE_-prefixed environment variables this app reads.
// Keep in sync with web/.env.development.
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  readonly VITE_SIGNALR_HUB_URL: string;
  /**
   * Optional public base URL for share/deep-links (session-engine/06). When
   * set and non-empty, the Lobby share widget builds `/join/:code` links from
   * this instead of `window.location.origin` - useful if the app is served
   * from a host/CDN edge that differs from the public-facing domain. Falls
   * back to the running origin when unset.
   */
  readonly VITE_PUBLIC_BASE_URL?: string;
  /**
   * Optional Google Analytics 4 measurement id (e.g. "G-XXXXXXXXXX") for product
   * analytics (analytics/01). PUBLIC id, not a secret - safe baked into the
   * bundle. When unset/empty, GA4 does not load and every analytics call is a
   * no-op (web/src/telemetry/analytics.ts). Set in deploy.yml's Build-web step.
   */
  readonly VITE_GA4_MEASUREMENT_ID?: string;
  /**
   * Optional Microsoft Clarity project id (e.g. "abcd1234ef") for session replay +
   * heatmaps (analytics/01). PUBLIC id, not a secret. When unset/empty, Clarity
   * does not load. IMPORTANT: create the Clarity project with Masking = "Mask"
   * (strict) so typed words are never recorded (analytics.ts, AC-03). Set in
   * deploy.yml's Build-web step.
   */
  readonly VITE_CLARITY_PROJECT_ID?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
