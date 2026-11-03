import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 真实 API 与 Worker 夹具仅供本批双身份发布流程，不加入普通真实栈发现。
export default defineConfig({...base,testMatch:'**/ai-faq-lifecycle.spec.mjs',projects:base.projects.map(p=>({...p,testIgnore:[],testMatch:'**/ai-faq-lifecycle.spec.mjs'}))});
