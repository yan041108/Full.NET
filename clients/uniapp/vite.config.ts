import { createRequire } from 'node:module';
import { defineConfig } from 'vite';
import type { PluginOption } from 'vite';

const require = createRequire(import.meta.url);
const uni = require('@dcloudio/vite-plugin-uni').default as () => PluginOption;
const i18nAlias = { find: /^vue-i18n$/, replacement: require.resolve('vue-i18n/dist/vue-i18n.esm-bundler.js') };

export default defineConfig(({ mode }) => ({
  // 小程序插件内置旧版 i18n；显式使用同一已修复发行，避免其 runtime 与新 core 混用。
  resolve: {
    alias: [i18nAlias]
  },
  // 小程序禁止动态代码生成，消息编译使用 AST 解释路径。
  define: { __INTLIFY_JIT_COMPILATION__: true },
  // Vitest 只验证工作区契约；页面清单由后续任务提供，因此测试模式不初始化 uni 编译插件。
  plugins: mode === 'test' ? [] : [uni(), {
    name: 'fullnet-patched-i18n',
    enforce: 'post',
    // DCloud 在 config hook 中覆盖小程序别名，必须在其后选择已修复的运行时。
    config: () => ({ resolve: { alias: [i18nAlias] } })
  }]
}));
