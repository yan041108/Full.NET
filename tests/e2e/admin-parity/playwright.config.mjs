import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  // Element Plus 与浏览器可访问性扫描内存占用较高，限制并发可避免 Vite/Edge 被系统回收。
  workers: 4,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.GITHUB_ACTIONS ? 'github' : 'line',
  use: {
    // 本地复用系统 Edge，GitHub Actions 则使用流水线安装的 Chromium。
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: {
    command: 'pnpm --dir ../../.. --filter @fullnet/admin exec vite --host 127.0.0.1 --port 15173 --logLevel error',
    url: 'http://127.0.0.1:15173',
    env: { VITE_REALTIME_ENABLED: 'false' },
    reuseExistingServer: !process.env.CI,
    stdout: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'ignore',
    stderr: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'pipe'
  },
  projects: [
    {
      name: 'vue-admin',
      metadata: { clientKind: 'vue' },
      use: { baseURL: 'http://127.0.0.1:15173' }
    }
  ]
});