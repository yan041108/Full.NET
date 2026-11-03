import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 仅发现 K05 元数据工作台，复用实际 MySQL Native API；不调用模型或 Worker。
export default defineConfig({...base,testMatch:'**/ai-knowledge-operations.spec.mjs',projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/ai-knowledge-operations.spec.mjs'}))});
