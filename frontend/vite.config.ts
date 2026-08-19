import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The dev server proxies /api to the local ASP.NET Core backend so the browser sees a
// same-origin URL and no CORS pre-flight is involved during development.
export default defineConfig({
  plugins: [react()],
  resolve: {
    // Mirrors the "@/*" -> "src/*" mapping in tsconfig.json. TypeScript's paths only affect
    // type resolution, so the bundler needs the alias declared here as well.
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'https://localhost:7135',
        changeOrigin: true,
        secure: false, // the local API uses a self-signed development certificate
      },
      '/health': { target: 'https://localhost:7135', changeOrigin: true, secure: false },
    },
  },
  build: { outDir: 'dist', sourcemap: true },
});
