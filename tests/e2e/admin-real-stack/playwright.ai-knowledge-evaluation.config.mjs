import {defineConfig} from '@playwright/test';
import base from './playwright.config.mjs';
// 仅发现评估集新页面，以当前新发布的 MySQL Native API 验证，不调用模型。
export default defineConfig({...base,retries:0,testMatch:'**/ai-knowledge-evaluation.spec.mjs',projects:base.projects.map(value=>({...value,testIgnore:[],testMatch:'**/ai-knowledge-evaluation.spec.mjs'}))});
