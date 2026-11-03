import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';
// 每个视口使用独立本人/批准者，避免正式单会话策略使其它视口失效。
export default defineConfig({ ...base, retries: 0, fullyParallel: true, workers: 2,
  testMatch: '**/personal-schedule-completion.spec.mjs',
  projects: base.projects.map(value => ({ ...value, testIgnore: [], testMatch: '**/personal-schedule-completion.spec.mjs' })) });
