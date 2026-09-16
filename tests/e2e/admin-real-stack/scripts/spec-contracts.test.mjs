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

test('Vue 真实栈服务器必须注入不含 unsafe-eval 的严格脚本 CSP', async () => {
  const configPath = path.resolve(import.meta.dirname, '../playwright.config.mjs');
  const viteConfigPath = path.resolve(import.meta.dirname, '../../../../ui/admin/vite.config.ts');
  const [playwrightConfig, viteConfig] = await Promise.all([
    readFile(configPath, 'utf8'),
    readFile(viteConfigPath, 'utf8')
  ]);

  assert.match(playwrightConfig, /VITE_STRICT_CSP:\s*'1'/u);
  assert.match(viteConfig, /script-src 'self'/u);
  assert.doesNotMatch(viteConfig, /unsafe-eval/u);
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
  assert.match(source, /BuildOrganizationUnitFilter/u);
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
  assert.match(source, /Full\.NET\.Host\.Api\.dll/u);
  assert.match(source, /Full\.NET\.Host\.Worker\.dll/u);
  assert.match(source, /FULLNET_E2E_API_PORT/u);
  assert.match(source, /apiLogPath/u);
  assert.match(source, /activeStack\.apiLogStream\?\.end\(\)/u);
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

test('代码生成工作区辅助函数必须读取真实栈根目录的状态文件', async () => {
  const helperPath = path.resolve(
    import.meta.dirname,
    '../tests/support/codegeneration-workspace.mjs'
  );
  const source = await readFile(helperPath, 'utf8');

  assert.match(source, /new URL\('\.\.\/\.\.\/\.stack-state\.json', import\.meta\.url\)/u);
});

test('运行日志真实栈必须使用隔离目录并在退出时清理', async () => {
  const bootstrapPath = path.resolve(
    import.meta.dirname,
    './bootstrap-stack.mjs'
  );
  const bootstrap = await readFile(bootstrapPath, 'utf8');

  assert.match(bootstrap, /fullnet-observability-e2e-/u);
  assert.match(bootstrap, /FullNet__ObservabilityAdmin__LogRootPath/u);
  assert.match(bootstrap, /e2e-observability\.log/u);
  assert.match(bootstrap, /fullnet-observability-real-stack-marker/u);
  assert.match(bootstrap, /rmSync\(activeStack\.observabilityLogRoot/u);
});

test('工作流真实栈必须覆盖 Host/Tenant 权限、并发与危险 Patch', async () => {
  const hostSpec = await readFile(
    path.resolve(import.meta.dirname, '../tests/workflow-approval.spec.mjs'),
    'utf8'
  );
  const tenantSpec = await readFile(
    path.resolve(import.meta.dirname, '../tests/workflow-approval-tenant.spec.mjs'),
    'utf8'
  );

  for (const source of [hostSpec, tenantSpec]) {
    assert.match(source, /workflow-todo-approve/u);
    assert.match(source, /workflow-todo-reject/u);
    assert.match(source, /toHaveCount\(0\)/u);
    assert.match(source, /authorization\.permission_denied/u);
    assert.match(source, /toBe\(409\)/u);
    assert.match(source, /assertDangerousPatchesReturn422/u);
  }

  assert.match(tenantSpec, /enterDevelopmentTenant/u);
  assert.match(tenantSpec, /loginTenantAdminAccessToken/u);
});

test('oidc-center 真实栈必须登记独立 Playwright 项目与根脚本入口', async () => {
  const playwrightConfig = await readFile(
    path.resolve(import.meta.dirname, '../playwright.config.mjs'),
    'utf8'
  );
  const rootPackage = await readFile(
    path.resolve(import.meta.dirname, '../../../../package.json'),
    'utf8'
  );
  const workspacePackage = await readFile(
    path.resolve(import.meta.dirname, '../package.json'),
    'utf8'
  );

  assert.match(playwrightConfig, /name:\s*'vue-admin-oidc-center'/u);
  assert.match(playwrightConfig, /VITE_IDENTITY_AUTH_MODE:\s*'oidc-center'/u);
  assert.match(playwrightConfig, /testMatch:\s*'\*\*\/admin-oidc-center\.spec\.mjs'/u);
  assert.match(rootPackage, /"test:e2e:real:oidc-center"/u);
  assert.match(workspacePackage, /"test:oidc-center"/u);
});

test('oidc-center 真实栈必须覆盖 §6 最小消费探针', async () => {
  const source = await readFile(
    path.resolve(import.meta.dirname, '../tests/admin-oidc-center.spec.mjs'),
    'utf8'
  );
  const fixturesSource = await readFile(
    path.resolve(import.meta.dirname, '../tests/support/admin-oidc-center-fixtures.mjs'),
    'utf8'
  );

  assert.match(source, /loginAdminViaOidcCenter/u);
  assert.match(source, /captureOidcAccessTokenFromOverviewProbe/u);
  assert.match(source, /expectOidcApiGetStatus/u);
  assert.match(source, /expectOidcApiPostStatus/u);
  assert.match(source, /createE2eHostPingJobDefinition/u);
  assert.match(source, /createE2eAiAgentModelConfig/u);
  assert.match(source, /createOidcCenterQueuedAgentRun/u);
  assert.match(fixturesSource, /expectRevokedOidcCenterAgentRunAccessRejected/u);
  assert.match(source, /\/api\/v1\/ai\/agent\/runs/u);
  assert.match(source, /可创建并读取排队 Agent Run/u);
  assert.match(source, /无法读取、取消、恢复或创建 Agent Run/u);
  assert.match(fixturesSource, /\/api\/v1\/ai\/agent\/runs\/\$\{runId\}\/resume/u);
  assert.match(source, /expectProtectedRoutesRedirectToOidcLogin/u);
  assert.match(fixturesSource, /OIDC_CENTER_PROTECTED_ROUTE_PROBES/u);
  assert.match(fixturesSource, /\/#\/workflow\/todos/u);
  assert.match(fixturesSource, /\/#\/jobs\/host-definitions/u);
  assert.match(source, /强制下线后无法直接访问受保护路由/u);
  assert.match(source, /强制下线后清理本地凭据、中心 Cookie 并拒绝 refresh token/u);
  assert.doesNotMatch(source, /waitForRequest\([^)]*\/api\/v1\/me/u);
  assert.match(source, /无法访问后台任务定义 API/u);
  assert.match(source, /无法访问后台任务执行历史 API/u);
  assert.match(source, /openTodoAndAct/u);
  assert.match(source, /\/api\/v1\/workflow\/todos\/mine/u);
  assert.match(source, /可访问工作流待办 API/u);
  assert.match(source, /切租户后 access token 仍可访问工作流待办 API/u);
  assert.match(source, /切租户后 access token 仍可访问 \/api\/v1\/ai\/agent-tools/u);
  assert.match(source, /切租户后 access token 仍可访问后台任务定义 API/u);
  assert.match(source, /切租户后 access token 仍可创建并读取排队 Agent Run/u);
  assert.match(source, /切租户并返回 Host 后 access token 仍可访问工作流待办 API/u);
  assert.match(source, /切租户并返回 Host 后 access token 仍可访问 \/api\/v1\/ai\/agent-tools/u);
  assert.match(source, /切租户并返回 Host 后 access token 仍可访问后台任务定义 API/u);
  assert.match(source, /Agent 工具/u);
  assert.match(source, /\/api\/v1\/ai\/agent-tools/u);
  assert.match(source, /无法访问 \/api\/v1\/ai\/agent-tools/u);
  assert.match(source, /host-jobs-action-trigger/u);
  assert.match(source, /expectOidcCenterLocalCredentialsCleared/u);
  assert.match(source, /expectOidcCenterTokensRejected/u);
  assert.match(source, /readOidcRefreshCredentialFromPage/u);
  assert.match(source, /expectStaleOidcRefreshCannotRestoreSession/u);
  assert.match(source, /应用退出后写回 refresh 凭据仍无法恢复会话/u);
  assert.match(source, /revokeCurrentOidcCenterSession/u);
  assert.match(source, /强制下线后 access token 无法访问 \/api\/v1\/ai\/agent-tools/u);
  assert.match(source, /强制下线后 access token 无法访问工作流待办 API/u);
  assert.match(source, /强制下线后 access token 无法访问后台任务定义 API/u);
  assert.match(source, /强制下线后 access token 无法访问后台任务执行历史 API/u);
  assert.match(source, /无法触发后台任务/u);
});

test('admin-oidc-center 真实栈用例数量与 T08 文档登记一致', async () => {
  const source = await readFile(
    path.resolve(import.meta.dirname, '../tests/admin-oidc-center.spec.mjs'),
    'utf8'
  );
  const count = (source.match(/^\s*test\(/gm) ?? []).length;
  assert.equal(count, 40);
});
