import { createSSRApp } from 'vue';

import App from './App.vue';
import { i18n } from './i18n';

if (import.meta.env.DEV && typeof window !== 'undefined') {
  // 测试端口仅进入 H5 开发构建；生产构建会静态移除整个动态导入。
  void import('./e2e/h5-bridge').then(({ installH5E2EBridge }) => {
    installH5E2EBridge();
  });
}

export function createApp() {
  const app = createSSRApp(App);
  app.use(i18n);

  return { app };
}
