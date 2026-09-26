import { spawn, spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createWriteStream, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildAppTemplate } from '../../../scripts/templates/build-app-template.mjs';
import { createApp } from '../../../scripts/templates/create-app.mjs';

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
    const container = await new GenericContainer('mysql:8.4')
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
  const container = await new GenericContainer('redis:7.4-alpine')
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
    Identity__AllowedOrigins__0: 'http://localhost',
    Identity__AllowedOrigins__1: 'http://127.0.0.1',
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
}

/**
 * @param {'sqlserver'|'mysql'} databaseProviderKey
 */
export async function verifyCreatedAppRealStack(databaseProviderKey) {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-created-app-rs-'));
  let dbContainer;
  let redisContainer;
  let apiProcess;
  let apiLogStream;
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

    const database = await startDatabaseContainer(databaseProviderKey);
    dbContainer = database.container;
    const redis = await startRedisContainer();
    redisContainer = redis.container;
    patchAppSettings(appRoot, database.databaseProvider, database.connectionString);
    const env = buildSharedEnv(database.connectionString, database.databaseProvider, redis.connectionString);

    const migratorProject = join(appRoot, 'framework/fullnet/src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj');
    const hostProject = join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj');
    const logRoot = join(repoRoot, '.tmp/template-real-stack', databaseProviderKey);
    mkdirSync(logRoot, { recursive: true });
    runDotnet(['build', migratorProject, '-c', 'Release', '-v', 'quiet'], appRoot, env);
    runDotnet(['build', hostProject, '-c', 'Release', '-v', 'quiet'], appRoot, env);
    runDotnet([
      'run', '--project', migratorProject, '-c', 'Release', '--no-build', '--',
      '--seed', 'development',
    ], appRoot, env, 600_000, join(logRoot, 'migrator.log'));

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
    await loginAndReadSettings(apiUrl);
  } finally {
    if (apiProcess && !apiProcess.killed) {
      apiProcess.kill('SIGTERM');
    }
    apiLogStream?.end();
    await dbContainer?.stop().catch(() => {});
    await redisContainer?.stop().catch(() => {});
    rmSync(workspace, { recursive: true, force: true });
  }
}
