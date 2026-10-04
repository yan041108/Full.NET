import { spawn, spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { copyFileSync, createWriteStream, existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildAppTemplate } from '../../../scripts/templates/build-app-template.mjs';
import { createApp } from '../../../scripts/templates/create-app.mjs';
import { prepareApplicationCompositionProbe } from './application-composition-probe.mjs';
import { verifyApplicationModuleEndpoint } from './application-module-http.mjs';
import { cleanupCreatedApp } from './created-app-cleanup.mjs';
import { verifyApplicationCrudGeneration } from './application-crud-generation.mjs';
import { prepareApplicationBusinessMigrations, prepareApplicationBusinessSchemaUpgrade, verifyApplicationMigrationResult } from './application-business-migrations.mjs';
import { verifyApplicationCrudSchemaSourceUpgrade } from './application-crud-schema-source-upgrade.mjs';
import { createPreUpgradeProduct, renewTenantSession, verifyUpgradedProductHttp } from './application-crud-live-upgrade.mjs';
import { verifyApplicationCrudModule } from './application-crud-module.mjs';
import { verifyApplicationCrudHostWiring } from './application-crud-host-wiring.mjs';
import { verifyApplicationCrudAuthorization } from './application-crud-authorization.mjs';
import { verifyApplicationCrudHttpDenial } from './application-crud-http-denial.mjs';
import { verifyApplicationCrudTenantHttp } from './application-crud-tenant-http.mjs';
import { verifyApplicationCrudTenantIsolation } from './application-crud-tenant-isolation.mjs';
import { verifyApplicationCrudOpenApi } from './application-crud-openapi.mjs';
import { verifyApplicationCrudClient, verifyApplicationCrudClientRuntime, verifyApplicationCrudClientTenantRead, verifyApplicationCrudClientProductRead, verifyApplicationCrudClientProductList, verifyApplicationCrudClientTenantWrites } from './application-crud-client.mjs';
import { verifyApplicationCrudVue } from './application-crud-vue.mjs';
import { startApplicationCrudBrowser } from './application-crud-browser.mjs';
import { verifyApplicationCrudReadPermission, verifyApplicationCrudNoPermission, verifyApplicationCrudCreatePermission, verifyApplicationCrudUpdatePermission, verifyApplicationCrudDeletePermission } from './application-crud-read-permission.mjs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

const repoRoot = resolve(fileURLToPath(new URL('../../../', import.meta.url)));
const requireFromRealStack = createRequire(join(repoRoot, 'tests/e2e/admin-real-stack/package.json'));
const { GenericContainer, Wait } = requireFromRealStack('testcontainers');
const waitForApi = (await import('../../e2e/admin-real-stack/scripts/wait-for-api.mjs')).waitForApi;

const sqlPassword = 'FullNet_Test!123';
const mysqlPassword = 'FullNet_Test!123';
const adminPassword = 'FullNet!2026Secure';
const apiPort = 5199;
const apiUrl = `http://127.0.0.1:${apiPort}`;

export function shouldSkipRealStack() {
  if (process.env.FULLNET_SKIP_TESTCONTAINERS === '1') {
    return true;
  }
  if (process.env.FULLNET_RUN_TEMPLATE_REAL_STACK === '1') {
    return false;
  }
  return process.env.CI !== 'true' && process.env.CI !== '1';
}

async function startDatabaseContainer(provider) {
  if (provider === 'mysql') {
    const mysqlImage = process.env.CI ? 'mysql:8.4' : process.env.FULLNET_TEMPLATE_TEST_MYSQL_IMAGE ?? 'mysql:8.4';
    const container = await new GenericContainer(mysqlImage)
      .withEnvironment({
        MYSQL_ROOT_PASSWORD: mysqlPassword,
        MYSQL_DATABASE: 'fullnet_app',
      })
      .withExposedPorts(3306)
      .withWaitStrategy(Wait.forListeningPorts())
      .start();
    const connectionString = [
      `Server=${container.getHost()};Port=${container.getMappedPort(3306)}`,
      'Database=fullnet_app',
      `User ID=root;Password=${mysqlPassword}`,
      'Allow User Variables=true',
    ].join(';');
    return { container, connectionString, databaseProvider: 'MySql' };
  }

  const container = await new GenericContainer('mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04')
    .withEnvironment({ ACCEPT_EULA: 'Y', MSSQL_SA_PASSWORD: sqlPassword })
    .withExposedPorts(1433)
    .withWaitStrategy(Wait.forListeningPorts())
    .start();
  const connectionString = [
    `Server=${container.getHost()},${container.getMappedPort(1433)}`,
    'User Id=sa',
    `Password=${sqlPassword}`,
    'TrustServerCertificate=True',
  ].join(';');
  return { container, connectionString, databaseProvider: 'SqlServer' };
}

async function startRedisContainer() {
  // 本地 Docker Hub 不可用时可使用已有镜像；CI 始终验证固定版本。
  const redisImage = process.env.CI ? 'redis:7.4-alpine' : process.env.FULLNET_TEMPLATE_TEST_REDIS_IMAGE ?? 'redis:7.4-alpine';
  const container = await new GenericContainer(redisImage)
    .withExposedPorts(6379)
    .withWaitStrategy(Wait.forListeningPorts())
    .start();
  return {
    container,
    connectionString: `localhost:${container.getMappedPort(6379)}`,
  };
}

function runDotnet(args, cwd, env, timeoutMs = 300_000, logPath) {
  const result = spawnSync('dotnet', args, {
    cwd,
    encoding: 'utf8',
    timeout: timeoutMs,
    env: { ...process.env, ...env },
    windowsHide: true,
  });
  if (logPath) writeFileSync(logPath, `${result.stdout ?? ''}\n${result.stderr ?? ''}`);
  if (result.status !== 0) {
    // Migrator 的稳定错误码写 stderr，详细原因写 stdout；两者都必须保留供远端定位。
    throw new Error(`dotnet ${args.join(' ')} failed: ${result.stderr ?? ''}\n${(result.stdout ?? '').slice(-16000)}\n${result.error?.message ?? ''}`);
  }
  return result;
}

function runOutboxProbe(project, appRoot, env, command, ...argumentsForProbe) {
  const result = runDotnet(['run', '--project', project, '-c', 'Release', '--no-build', '--',
    command, ...argumentsForProbe], appRoot, env, 60_000);
  const marker = result.stdout.split(/\r?\n/u).find(line => line.startsWith('OUTBOX_PROBE '));
  assert.ok(marker, `Outbox probe ${command} returned no state: ${result.stdout}`);
  return JSON.parse(marker.slice('OUTBOX_PROBE '.length));
}

function buildSharedEnv(connectionString, databaseProvider, redisConnectionString) {
  return {
    Database__Provider: databaseProvider,
    Database__ConnectionString: connectionString,
    Database__MySqlGuidStorageMode: 'Binary16',
    ConnectionStrings__app: connectionString,
    Cache__RedisConnectionString: redisConnectionString,
    Realtime__RedisBackplaneConnectionString: redisConnectionString,
    Realtime__AllowSharedRedisInDevelopment: 'true',
    RateLimiting__EnableGlobalApiLimit: 'false',
    UuidBinaryContract__MaintenanceMode: 'true',
    UuidBinaryContract__BackupVerified: 'true',
    UuidBinaryContract__LegacyWritersStopped: 'true',
    UuidBinaryContract__DestructiveDdlApprovalId: 'created-app-real-stack-009',
    PreV1NamingContract__MaintenanceMode: 'true',
    PreV1NamingContract__BackupVerified: 'true',
    PreV1NamingContract__LegacyWritersStopped: 'true',
    PreV1NamingContract__LegacyOutboxDrained: 'true',
    PreV1NamingContract__DestructiveDdlApprovalId: 'created-app-real-stack-011',
    Identity__Bootstrap__Username: 'admin',
    Identity__Bootstrap__Password: adminPassword,
    // 五种普通账号各走一次 API 改密前登录和一次浏览器登录，保持有界限流并容纳该验收流量。
    Identity__LoginRateLimitPermitLimitPerMinute: '20',
    Identity__AllowedOrigins__0: 'http://localhost',
    Identity__AllowedOrigins__1: 'http://127.0.0.1',
    Identity__AllowedOrigins__2: 'http://localhost:25183',
    Identity__AllowDevelopmentEphemeralSigningKey: 'true',
    Tenancy__HostDomains__0: 'localhost',
    Tenancy__HostDomains__1: '127.0.0.1',
    FullNet__FrameworkManifest__ContentRoot: '.',
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_ENVIRONMENT: 'Development',
  };
}

function patchAppSettings(appRoot, provider, connectionString) {
  const settingsPath = join(appRoot, 'appsettings.json');
  const settings = JSON.parse(readFileSync(settingsPath, 'utf8'));
  settings.Database.Provider = provider === 'MySql' ? 'mysql' : 'sqlserver';
  settings.ConnectionStrings = { app: connectionString };
  settings.Kestrel = { Endpoints: { Http: { Url: apiUrl } } };
  writeFileSync(settingsPath, JSON.stringify(settings, null, 2) + '\n', 'utf8');
  const hostSettingsPath = join(appRoot, 'src', 'Demo.Host.Api', 'appsettings.json');
  if (existsSync(hostSettingsPath)) {
    writeFileSync(hostSettingsPath, JSON.stringify(settings, null, 2) + '\n', 'utf8');
  }
}

async function loginAndReadSettings(baseUrl) {
  const loginResponse = await fetch(`${baseUrl}/api/v1/auth/login`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Origin: 'http://localhost',
    },
    body: JSON.stringify({ username: 'admin', password: adminPassword }),
  });
  if (!loginResponse.ok) {
    throw new Error(`login failed: ${loginResponse.status} ${await loginResponse.text()}`);
  }
  const loginBody = await loginResponse.json();
  const accessToken = loginBody.accessToken ?? loginBody.AccessToken;
  if (!accessToken) {
    throw new Error('login response missing accessToken');
  }
  const settingsResponse = await fetch(`${baseUrl}/api/v1/settings/dict-types?page=1&pageSize=5`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (!settingsResponse.ok) {
    throw new Error(`settings read failed: ${settingsResponse.status} ${await settingsResponse.text()}`);
  }
  // 在独立应用内执行真实写入、乐观锁更新和删除，避免只读冒烟掩盖装配缺口。
  const request = async (path, method, body, expectedStatus) => {
    const response = await fetch(`${baseUrl}/api/v1/settings/dict-types${path}`, {
      method,
      headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', Origin: 'http://localhost' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    assert.equal(response.status, expectedStatus, `${method} ${path}: ${text}`);
    return text ? JSON.parse(text) : null;
  };
  const created = await request('', 'POST', {
    code: 'created_app_crud_probe', name: 'Created application CRUD probe', description: null, displayOrder: 1,
  }, 201);
  const read = await request(`/${created.id}`, 'GET', undefined, 200);
  assert.equal(read.code, 'created_app_crud_probe');
  const updated = await request(`/${created.id}`, 'PUT', {
    name: 'Updated application CRUD probe', description: null, displayOrder: 2, version: read.version,
  }, 200);
  assert.equal(updated.name, 'Updated application CRUD probe');
  const disabled = await request(`/${created.id}/disable`, 'POST', undefined, 200);
  assert.equal(disabled.isActive, false);
  await request(`/${created.id}/delete`, 'POST', { version: disabled.version }, 204);
  await request(`/${created.id}`, 'GET', undefined, 404);
  return accessToken;
}

/**
 * @param {'sqlserver'|'mysql'} databaseProviderKey
 */
export async function verifyCreatedAppRealStack(databaseProviderKey) {
  // Windows 的 TEMP 可能是 8.3 短路径，Vite 以长路径校验文件允许列表。
  const workspace = mkdtempSync(join(realpathSync.native(tmpdir()), 'fullnet-created-app-rs-'));
  let dbContainer;
  let redisContainer;
  let apiProcess;
  let apiLogStream;
  let workerProcess;
  let workerLogStream;
  let browserRuntime;
  try {
    const { templateRoot } = buildAppTemplate({ output: join(workspace, 'package') });
    const appRoot = join(workspace, 'app');
    createApp({
      packageRoot: templateRoot,
      output: appRoot,
      name: 'Demo',
      ownerKey: 'acme',
      database: databaseProviderKey,
      preset: 'minimal',
      httpPort: apiPort,
    });

    const manifest = JSON.parse(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'));
    if (manifest.migrationInventory?.selectionStatus !== 'preset-minimal') {
      throw new Error('expected preset-minimal migration inventory');
    }

    const logRoot = join(repoRoot, '.tmp/template-real-stack', databaseProviderKey);
    mkdirSync(logRoot, { recursive: true });
    // 运行应用自带客户端工具；仅验证冻结基线零漂移，不冒充业务 Vue 接入或页面编译。
    const clientCheck = spawnSync(process.execPath, [join(appRoot, '.fullnet-tools/openapi/generate-fullnet-client.mjs'), '--check'],
      { cwd: workspace, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    writeFileSync(join(logRoot, 'application-client-tools.json'), JSON.stringify({ status: clientCheck.status,
      error: clientCheck.error?.message, stdout: clientCheck.stdout, stderr: clientCheck.stderr }, null, 2));
    assert.equal(clientCheck.error, undefined, 'application client tool process failed');
    assert.equal(clientCheck.status, 0, 'application client baseline drift: ' + clientCheck.stderr);
    const diagnosticInputs = ['fullnet-app.json', 'framework-manifest.json', 'appsettings.json',
      'src/Demo.Host.Api/appsettings.json', 'src/Demo.Host.Migrator/appsettings.json']
      .map((path) => [path, readFileSync(join(appRoot, path))]);
    // 必须运行分发应用自带的 CLI；原仓库的诊断成功不能证明应用路径和预设闭包正确。
    const diagnosis = runDotnet(['run', '--project',
      join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli'),
      '-c', 'Release', '--', 'diagnose', '--workspace', appRoot, '--profile', 'development'],
    appRoot, {}, 300_000, join(logRoot, 'diagnose.log'));
    for (const code of ['DIAG_SDK_OK', 'DIAG_WORKSPACE_OK', 'DIAG_APP_PROFILE_OK', 'DIAG_MODULE_CLOSURE_OK']) {
      assert.match(diagnosis.stdout, new RegExp(`${code} ok`));
    }
    for (const [path, before] of diagnosticInputs) {
      assert.deepEqual(readFileSync(join(appRoot, path)), before, `diagnose changed ${path}`);
    }
    const diagnosticCli = join(appRoot,
      'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
    // 分发应用未初始化 User Secrets 时，生产诊断必须报告缺连接，且不能改写应用文件。
    const productionDiagnosis = spawnSync('dotnet', [diagnosticCli, 'diagnose', '--workspace', appRoot,
      '--profile', 'production'], {
      cwd: appRoot, encoding: 'utf8', timeout: 60_000, windowsHide: true,
      env: { ...process.env, ConnectionStrings__app: '<your-connection>' },
    });
    assert.equal(productionDiagnosis.error, undefined, 'production diagnose process failed');
    assert.equal(productionDiagnosis.status, 1, productionDiagnosis.stderr || productionDiagnosis.stdout);
    assert.match(productionDiagnosis.stdout, /DIAG_CONNECTION_MISSING error/u);
    assert.doesNotMatch(productionDiagnosis.stdout + productionDiagnosis.stderr, /<your-connection>/u);
    for (const [path, before] of diagnosticInputs) {
      assert.deepEqual(readFileSync(join(appRoot, path)), before, `production diagnose changed ${path}`);
    }
    writeFileSync(join(logRoot, 'diagnose-production.json'), JSON.stringify({
      status: productionDiagnosis.status, connectionMissing: true, inputsUnchanged: true,
    }, null, 2));

    prepareApplicationCompositionProbe(appRoot);
    verifyApplicationCrudGeneration(appRoot, { reportDirectory: join(logRoot, 'application-crud') });
    verifyApplicationCrudModule(appRoot, { reportDirectory: join(logRoot, 'application-crud-module'), removeTestSqlComment: true });
    verifyApplicationCrudHostWiring(appRoot, { reportDirectory: join(logRoot, 'application-crud-host-wiring') });
    verifyApplicationCrudAuthorization(appRoot, { reportDirectory: join(logRoot, 'application-crud-authorization') });
    const adoption = prepareApplicationBusinessMigrations(appRoot);
    writeFileSync(join(logRoot, 'application-migration-adoption.json'), JSON.stringify(adoption, null, 2));
    const database = await startDatabaseContainer(databaseProviderKey);
    dbContainer = database.container;
    const redis = await startRedisContainer();
    redisContainer = redis.container;
    patchAppSettings(appRoot, database.databaseProvider, database.connectionString);
    const env = buildSharedEnv(database.connectionString, database.databaseProvider, redis.connectionString);

    const migratorProject = join(appRoot, 'src/Demo.Host.Migrator/Demo.Host.Migrator.csproj');
    const hostProject = join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj');
    const workerProject = join(appRoot, 'src/Demo.Host.Worker/Demo.Host.Worker.csproj');
    const outboxProbeRoot = join(appRoot, 'tests/created-app-outbox-probe');
    mkdirSync(outboxProbeRoot, { recursive: true });
    for (const file of ['CreatedAppOutboxProbe.csproj', 'Program.cs', 'BusinessOutboxProbe.cs', 'SchemaUpgradeProbe.cs']) {
      copyFileSync(join(repoRoot, 'tests/templates/support/created-app-outbox-probe', file), join(outboxProbeRoot, file));
    }
    const outboxProbeProject = join(outboxProbeRoot, 'CreatedAppOutboxProbe.csproj');
    runDotnet(['build', migratorProject, '-c', 'Release', '-v', 'quiet'], appRoot, env);
    runDotnet(['build', hostProject, '-c', 'Release', '-v', 'quiet'], appRoot, env);
    runDotnet(['build', workerProject, '-c', 'Release', '-v', 'quiet'], appRoot, env);
    runDotnet(['build', outboxProbeProject, '-c', 'Release', '-v', 'quiet'], appRoot, env);
    const firstMigration = runDotnet([
      'run', '--project', migratorProject, '-c', 'Release', '--no-build', '--',
      '--seed', 'development',
    ], appRoot, env, 600_000, join(logRoot, 'migrator.log'));
    const firstResult = verifyApplicationMigrationResult(firstMigration.stdout, true);
    const repeatMigration = runDotnet([
      'run', '--project', migratorProject, '-c', 'Release', '--no-build',
    ], appRoot, env, 600_000, join(logRoot, 'migrator-repeat.log'));
    const repeatResult = verifyApplicationMigrationResult(repeatMigration.stdout, false);
    writeFileSync(join(logRoot, 'application-migration-results.json'), JSON.stringify({ first: firstResult, repeat: repeatResult }, null, 2));

    const workerUrl = `http://127.0.0.1:${JSON.parse(readFileSync(join(appRoot, 'fullnet-app.json'), 'utf8')).workerHttpPort}`;
    const workerEnv = {
      ...env,
      ASPNETCORE_URLS: workerUrl,
      Kestrel__Endpoints__Http__Url: workerUrl,
      Messaging__Worker__Mode: 'LegacyPolling',
      OutboxWorker__PollMilliseconds: '100',
      OutboxWorker__MaximumIdlePollMilliseconds: '100',
    };
    const retirement = runDotnet([
      'run', '--project', workerProject, '-c', 'Release', '--no-build', '--',
      '--outbox-version-retirement-message-type', 'fullnet.tenancy.tenant.changed',
      '--outbox-version-retirement-schema-version', '1',
    ], appRoot, workerEnv, 180_000, join(logRoot, 'worker-retirement.log'));
    const retirementReport = retirement.stdout.split(/\r?\n/u)
      .map(line => { try { return JSON.parse(line); } catch { return null; } })
      .find(value => value?.code === 'outbox.version_retirement.safe');
    assert.ok(retirementReport, 'generated Worker did not query the migrated outbox: ' + retirement.stdout);
    assert.equal(retirementReport.pendingCount, 0);
    assert.equal(retirementReport.deadLetterCount, 0);

    const outboxMessage = runOutboxProbe(outboxProbeProject, appRoot, env, 'enqueue');
    const pendingOutbox = runOutboxProbe(outboxProbeProject, appRoot, env, 'state', outboxMessage.id);
    assert.equal(pendingOutbox.Attempts, 0);
    assert.equal(pendingOutbox.IsProcessed, 0);
    assert.equal(pendingOutbox.IsDeadLettered, 0);

    const workerLogPath = join(logRoot, 'worker.log');
    writeFileSync(workerLogPath, '');
    workerLogStream = createWriteStream(workerLogPath, { flags: 'a' });
    workerProcess = spawn('dotnet', ['run', '--project', workerProject, '-c', 'Release', '--no-build'], {
      cwd: appRoot,
      env: { ...process.env, ...workerEnv },
      stdio: 'pipe',
    });
    workerProcess.stdout?.pipe(workerLogStream, { end: false });
    workerProcess.stderr?.pipe(workerLogStream, { end: false });
    await waitForApi(workerUrl, 180_000, workerLogPath);
    const workerReady = await fetch(`${workerUrl}/health/ready`);
    assert.equal(workerReady.status, 200, `generated Worker readiness: ${await workerReady.text()}`);
    const deliveredOutbox = runOutboxProbe(outboxProbeProject, appRoot, env, 'wait', outboxMessage.id);
    writeFileSync(join(logRoot, 'worker-outbox-delivery.json'), JSON.stringify({ pendingOutbox, deliveredOutbox }, null, 2));
    assert.equal(deliveredOutbox.Attempts, 1);
    assert.equal(deliveredOutbox.IsProcessed, 1);
    assert.equal(deliveredOutbox.IsDeadLettered, 0);
    assert.equal(deliveredOutbox.DeadLetterReasonCode, null);
    assert.equal(deliveredOutbox.IsLeaseReleased, 1);
    assert.equal(deliveredOutbox.IsRetryCleared, 1);

    const apiLogPath = join(logRoot, 'api.log');
    writeFileSync(apiLogPath, '');
    apiLogStream = createWriteStream(apiLogPath, { flags: 'a' });
    apiProcess = spawn('dotnet', ['run', '--project', hostProject, '-c', 'Release', '--no-build'], {
      cwd: appRoot,
      env: { ...process.env, ...env, ASPNETCORE_URLS: apiUrl },
      stdio: 'pipe',
    });
    apiProcess.stdout?.pipe(apiLogStream, { end: false });
    apiProcess.stderr?.pipe(apiLogStream, { end: false });
    await waitForApi(apiUrl, 180_000, apiLogPath);
    await verifyApplicationModuleEndpoint(apiUrl, { logPath: join(logRoot, 'application-module-http.json') });
    await verifyApplicationCrudOpenApi(apiUrl, { expectedPath: join(appRoot, 'contracts/openapi/products.generated.openapi.json'),
      logPath: join(logRoot, 'application-crud-openapi.json') });
    verifyApplicationCrudClient(appRoot, { reportDirectory: join(logRoot, 'application-crud-client') });
    verifyApplicationCrudVue(appRoot, { reportDirectory: join(logRoot, 'application-crud-vue') });
    browserRuntime = await startApplicationCrudBrowser(appRoot, apiUrl, join(logRoot, 'application-crud-browser'));
    await verifyApplicationCrudClientRuntime(appRoot, apiUrl, { logPath: join(logRoot, 'application-crud-client/runtime.json') });
    let hostAccessToken = await loginAndReadSettings(apiUrl);
    await verifyApplicationCrudClientRuntime(appRoot, apiUrl, { hostAccessToken,
      logPath: join(logRoot, 'application-crud-client/host-runtime.json') });
    await verifyApplicationCrudHttpDenial(apiUrl, { hostAccessToken, logPath: join(logRoot, 'application-crud-http-denial.json') });
    const onTenantAccount = account => browserRuntime.verify(account);
    const readPermission = await verifyApplicationCrudReadPermission(apiUrl, { hostAccessToken, logPath: join(logRoot, 'application-crud-read-permission.json'), onTenantAccount });
    hostAccessToken = readPermission.hostAccessToken;
    const noPermission = await verifyApplicationCrudNoPermission(apiUrl, { hostAccessToken, logPath: join(logRoot, 'application-crud-no-permission.json'), onTenantAccount });
    hostAccessToken = noPermission.hostAccessToken;
    const createPermission = await verifyApplicationCrudCreatePermission(apiUrl, { hostAccessToken, logPath: join(logRoot, 'application-crud-create-permission.json'), onTenantAccount });
    hostAccessToken = createPermission.hostAccessToken;
    const updatePermission = await verifyApplicationCrudUpdatePermission(apiUrl, { hostAccessToken, logPath: join(logRoot, 'application-crud-update-permission.json'), onTenantAccount });
    hostAccessToken = updatePermission.hostAccessToken;
    const deletePermission = await verifyApplicationCrudDeletePermission(apiUrl, { hostAccessToken, logPath: join(logRoot, 'application-crud-delete-permission.json'), onTenantAccount });
    hostAccessToken = deletePermission.hostAccessToken;
    const tenantCrud = await verifyApplicationCrudTenantHttp(apiUrl, { hostAccessToken,
      logPath: join(logRoot, 'application-crud-tenant-http.json'),
      onCreatedProduct: async ({ tenantAccessToken, product }) => {
        await verifyApplicationCrudClientProductRead(appRoot, apiUrl,
          { tenantAccessToken, expectedProduct: product, logPath: join(logRoot, 'application-crud-client/product-read.json') });
        await verifyApplicationCrudClientProductList(appRoot, apiUrl,
          { tenantAccessToken, expectedProduct: product, logPath: join(logRoot, 'application-crud-client/product-list.json') });
      } });
    const unitName = 'Generated application Worker projection probe';
    const unitResponse = await fetch(`${apiUrl}/api/v1/organization/units/`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${tenantCrud.tenantAccessToken}`, Origin: 'http://localhost', 'Content-Type': 'application/json' },
      body: JSON.stringify({ parentId: null, code: `app-outbox-${Date.now().toString(36)}`, name: unitName, displayOrder: 10 }),
      signal: AbortSignal.timeout(15_000),
    });
    assert.equal(unitResponse.status, 201, unitResponse.status === 201
      ? 'generated application organization write' : `generated application organization write: ${await unitResponse.text()}`);
    const unit = await unitResponse.json();
    assert.match(unit.id, /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu);
    assert.equal(unit.name, unitName);
    assert.equal(unit.version, 1);
    const projectedUnit = runOutboxProbe(outboxProbeProject, appRoot, env, 'business-wait', tenantCrud.tenantId, unit.id);
    writeFileSync(join(logRoot, 'worker-business-outbox.json'), JSON.stringify(projectedUnit, null, 2));
    assert.equal(projectedUnit.TenantId, tenantCrud.tenantId);
    assert.equal(projectedUnit.UnitId, unit.id);
    assert.equal(projectedUnit.MessageType, 'fullnet.organization.unit.changed');
    assert.equal(projectedUnit.SchemaVersion, 1);
    assert.equal(projectedUnit.ContentType, 'application/x-memorypack');
    assert.equal(projectedUnit.PayloadName, unitName);
    assert.equal(projectedUnit.PayloadVersion, unit.version);
    assert.equal(projectedUnit.Attempts, 1);
    assert.equal(projectedUnit.IsProcessed, 1);
    assert.equal(projectedUnit.IsDeadLettered, 0);
    assert.equal(projectedUnit.DeadLetterReasonCode, null);
    assert.equal(projectedUnit.IsLeaseReleased, 1);
    assert.equal(projectedUnit.IsRetryCleared, 1);
    assert.equal(projectedUnit.ProjectionName, unitName);
    assert.equal(projectedUnit.ProjectionVersion, unit.version);
    assert.equal(projectedUnit.ProjectionIsActive, 1);
    await verifyApplicationCrudClientTenantRead(appRoot, apiUrl, { tenantAccessToken: tenantCrud.tenantAccessToken,
      logPath: join(logRoot, 'application-crud-client/tenant-read.json') });
    await verifyApplicationCrudClientTenantWrites(appRoot, apiUrl, { tenantAccessToken: tenantCrud.tenantAccessToken,
      expectedTenantId: tenantCrud.tenantId, logPath: join(logRoot, 'application-crud-client/tenant-writes.json') });
    await verifyApplicationCrudTenantIsolation(apiUrl, { localTenantId: tenantCrud.tenantId, initialAccessToken: tenantCrud.tenantAccessToken,
      logPath: join(logRoot, 'application-crud-tenant-isolation.json') });
    assert.equal(workerProcess.exitCode, null, 'generated Worker exited during application acceptance');
    assert.equal(workerProcess.signalCode, null, 'generated Worker received an exit signal during application acceptance');
    const finalWorkerReady = await fetch(`${workerUrl}/health/ready`);
    assert.equal(finalWorkerReady.status, 200, `generated Worker final readiness: ${await finalWorkerReady.text()}`);
    await stopLoggedProcess(workerProcess, workerLogStream);
    workerProcess = undefined;
    workerLogStream = undefined;
    const workerOutput = readFileSync(workerLogPath, 'utf8');
    for (const marker of ['Unhandled exception', 'Outbox polling iteration failed',
      'Outbox backlog sampling failed', 'Outbox retention iteration failed',
      'Failed Outbox message', 'Dead-lettered Outbox message']) {
      assert.ok(!workerOutput.includes(marker), `generated Worker logged ${marker}`);
    }
    for (const line of workerOutput.split(/\r?\n/u)) {
      let entry;
      try { entry = JSON.parse(line); } catch { continue; }
      assert.ok(!['Error', 'Fatal'].includes(entry?.['@l']), `generated Worker logged ${entry['@l']}: ${entry['@mt']}`);
    }

    const legacyProduct = await createPreUpgradeProduct(apiUrl, tenantCrud.tenantAccessToken,
      tenantCrud.tenantId, join(logRoot, 'application-crud-live-upgrade/legacy-create.json'));
    await stopLoggedProcess(apiProcess, apiLogStream);
    apiProcess = undefined;
    apiLogStream = undefined;
    const sourceUpgrade = verifyApplicationCrudSchemaSourceUpgrade(appRoot,
      { reportDirectory: join(logRoot, 'application-crud-live-upgrade/source') });
    const upgradedAdoption = prepareApplicationBusinessSchemaUpgrade(appRoot);
    assert.equal(upgradedAdoption.scriptsPerProvider, 2);
    runDotnet(['build', migratorProject, '-c', 'Release', '-v', 'quiet'], appRoot, env,
      300_000, join(logRoot, 'application-crud-live-upgrade/migrator-build.log'));
    const migrationArgs = ['run', '--project', migratorProject, '-c', 'Release', '--no-build'];
    const upgradeMigration = runDotnet(migrationArgs, appRoot, env, 600_000,
      join(logRoot, 'application-crud-live-upgrade/migrator-upgrade.log'));
    const upgraded = verifyApplicationMigrationResult(upgradeMigration.stdout, false, 1);
    const upgradeRepeat = runDotnet(migrationArgs, appRoot, env, 600_000,
      join(logRoot, 'application-crud-live-upgrade/migrator-repeat.log'));
    const repeated = verifyApplicationMigrationResult(upgradeRepeat.stdout, false);
    const unaccounted = runOutboxProbe(outboxProbeProject, appRoot, env, 'schema-upgrade-unaccount');
    assert.equal(unaccounted.removed, 1);
    assert.equal(unaccounted.commentRemoved, databaseProviderKey === 'sqlserver' ? 1 : 0,
      'recovery probe did not prepare the provider-specific partial DDL state');
    const recoveryMigration = runDotnet(migrationArgs, appRoot, env, 600_000,
      join(logRoot, 'application-crud-live-upgrade/migrator-recovery.log'));
    const recovered = verifyApplicationMigrationResult(recoveryMigration.stdout, false, 1);
    const restoredComment = runOutboxProbe(outboxProbeProject, appRoot, env, 'schema-upgrade-comment-state');
    assert.equal(restoredComment.present, 1, 'recovery did not restore the Description metadata comment');
    const recoveryRepeat = runDotnet(migrationArgs, appRoot, env, 600_000,
      join(logRoot, 'application-crud-live-upgrade/migrator-recovery-repeat.log'));
    const recoveryRepeated = verifyApplicationMigrationResult(recoveryRepeat.stdout, false);
    const upgradedApiLogPath = join(logRoot, 'application-crud-live-upgrade/api.log');
    apiLogStream = createWriteStream(upgradedApiLogPath, { flags: 'a' });
    apiProcess = spawn('dotnet', ['run', '--project', hostProject, '-c', 'Release', '--no-build'], {
      cwd: appRoot, env: { ...process.env, ...env, ASPNETCORE_URLS: apiUrl }, stdio: 'pipe',
    });
    apiProcess.stdout?.pipe(apiLogStream, { end: false });
    apiProcess.stderr?.pipe(apiLogStream, { end: false });
    await waitForApi(apiUrl, 180_000, upgradedApiLogPath);
    await verifyApplicationCrudOpenApi(apiUrl, { expectedPath: join(appRoot, 'contracts/openapi/products.generated.openapi.json'),
      logPath: join(logRoot, 'application-crud-live-upgrade/openapi.json') });
    const upgradedTenantToken = await renewTenantSession(apiUrl, adminPassword, tenantCrud.tenantId);
    const http = await verifyUpgradedProductHttp(apiUrl, upgradedTenantToken,
      tenantCrud.tenantId, legacyProduct, join(logRoot, 'application-crud-live-upgrade/http.json'));
    writeFileSync(join(logRoot, 'application-crud-live-upgrade/result.json'), JSON.stringify({
      sourceUpdated: sourceUpgrade.sourceUpdated, moduleUpdated: sourceUpgrade.moduleUpdated,
      databaseMigrationApplied: true, upgraded, repeated, unaccounted, recovered,
      restoredComment, recoveryRepeated, ...http,
    }, null, 2));
  } finally {
    try {
      await browserRuntime?.close();
    } finally {
      await cleanupCreatedApp({ apiProcess, apiLogStream, workerProcess, workerLogStream,
        dbContainer, redisContainer, workspace });
    }
  }
}
