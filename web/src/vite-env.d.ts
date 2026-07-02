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
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
