import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
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
      command: 'pnpm --dir ../../.. --filter @fullnet/admin dev',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: !process.env.CI
    },
    {
      command: 'pnpm --dir ../../.. --filter @fullnet/admin-layui dev',
      url: 'http://127.0.0.1:5174',
      reuseExistingServer: !process.env.CI
    }
  ],
  projects: [
    {
      name: 'vue-admin',
      metadata: { clientKind: 'vue' },
      use: { baseURL: 'http://127.0.0.1:5173' }
    },
    {
      name: 'layui-admin',
      metadata: { clientKind: 'layui' },
      use: { baseURL: 'http://127.0.0.1:5174' }
    }
  ]
});
