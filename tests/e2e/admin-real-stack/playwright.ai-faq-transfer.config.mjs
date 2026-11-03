import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 新传输场景复用真实 Native API 和专用数据库，不发现已验收的旧 FAQ 流程。
export default defineConfig({...base,testMatch:'**/ai-faq-transfer.spec.mjs',projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/ai-faq-transfer.spec.mjs'}))});
