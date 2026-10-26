import { configDefaults, defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  plugins: [vue()],
  test: {
    // 跨端组件回归解析uni-app已锁定的依赖，后台生产构建不引入此别名。
    alias: { 'vue-i18n': fileURLToPath(new URL('../../clients/uniapp/node_modules/vue-i18n/dist/vue-i18n.mjs', import.meta.url)) },
    environment: 'jsdom',
    clearMocks: true,
    setupFiles: ['./src/test/setup.ts'],
    exclude: [...configDefaults.exclude, 'tests/**/*.mjs']
  }
});
