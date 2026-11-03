import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';
// 真实嵌入关系需要专用完整发布夹具，普通真实栈不隐式运行该场景。
export default defineConfig({ ...base, testMatch: '**/ai-search-relations.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-search-relations.spec.mjs' })) });
