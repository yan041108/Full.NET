import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';
// FAQ 来源的 Native/真实数据库夹具独立供给，只发现本批实际页面场景。
export default defineConfig({ ...base, testMatch: '**/ai-faq-search.spec.mjs', projects: base.projects.map(value => ({ ...value, testIgnore: [], testMatch: '**/ai-faq-search.spec.mjs' })) });
