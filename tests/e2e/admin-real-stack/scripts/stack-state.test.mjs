import assert from 'node:assert/strict';
import test from 'node:test';
import { canReuseStackState } from './stack-state.mjs';

const expected = { workspaceRoot: '/workspace/owned', apiUrl: 'http://127.0.0.1:54159', databaseProvider: 'SqlServer', stackProfile: 'development' };
const state = { ...expected, apiPid: 101, workerPid: 102, stackInstanceId: 'owned-instance', codeGenerationWorkspaceRoot: '/tmp/codegen', observabilityLogRoot: '/tmp/logs' };
const alive = pid => pid === 101 || pid === 102;
const exists = () => true;
test('matching API and Worker state can be reused', () => assert.equal(canReuseStackState(state, expected, alive, exists), true));
for (const [name, patch] of Object.entries({
  'different checkout': { workspaceRoot: '/workspace/foreign' },
  'missing owner': { workspaceRoot: undefined },
  'different API port': { apiUrl: 'http://127.0.0.1:5149' },
  'dead API process': { apiPid: 999 },
  'dead Worker process': { workerPid: 999 },
  'wrong provider': { databaseProvider: 'MySql' },
  'wrong profile': { stackProfile: 'production-totp' },
  'missing directory': { observabilityLogRoot: undefined }
})) {
  test('rejects ' + name, () => assert.equal(canReuseStackState({ ...state, ...patch }, expected, alive, exists), false));
}
test('rejects missing directory on disk', () => assert.equal(canReuseStackState(state, expected, alive, () => false), false));
