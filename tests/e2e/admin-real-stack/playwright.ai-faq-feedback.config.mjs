import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 只发现已批准私人反馈转草稿；实际 Native API、受控 Worker 和原反馈由隔离夹具提供。
export default defineConfig({...base,testMatch:'**/ai-faq-feedback.spec.mjs',projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/ai-faq-feedback.spec.mjs'}))});
