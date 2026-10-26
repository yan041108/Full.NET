import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
// @ts-expect-error 独立运行构建的 Vite 插件由原生 ESM 提供。
import { vformRuntimePlugin } from './vform-runtime-plugin.mjs';

const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5149';

export default defineConfig({
  plugins: [vue(), vformRuntimePlugin()],
  server: {
    host: 'localhost',
    strictPort: true,
    headers: process.env.VITE_STRICT_CSP === '1'
      ? { 'Content-Security-Policy': "script-src 'self'; object-src 'none'; base-uri 'self'; frame-src 'self'" }
      : { 'Content-Security-Policy': "frame-src 'self'" },
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: false
      },
      '/hubs': {
        target: apiTarget,
        changeOrigin: false,
        ws: true
      },
      '/connect': {
        target: apiTarget,
        changeOrigin: false
      },
      '/identity': {
        target: apiTarget,
        changeOrigin: false
      },
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
