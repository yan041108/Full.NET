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

function resolveStackProfile() {
  return process.env.FULLNET_E2E_STACK_PROFILE ?? 'development';
}

function readStackState() {
  if (!existsSync(statePath)) {
    return null;
  }

  return JSON.parse(readFileSync(statePath, 'utf8'));
}

function isProcessAlive(pid) {
  if (!Number.isSafeInteger(pid) || pid <= 0) {
    return false;
  }

  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
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

  const configuredApiUrl = process.env.FULLNET_E2E_API_URL;
  if (configuredApiUrl) {
    await waitForApi(configuredApiUrl);
    return;
  }

  const expectedProvider = resolveDatabaseProvider();
  const expectedProfile = resolveStackProfile();
  const existingState = readStackState();
  const providerMatches = existingState?.databaseProvider === expectedProvider;
  const profileMatches = existingState?.stackProfile === expectedProfile;
  const workspaceMatches = typeof existingState?.codeGenerationWorkspaceRoot === 'string'
    && existsSync(existingState.codeGenerationWorkspaceRoot);
  const observabilityLogRootMatches = typeof existingState?.observabilityLogRoot === 'string'
    && existsSync(existingState.observabilityLogRoot);
  let stateIsReusable = Boolean(
    existingState
    && providerMatches
    && profileMatches
    && workspaceMatches
    && observabilityLogRootMatches
  );

  if (stateIsReusable) {
    try {
      // 状态文件只描述上次启动结果；复用前必须确认 API 与独立 Worker 都仍然存活。
      if (!isProcessAlive(existingState.workerPid)) {
        throw new Error('真实栈 Worker 已退出。');
      }
      await waitForApi(existingState.apiUrl, 5_000);
      process.env.FULLNET_E2E_API_URL = existingState.apiUrl;
      return;
    } catch {
      stateIsReusable = false;
    }
  }

  if (!stateIsReusable) {
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
