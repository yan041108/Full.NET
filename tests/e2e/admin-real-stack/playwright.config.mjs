import { defineConfig } from '@playwright/test';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.GITHUB_ACTIONS ? 'github' : 'list',
  globalSetup: './global-setup.mjs',
  globalTeardown: './global-teardown.mjs',
  use: {
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: {
    command: 'pnpm --dir ../../.. --filter @fullnet/admin exec vite --host localhost --port 25173',
    url: 'http://localhost:25173',
    reuseExistingServer: process.env.FULLNET_E2E_REUSE_SERVER === '1',
    env: {
      VITE_API_BASE_URL: apiBaseUrl
    }
  },
  projects: [
    {
      name: 'vue-admin',
      metadata: { clientKind: 'vue' },
      use: { baseURL: 'http://localhost:25173' }
    }
  ]
});