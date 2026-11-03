import { defineConfig } from '@playwright/test';
import base from './playwright.config.mjs';
// FAQ 来源替换桥接使用专用真实栈夹具，不进入普通真实栈发现范围。
export default defineConfig({...base,testMatch:'**/ai-faq-drafts.spec.mjs',projects:base.projects.map(p=>({...p,testIgnore:[],testMatch:'**/ai-faq-drafts.spec.mjs'}))});
