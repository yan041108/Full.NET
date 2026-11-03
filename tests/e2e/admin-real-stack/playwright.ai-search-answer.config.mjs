import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';

// 收费确认与发布替换只消费专用冻结夹具，普通真实栈不隐式运行。
export default defineConfig({ ...base, testMatch: '**/ai-search-answer.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-search-answer.spec.mjs' })) });
