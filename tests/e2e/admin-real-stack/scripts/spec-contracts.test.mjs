import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { findForbiddenSessionContextLocators } from './spec-contracts.mjs';

test('识别直接选择 Full.NET Host 隐藏文本的真实栈断言', () => {
  const violations = findForbiddenSessionContextLocators(
    "await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();"
  );

  assert.deepEqual(violations, [1]);
});

test('允许通过双端可见上下文辅助函数断言 Host 上下文', () => {
  const violations = findForbiddenSessionContextLocators(
    "await expectVisibleCurrentContext(page, 'Full.NET Host');"
  );

  assert.deepEqual(violations, []);
});

test('真实栈 spec 统一通过可见上下文辅助函数断言 Host 上下文', async () => {
  const testsDirectory = path.resolve(import.meta.dirname, '../tests');
  const specFiles = (await readdir(testsDirectory))
    .filter(fileName => fileName.endsWith('.spec.mjs'))
    .sort();
  const violations = [];

  for (const fileName of specFiles) {
    const source = await readFile(path.join(testsDirectory, fileName), 'utf8');
    for (const lineNumber of findForbiddenSessionContextLocators(source)) {
      violations.push(`${fileName}:${lineNumber}`);
    }
  }

  assert.deepEqual(
    violations,
    [],
    '请复用 expectVisibleCurrentContext，避免命中 Vue 隐藏选项文本。'
  );
});

test('代码生成真实栈必须验证受跟踪预览、历史刷新与无源码摘要', async () => {
  const specPath = path.resolve(
    import.meta.dirname,
    '../tests/host-code-generation-previews.spec.mjs'
  );
  const source = await readFile(specPath, 'utf8');

  assert.match(source, /\/api\/v1\/code-generation\/runs\/preview/u);
  assert.match(source, /tracked\.runId/u);
  assert.match(source, /page\.reload\(\)/u);
  assert.match(source, /not\.toHaveProperty\('schema'\)/u);
  assert.match(source, /not\.toHaveProperty\('content'\)/u);
  assert.match(source, /not\.toHaveProperty\('errorMessage'\)/u);
  assert.match(source, /toOrganizationOwnedExplicitSchema/u);
  assert.match(source, /IOrganizationOwnedEntityWriteAuthorizer/u);
});

test('职位真实栈场景必须覆盖机构与职级写入并从 API 回读', async () => {
  const specPath = path.resolve(
    import.meta.dirname,
    '../tests/host-org-positions.spec.mjs'
  );
  const source = await readFile(specPath, 'utf8');

  assert.match(source, /\/api\/v1\/organization\/positions\/\$\{position\.id\}\/unit/u);
  assert.match(
    source,
    /\/api\/v1\/organization\/positions\/\$\{position\.id\}\/position-level/u
  );
  assert.match(source, /positionLevelId/u);
  assert.match(source, /unitId/u);
});

test('职级目录真实栈场景必须覆盖创建更新禁用并从 API 回读', async () => {
  const specPath = path.resolve(
    import.meta.dirname,
    '../tests/host-org-position-levels.spec.mjs'
  );
  const source = await readFile(specPath, 'utf8');

  assert.match(source, /method\(\) === 'POST'/u);
  assert.match(source, /method\(\) === 'PUT'/u);
  assert.match(source, /\/disable/u);
  assert.match(source, /getPositionLevel/u);
  assert.match(source, /isActive/u);
});

test('用户职位真实栈场景必须覆盖分配设主取消并从 API 回读', async () => {
  const specPath = path.resolve(
    import.meta.dirname,
    '../tests/host-org-user-positions.spec.mjs'
  );
  const source = await readFile(specPath, 'utf8');

  assert.match(source, /method\(\) === 'POST'/u);
  assert.match(source, /method\(\) === 'PUT'/u);
  assert.match(source, /\/disable/u);
  assert.match(source, /getUserPosition/u);
  assert.match(source, /\/assignable-users/u);
  assert.doesNotMatch(source, /\/api\/v1\/me/u);
  assert.match(source, /isPrimary/u);
  assert.match(source, /isActive/u);
});

test('真实栈状态文件必须通过短时存活探测后才能复用', async () => {
  const setupPath = path.resolve(import.meta.dirname, '../global-setup.mjs');
  const source = await readFile(setupPath, 'utf8');

  assert.match(source, /await waitForApi\(existingState\.apiUrl, 5_000\)/u);
  assert.match(source, /catch \{\s+stateIsReusable = false;/u);
});

test('真实栈必须按生产角色分离启动并清理 Worker', async () => {
  const bootstrapPath = path.resolve(
    import.meta.dirname,
    './bootstrap-stack.mjs'
  );
  const source = await readFile(bootstrapPath, 'utf8');

  assert.match(
    source,
    /src\/Hosts\/Full\.NET\.Host\.Worker\/Full\.NET\.Host\.Worker\.csproj/u
  );
  assert.match(source, /workerProcess/u);
  assert.match(source, /workerPid:\s*workerProcess\.pid/u);
  assert.match(source, /workerLogPath/u);
  assert.match(source, /activeStack\.workerProcess\.kill\(\)/u);
  assert.match(
    source,
    /Realtime__RedisBackplaneConnectionString:\s*redisConnectionString/u,
    /Realtime__AllowSharedRedisInDevelopment:\s*'true'/u,
  );
});

test('真实栈复用前必须确认 Worker 进程仍存活', async () => {
  const setupPath = path.resolve(import.meta.dirname, '../global-setup.mjs');
  const source = await readFile(setupPath, 'utf8');

  assert.match(source, /isProcessAlive\(existingState\.workerPid\)/u);
});

test('代码生成真实栈必须使用临时工作区并验证双端确认 Apply', async () => {
  const bootstrapPath = path.resolve(
    import.meta.dirname,
    './bootstrap-stack.mjs'
  );
  const bootstrap = await readFile(bootstrapPath, 'utf8');
  assert.match(bootstrap, /mkdtempSync\(path\.join\(/u);
  assert.match(bootstrap, /CodeGeneration__Apply__Enabled:\s*'true'/u);
  assert.match(bootstrap, /CodeGeneration__Apply__WorkspaceRoot/u);
  assert.match(
    bootstrap,
    /rmSync\(activeStack\.codeGenerationWorkspaceRoot/u
  );

  const specPath = path.resolve(
    import.meta.dirname,
    '../tests/host-code-generation-templates.spec.mjs'
  );
  const spec = await readFile(specPath, 'utf8');
  assert.match(spec, /\/api\/v1\/code-generation\/runs\/apply/u);
  assert.match(spec, /\/api\/v1\/code-generation\/runs\/rollback/u);
  assert.match(spec, /\/api\/v1\/code-generation\/runs\/rollback-chain/u);
  assert.match(spec, /rollback-chain/u);
  assert.match(spec, /confirmRollback\(page, clientKind\)/u);
  assert.match(spec, /confirmApply\(page, clientKind\)/u);
  assert.match(spec, /runHistory\(view, clientKind\)/u);
  assert.match(spec, /readAppliedWorkspaceArtifact/u);
  assert.match(spec, /IOrganizationOwnedEntityWriteAuthorizer/u);
});
