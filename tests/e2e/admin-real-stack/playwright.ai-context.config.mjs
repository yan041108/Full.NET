import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';

// 专用原生夹具冻结发布身份并协调替换，普通真实栈不隐式消费桥接数据。
export default defineConfig({ ...base, testMatch: '**/ai-knowledge-context.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-knowledge-context.spec.mjs' })) });
