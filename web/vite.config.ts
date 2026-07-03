// Vite config for the QuibbleStone web client.
// - React plugin for Fast Refresh + JSX transform.
// - Dev server on port 5173 (the API's CORS allowlist expects this origin).
// - TWO build entries (sysadmin-console/01, #135): the kid app (index.html) and
//   the SEPARATE operator back office (admin.html -> src/admin/main.tsx). They are
//   distinct Rollup inputs, so the back office is its OWN bundle/entry tree with no
//   import edge from the kid app (AC-04). Paths are resolved from this file's URL
//   (ESM-safe: __dirname is not defined in an ESM config).
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
  build: {
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL('./index.html', import.meta.url)),
        admin: fileURLToPath(new URL('./admin.html', import.meta.url)),
      },
    },
  },
});
