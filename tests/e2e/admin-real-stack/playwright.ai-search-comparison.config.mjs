import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';

// 明确收费的比较场景只运行专用原生夹具，普通真实栈不隐式派发模型。
export default defineConfig({ ...base, testMatch: '**/ai-search-comparison.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-search-comparison.spec.mjs' })) });
