import path from 'node:path';

/** 复用前检查配置和进程；健康检查由调用方继续执行。 */
export function canReuseStackState(state, expected, isAlive, exists) {
  return Boolean(state
    && typeof state.workspaceRoot === 'string'
    && path.resolve(state.workspaceRoot) === path.resolve(expected.workspaceRoot)
    && state.apiUrl === expected.apiUrl
    && state.databaseProvider === expected.databaseProvider
    && state.stackProfile === expected.stackProfile
    && typeof state.codeGenerationWorkspaceRoot === 'string' && exists(state.codeGenerationWorkspaceRoot)
    && typeof state.observabilityLogRoot === 'string' && exists(state.observabilityLogRoot)
    && isAlive(state.apiPid) && isAlive(state.workerPid));
}
