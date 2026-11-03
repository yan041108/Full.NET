import { defineConfig } from '@playwright/test';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL
  ?? `http://localhost:${process.env.FULLNET_E2E_API_PORT ?? '5149'}`;
const adminOrigin = process.env.FULLNET_E2E_ADMIN_ORIGIN ?? 'http://localhost:25173';
const adminPort = new URL(adminOrigin).port || '80';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.GITHUB_ACTIONS),
  retries: process.env.GITHUB_ACTIONS ? 1 : 0,
  reporter: process.env.FULLNET_E2E_L3_JSON_REPORT
    ? [
        [process.env.GITHUB_ACTIONS ? 'github' : 'line'],
        ['json', { outputFile: process.env.FULLNET_E2E_L3_JSON_REPORT }]
      ]
    : process.env.GITHUB_ACTIONS
      ? 'github'
      : 'line',
  globalSetup: './global-setup.mjs',
  globalTeardown: './global-teardown.mjs',
  use: {
    channel: process.env.GITHUB_ACTIONS ? undefined : 'msedge',
    trace: 'retain-on-failure'
  },
  webServer: [
    {
      command: `pnpm --dir ../../.. --filter @fullnet/admin exec vite --host localhost --port ${adminPort} --logLevel error`,
      url: adminOrigin,
      reuseExistingServer: process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'ignore',
      stderr: process.env.PLAYWRIGHT_WEBSERVER_LOGS === '1' ? 'pipe' : 'pipe',
      env: {
        VITE_API_PROXY_TARGET: apiBaseUrl,
        VITE_STRICT_CSP: '1'
      }
    },
    ...(process.env.FULLNET_E2E_SKIP_OIDC_FIXTURES === '1' ? [] : [{
      command: 'node scripts/serve-oidc-fixture.mjs 5173 fixtures/oidc-rp-a',
      url: 'http://localhost:5173',
      reuseExistingServer: process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: 'ignore',
      stderr: 'pipe'
    },
    {
      command: 'node scripts/serve-oidc-fixture.mjs 5174 fixtures/oidc-rp-b',
      url: 'http://localhost:5174',
      reuseExistingServer: process.env.FULLNET_E2E_REUSE_SERVER === '1',
      stdout: 'ignore',
      stderr: 'pipe'
    }])

  ],
  projects: [
    {
      name: 'vue-admin',
      metadata: { clientKind: 'vue' },
      // 历史 SPA 场景保留供业务消费追踪；BFF 真实入口由 test:oidc-center 和独立 HTTPS 夹具执行。
      testIgnore: ['**/admin-oidc-center.spec.mjs', '**/ai-correction-workbench.spec.mjs', '**/ai-knowledge-search.spec.mjs', '**/ai-knowledge-context.spec.mjs', '**/ai-search-answer.spec.mjs', '**/ai-search-comparison.spec.mjs', '**/ai-search-relations.spec.mjs', '**/ai-search-refinement.spec.mjs', '**/ai-faq-drafts.spec.mjs', '**/ai-faq-lifecycle.spec.mjs', '**/ai-faq-search.spec.mjs', '**/ai-faq-transfer.spec.mjs', '**/ai-faq-parents.spec.mjs', '**/ai-faq-feedback.spec.mjs', '**/ai-knowledge-operations.spec.mjs', '**/ai-knowledge-evaluation.spec.mjs', '**/ai-knowledge-runs.spec.mjs', '**/personal-schedule-assistant.spec.mjs', '**/personal-schedule-answer.spec.mjs', '**/personal-schedule-faq.spec.mjs', '**/personal-schedule-completion.spec.mjs'],
      use: { baseURL: adminOrigin }
    }

  ]
});
