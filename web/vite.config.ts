// Vite config for the QuibbleStone web client.
// - React plugin for Fast Refresh + JSX transform.
// - Dev server on port 5173 (the API's CORS allowlist expects this origin).
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
});
