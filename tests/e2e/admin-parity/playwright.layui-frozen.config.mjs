import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  workers: 2,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.GITHUB_ACTIONS ? 'github' : 'list',
  use: {
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: {
    command: 'pnpm --dir ../../.. --filter @fullnet/admin-layui exec vite --host 127.0.0.1 --port 15174',
    url: 'http://127.0.0.1:15174',
    env: { VITE_REALTIME_ENABLED: 'false' },
    reuseExistingServer: !process.env.CI
  },
  projects: [
    {
      name: 'layui-admin',
      metadata: { clientKind: 'layui' },
      use: { baseURL: 'http://127.0.0.1:15174' }
    }
  ]
});