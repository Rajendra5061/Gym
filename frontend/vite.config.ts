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
  // Pre-bundling the heavyweights up front stops Vite from pausing the page mid-session to
  // re-optimise when a lazy route first pulls them in — the "reloading because a new
  // dependency was found" stall that reads as a slow app.
  optimizeDeps: {
    include: ['react', 'react-dom', 'react-router-dom', 'recharts'],
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
  // `vite preview` serves the production build with the same proxy, so the minified app can
  // be demoed on the dev machine at production speed without a separate web server.
  preview: {
    port: 5175,
    proxy: {
      '/api': { target: 'https://localhost:7135', changeOrigin: true, secure: false },
      '/health': { target: 'https://localhost:7135', changeOrigin: true, secure: false },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    // recharts is the single biggest chunk; splitting it keeps every non-chart page's
    // JavaScript small and lets charts arrive only where they are drawn.
    rollupOptions: {
      output: {
        manualChunks: {
          charts: ['recharts'],
          vendor: ['react', 'react-dom', 'react-router-dom'],
        },
      },
    },
  },
});
