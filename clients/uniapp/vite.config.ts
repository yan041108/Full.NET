import { createRequire } from 'node:module';
import { defineConfig } from 'vite';
import type { PluginOption } from 'vite';

const require = createRequire(import.meta.url);
const uni = require('@dcloudio/vite-plugin-uni').default as () => PluginOption;

export default defineConfig(({ mode }) => ({
  // Vitest 只验证工作区契约；页面清单由后续任务提供，因此测试模式不初始化 uni 编译插件。
  plugins: mode === 'test' ? [] : [uni()]
}));
