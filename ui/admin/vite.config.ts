import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
// @ts-expect-error 独立运行构建的 Vite 插件由原生 ESM 提供。
import { vformRuntimePlugin } from './vform-runtime-plugin.mjs';
import { readFileSync } from 'node:fs';

const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5149';

export default defineConfig({
  plugins: [vue(), vformRuntimePlugin()],
  server: {
    host: 'localhost',
    strictPort: true,
    https: process.env.VITE_HTTPS_CERT && process.env.VITE_HTTPS_KEY
      ? { cert: readFileSync(process.env.VITE_HTTPS_CERT), key: readFileSync(process.env.VITE_HTTPS_KEY) }
      : undefined,
    headers: process.env.VITE_STRICT_CSP === '1'
      ? { 'Content-Security-Policy': "script-src 'self'; object-src 'none'; base-uri 'self'; frame-src 'self'" }
      : { 'Content-Security-Policy': "frame-src 'self'" },
    proxy: {
      '/api/v1/identity/browser/callback': {
        target: process.env.VITE_BFF_CALLBACK_PROXY_TARGET ?? apiTarget,
        xfwd: true,
        changeOrigin: false
      },
      '/api': {
        target: apiTarget,
        xfwd: true,
        changeOrigin: false
      },
      '/hubs': {
        target: apiTarget,
        changeOrigin: false,
        ws: true
      },
      '/connect': {
        target: apiTarget,
        xfwd: true,
        changeOrigin: false
      },
      '/identity': {
        target: apiTarget,
        xfwd: true,
        changeOrigin: false
      },
      '/.well-known': { target: apiTarget, changeOrigin: false, xfwd: true },
      '/scalar': {
        target: apiTarget,
        changeOrigin: false
      },
      '/openapi': {
        target: apiTarget,
        changeOrigin: false
      }
    }
  }
});
