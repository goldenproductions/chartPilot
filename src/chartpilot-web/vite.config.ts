import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The dev server is bound to loopback only: ChartPilot renders arbitrary Go
// templates and is not a service to expose on a network interface.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: '127.0.0.1',
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: false,
      },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 4096,
  },
  worker: {
    format: 'es',
  },
});
