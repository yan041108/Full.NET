import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';
// 两个独立本人账号只验本次混合问答，不重复旧来源预览或 Workflow 页面。
export default defineConfig({ ...base, retries: 0, fullyParallel: true, workers: 2,
  testMatch: '**/personal-schedule-answer.spec.mjs',
  projects: base.projects.map(value => ({ ...value, testIgnore: [], testMatch: '**/personal-schedule-answer.spec.mjs' })) });
