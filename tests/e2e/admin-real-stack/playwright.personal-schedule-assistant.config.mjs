import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';
// 两个独立本人会话验证同一正式日程来源入口，不复用旧K05桥接或业务用例。
export default defineConfig({ ...base, retries: 0, fullyParallel: true, workers: 2,
  testMatch: '**/personal-schedule-assistant.spec.mjs',
  projects: base.projects.map(value => ({ ...value, testIgnore: [], testMatch: '**/personal-schedule-assistant.spec.mjs' })) });
