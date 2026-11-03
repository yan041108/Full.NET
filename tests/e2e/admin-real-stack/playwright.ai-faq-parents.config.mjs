import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 只发现本次父 FAQ 派生流程；真实 Native API 和 Worker 由本批隔离夹具提供。
export default defineConfig({...base,testMatch:'**/ai-faq-parents.spec.mjs',projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/ai-faq-parents.spec.mjs'}))});
