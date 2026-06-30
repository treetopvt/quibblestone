/// <reference types="vite/client" />

// Typed view of the VITE_-prefixed environment variables this app reads.
// Keep in sync with web/.env.development.
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  readonly VITE_SIGNALR_HUB_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
