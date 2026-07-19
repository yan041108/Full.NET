import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { waitForApi } from './scripts/wait-for-api.mjs';

const repoRoot = path.resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const statePath = path.join(repoRoot, 'tests/e2e/admin-real-stack/.stack-state.json');

function resolveDatabaseProvider() {
  const value = (process.env.FULLNET_E2E_DATABASE_PROVIDER ?? 'SqlServer').toLowerCase();
  return value === 'mysql' ? 'MySql' : 'SqlServer';
}

function readStackState() {
  if (!existsSync(statePath)) {
    return null;
  }

  return JSON.parse(readFileSync(statePath, 'utf8'));
}

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

  const expectedProvider = resolveDatabaseProvider();
  const existingState = readStackState();
  const providerMatches = existingState?.databaseProvider === expectedProvider;

  if (!process.env.FULLNET_E2E_API_URL && (!existingState || !providerMatches)) {
    const { bootstrapStack } = await import('./scripts/bootstrap-stack.mjs');
    await bootstrapStack();
  }

  const state = readStackState();
  if (!state?.apiUrl) {
    throw new Error('真实栈状态文件缺失 apiUrl，无法继续 E2E。');
  }

  process.env.FULLNET_E2E_API_URL = state.apiUrl;
  await waitForApi(state.apiUrl);
}
