import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 两个视口使用两个独立操作者和本人库；只发现本批实际运行、账本与来源清空流程。
export default defineConfig({...base,retries:0,fullyParallel:true,workers:2,testMatch:'**/ai-knowledge-runs.spec.mjs',projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/ai-knowledge-runs.spec.mjs'}))});
