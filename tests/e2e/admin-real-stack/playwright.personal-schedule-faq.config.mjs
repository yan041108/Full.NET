import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 新增本人来源FAQ的两位独立操作者，不重复旧FAQ或Workflow页面验收。
export default defineConfig({...base,retries:0,fullyParallel:true,workers:2,testMatch:'**/personal-schedule-faq.spec.mjs',
  projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/personal-schedule-faq.spec.mjs'}))});
