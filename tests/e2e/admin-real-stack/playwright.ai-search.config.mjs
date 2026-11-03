import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';

// 专用 Linux 原生夹具才提供发布代次与协调文件，普通真实栈不得隐式消费。
export default defineConfig({ ...base, testMatch: '**/ai-knowledge-search.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-knowledge-search.spec.mjs' })) });
