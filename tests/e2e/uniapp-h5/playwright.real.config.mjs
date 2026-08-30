import { defineConfig } from '@playwright/test';

process.env.FULLNET_E2E_API_PORT ??= '5159';
const apiBaseUrl = process.env.FULLNET_E2E_API_URL
  ?? `http://localhost:${process.env.FULLNET_E2E_API_PORT}`;

export default defineConfig({
  testDir: './tests-real',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.GITHUB_ACTIONS ? 'github' : 'line',
  globalSetup: '../admin-real-stack/global-setup.mjs',
  globalTeardown: '../admin-real-stack/global-teardown.mjs',
  outputDir: 'test-results-real',
  use: {
    baseURL: 'http://localhost:5175',
    // localhost 与 API 保持同站，真实覆盖 SameSite=Strict 刷新 Cookie。
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: {
    command: 'pnpm --dir ../../.. --filter @fullnet/uniapp exec uni -p h5 --host localhost --port 5175 --strictPort',
    url: 'http://localhost:5175',
    reuseExistingServer: !process.env.CI || process.env.FULLNET_E2E_REUSE_SERVER === '1',
    stdout: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'ignore',
    stderr: 'pipe',
    env: {
      VITE_API_BASE_URL: apiBaseUrl
    }
  },
  projects: [{ name: 'uniapp-h5-real' }]
});
