import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { canReuseStackState } from './scripts/stack-state.mjs';
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

  try { return JSON.parse(readFileSync(statePath, 'utf8')); } catch { return null; }
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
    if (process.env.FULLNET_E2E_SKIP_VIEWER_PROVISION !== '1') {
      const { provisionViewer } = await import('./scripts/provision-viewer.mjs');
      await provisionViewer(process.env);
    }
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
  let stateIsReusable = canReuseStackState(existingState, {
    workspaceRoot: repoRoot,
    apiUrl: 'http://127.0.0.1:' + (process.env.FULLNET_E2E_API_PORT ?? '5149'),
    databaseProvider: expectedProvider,
    stackProfile: expectedProfile
  }, isProcessAlive, existsSync);

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
    // 未匹配的存活实例不归本次运行所有，不能覆盖状态后遗失其清理入口。
    if (existingState && (isProcessAlive(existingState.apiPid) || isProcessAlive(existingState.workerPid))) {
      throw new Error('A live real stack has mismatched configuration; use an isolated state workspace.');
    }
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
