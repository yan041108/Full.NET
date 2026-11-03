import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';

// 明确收费的改写重排场景只运行专用原生夹具，普通真实栈不隐式派发模型。
export default defineConfig({ ...base, testMatch: '**/ai-search-refinement.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-search-refinement.spec.mjs' })) });
