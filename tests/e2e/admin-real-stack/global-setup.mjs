import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { waitForApi } from './scripts/wait-for-api.mjs';

const repoRoot = path.resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const statePath = path.join(repoRoot, 'tests/e2e/admin-real-stack/.stack-state.json');

export default async function globalSetup() {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP === '1') {
    const apiUrl = process.env.FULLNET_E2E_API_URL;
    if (!apiUrl) {
      throw new Error(
        'FULLNET_E2E_SKIP_BOOTSTRAP=1 时必须提供 FULLNET_E2E_API_URL。'
      );
    }

    await waitForApi(apiUrl);
    process.env.FULLNET_E2E_API_URL = apiUrl;
    return;
  }

  if (!process.env.FULLNET_E2E_API_URL && !existsSync(statePath)) {
    const { bootstrapStack } = await import('./scripts/bootstrap-stack.mjs');
    await bootstrapStack();
  }

  const state = JSON.parse(readFileSync(statePath, 'utf8'));
  process.env.FULLNET_E2E_API_URL = state.apiUrl;
  await waitForApi(state.apiUrl);
}
