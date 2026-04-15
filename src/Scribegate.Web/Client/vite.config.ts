import { defineConfig } from 'vite';

export default defineConfig({
  root: '.',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5210',
      '/healthz': 'http://localhost:5210',
      '/swagger': 'http://localhost:5210',
    },
  },
});
