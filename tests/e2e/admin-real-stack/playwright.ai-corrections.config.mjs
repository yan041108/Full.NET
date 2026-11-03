import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';

// 专项依赖显式 Linux 桥接夹具，不能在普通真实栈发现时隐式启动或读取另一任务状态。
export default defineConfig({
  ...base,
  testMatch: '**/ai-correction-workbench.spec.mjs',
  projects: base.projects.map(project => ({ ...project, testIgnore: [], testMatch: '**/ai-correction-workbench.spec.mjs' }))
});
