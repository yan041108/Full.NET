import base from './playwright.config.mjs';

// 默认开发模式套件不收集生产产物的网络断言，专用入口只运行这三个直接场景。
export default {
  ...base,
  testDir: '.',
  testMatch: ['production-tests/startup-sdk-delivery.spec.mjs', 'tests/shell-parity.spec.mjs'],
  grep: /匿名首屏|生产启动入口|动态导航和可信租户/u,
  workers: 1,
  webServer: {
    command: 'pnpm --dir ../../.. --filter @fullnet/admin exec vite preview --host 127.0.0.1 --port 15174 --strictPort',
    url: 'http://127.0.0.1:15174',
    reuseExistingServer: false,
    stdout: 'ignore',
    stderr: 'pipe'
  },
  projects: [{ name: 'vue-admin', metadata: { clientKind: 'vue' }, use: { baseURL: 'http://127.0.0.1:15174' } }]
};
