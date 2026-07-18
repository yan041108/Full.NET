import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  // Element Plus 与浏览器可访问性扫描内存占用较高，限制并发可避免 Vite/Edge 被系统回收。
  workers: 4,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.GITHUB_ACTIONS ? 'github' : 'list',
  use: {
    // 本地复用系统 Edge，GitHub Actions 则使用流水线安装的 Chromium。
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: [
    {
      // 使用验收专用端口，避免复用开发者正在运行的 Vite/uni-app 页面造成串台。
      command: 'pnpm --dir ../../.. --filter @fullnet/admin exec vite --host 127.0.0.1 --port 15173',
      url: 'http://127.0.0.1:15173',
      reuseExistingServer: !process.env.CI
    },
    {
      command: 'pnpm --dir ../../.. --filter @fullnet/admin-layui exec vite --host 127.0.0.1 --port 15174',
      url: 'http://127.0.0.1:15174',
      reuseExistingServer: !process.env.CI
    }
  ],
  projects: [
    {
      name: 'vue-admin',
      metadata: { clientKind: 'vue' },
      use: { baseURL: 'http://127.0.0.1:15173' }
    },
    {
      name: 'layui-admin',
      metadata: { clientKind: 'layui' },
      use: { baseURL: 'http://127.0.0.1:15174' }
    }
  ]
});
