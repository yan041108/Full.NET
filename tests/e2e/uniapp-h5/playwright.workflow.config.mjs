import { defineConfig } from '@playwright/test';
import path from 'node:path';

const origin = process.env.FULLNET_E2E_H5_ORIGIN;
const directory = process.env.FULLNET_E2E_FILES_DIRECTORY;
if (!origin || !directory || !process.env.FULLNET_E2E_API_URL) throw new Error('Workflow H5 requires its owned real fixture.');
export default defineConfig({
  testDir: './tests-real', workers: 1, retries: 0, fullyParallel: false,
  reporter: [['line'], ['json', { outputFile: process.env.FULLNET_E2E_L3_JSON_REPORT }]],
  outputDir: path.join(directory, 'browser'),
  use: { baseURL: origin, channel: 'msedge', trace: 'retain-on-failure' },
  webServer: {
    command: `pnpm --dir ../../.. --filter @fullnet/uniapp exec uni -p h5 --host localhost --port ${new URL(origin).port} --strictPort`,
    url: origin, reuseExistingServer: false, stderr: 'pipe',
    env: { VITE_API_BASE_URL: process.env.FULLNET_E2E_API_URL, UNI_OUTPUT_DIR: path.join(directory, 'mobile-build') }
  },
  projects: [{ name: 'workflow-files-h5' }]
});
