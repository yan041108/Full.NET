import { defineConfig } from '@playwright/test';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.GITHUB_ACTIONS ? 'github' : 'line',
  globalSetup: './global-setup.mjs',
  globalTeardown: './global-teardown.mjs',
  use: {
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: [
    {
      command: 'pnpm --dir ../../.. --filter @fullnet/admin exec vite --host localhost --port 25173 --logLevel error',
      url: 'http://localhost:25173',
      reuseExistingServer: !process.env.CI || process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'ignore',
      stderr: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'pipe',
      env: {
        VITE_API_BASE_URL: apiBaseUrl,
        VITE_STRICT_CSP: '1'
      }
    },
    {
      command: 'node scripts/serve-oidc-fixture.mjs 5173 fixtures/oidc-rp-a',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI || process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: 'ignore',
      stderr: 'pipe'
    },
    {
      command: 'node scripts/serve-oidc-fixture.mjs 5174 fixtures/oidc-rp-b',
      url: 'http://localhost:5174',
      reuseExistingServer: !process.env.CI || process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: 'ignore',
      stderr: 'pipe'
    },
    {
      command: 'pnpm --dir ../../.. --filter @fullnet/admin exec vite --host localhost --port 25175 --logLevel error',
      url: 'http://localhost:25175',
      reuseExistingServer: !process.env.CI || process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'ignore',
      stderr: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'pipe',
      env: {
        VITE_API_BASE_URL: apiBaseUrl,
        VITE_STRICT_CSP: '1',
        VITE_IDENTITY_AUTH_MODE: 'oidc-center',
        VITE_IDENTITY_OIDC_CLIENT_ID: 'e2e-admin-oidc-spa'
      }
    }
  ],
  projects: [
    {
      name: 'vue-admin',
      metadata: { clientKind: 'vue' },
      testIgnore: '**/admin-oidc-center.spec.mjs',
      use: { baseURL: 'http://localhost:25173' }
    },
    {
      name: 'vue-admin-oidc-center',
      metadata: { clientKind: 'vue-oidc-center' },
      testMatch: '**/admin-oidc-center.spec.mjs',
      use: { baseURL: 'http://localhost:25175' }
    }
  ]
});
