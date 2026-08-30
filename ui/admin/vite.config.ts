import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5149';

export default defineConfig({
  plugins: [vue()],
  server: {
    host: 'localhost',
    strictPort: true,
    headers: process.env.VITE_STRICT_CSP === '1'
      ? { 'Content-Security-Policy': "script-src 'self'; object-src 'none'; base-uri 'self'" }
      : undefined,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: false
      },
      '/hubs': {
        target: apiTarget,
        changeOrigin: false,
        ws: true
      }
    }
  }
});
