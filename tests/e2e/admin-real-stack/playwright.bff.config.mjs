import { defineConfig } from '@playwright/test';
import path from 'node:path';

// 独立真实后端夹具提供 HTTPS 和数据库，不能落回旧 SPA 令牌夹具或 Mock。
export default defineConfig({
  testDir: './bff-tests', workers: 1, retries: 0, timeout: 90_000,
  outputDir: process.env.FULLNET_BFF_UI_OUTPUT,
  reporter: [['list'], ['json', { outputFile: path.join(process.env.FULLNET_BFF_UI_OUTPUT, 'results.json') }]],
  use: { baseURL: process.env.FULLNET_BFF_UI_ORIGIN, ignoreHTTPSErrors: true,
    channel: process.platform === 'win32' ? 'msedge' : 'chromium', trace: 'off', screenshot: 'only-on-failure' },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1440, height: 1000 } } },
    { name: 'mobile', use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } }
  ]
});
