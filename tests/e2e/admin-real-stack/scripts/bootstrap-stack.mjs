import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { GenericContainer, Wait } from 'testcontainers';
import { provisionViewer } from './provision-viewer.mjs';
import { waitForApi } from './wait-for-api.mjs';

const repoRoot = path.resolve(fileURLToPath(new URL('../../../..', import.meta.url)));
const statePath = path.join(repoRoot, 'tests/e2e/admin-real-stack/.stack-state.json');
const sqlPassword = 'FullNet_Test!123';
const mysqlPassword = 'FullNet_Test!123';
const apiPort = 5149;
const apiUrl = `http://localhost:${apiPort}`;
const adminPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const adminUsername = process.env.FULLNET_E2E_USERNAME ?? 'admin';

/** 由 global-teardown 调用的进程内栈引用，避免序列化 testcontainers 句柄。 */
let activeStack;

function resolveDatabaseProvider() {
  const value = (process.env.FULLNET_E2E_DATABASE_PROVIDER ?? 'SqlServer').toLowerCase();
  if (value === 'mysql') {
    return 'MySql';
  }

  return 'SqlServer';
}

async function startDatabaseContainer(provider) {
  if (provider === 'MySql') {
    const container = await new GenericContainer('mysql:8.0')
      .withEnvironment({
        MYSQL_DATABASE: 'fullnet',
        MYSQL_USER: 'fullnet',
        MYSQL_PASSWORD: mysqlPassword,
        MYSQL_ROOT_PASSWORD: mysqlPassword
      })
      .withCommand(['--log-bin-trust-function-creators=1'])
      .withExposedPorts(3306)
      .withWaitStrategy(Wait.forListeningPorts())
      .start();

    const connectionString = [
      `Server=${container.getHost()}`,
      `Port=${container.getMappedPort(3306)}`,
      'Database=fullnet',
      'User=fullnet',
      `Password=${mysqlPassword}`
    ].join(';');

    return { container, connectionString };
  }

  const container = await new GenericContainer(
    'mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04'
  )
    .withEnvironment({
      ACCEPT_EULA: 'Y',
      MSSQL_SA_PASSWORD: sqlPassword
    })
    .withExposedPorts(1433)
    .withWaitStrategy(Wait.forListeningPorts())
    .start();

  const connectionString = [
    `Server=${container.getHost()},${container.getMappedPort(1433)}`,
    'User Id=sa',
    `Password=${sqlPassword}`,
    'TrustServerCertificate=True'
  ].join(';');

  return { container, connectionString };
}

/**
 * 启动 Testcontainer、执行 Migrator Development Seed，并拉起 API Host。
 * 真实套件禁止 route mock；凭据通过环境变量覆盖。
 */
export async function bootstrapStack() {
  if (activeStack) {
    return activeStack;
  }

  const databaseProvider = resolveDatabaseProvider();
  const { container, connectionString } = await startDatabaseContainer(databaseProvider);

  const sharedEnv = {
    ...withoutTestScenarioHostConfiguration(process.env),
    Database__Provider: databaseProvider,
    Database__ConnectionString: connectionString,
    Database__MySqlGuidStorageMode: 'Binary16',
    UuidBinaryContract__MaintenanceMode: 'true',
    UuidBinaryContract__BackupVerified: 'true',
    UuidBinaryContract__LegacyWritersStopped: 'true',
    UuidBinaryContract__DestructiveDdlApprovalId: 'e2e-real-stack-009',
    PreV1NamingContract__MaintenanceMode: 'true',
    PreV1NamingContract__BackupVerified: 'true',
    PreV1NamingContract__LegacyWritersStopped: 'true',
    PreV1NamingContract__LegacyOutboxDrained: 'true',
    PreV1NamingContract__DestructiveDdlApprovalId: 'e2e-real-stack-011',
    Identity__Bootstrap__Username: adminUsername,
    Identity__Bootstrap__Password: adminPassword,
    Identity__AllowDevelopmentEphemeralSigningKey: 'true',
    Identity__AllowedOrigins__0: 'http://localhost:25173',
    Identity__AllowedOrigins__1: 'http://localhost:25174',
    Identity__LoginRateLimitPermitLimitPerMinute: '240',
    Identity__SessionMutationRateLimitPermitLimitPerMinute: '240',
    Tenancy__HostDomains__0: 'localhost',
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_ENVIRONMENT: 'Development'
  };

  await runDotnet([
    'run',
    '--project',
    'src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj',
    '--',
    '--seed',
    'development'
  ], sharedEnv);

  const apiProcess = spawn(
    'dotnet',
    [
      'run',
      '--project',
      'src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj',
      '--urls',
      apiUrl
    ],
    {
      cwd: repoRoot,
      env: {
        ...sharedEnv,
        Identity__EnableRemoteSuperAdministratorManagement: 'true'
      },
      stdio: 'pipe'
    }
  );

  await waitForApi(apiUrl);
  const viewerEnvironment = {
    ...process.env,
    FULLNET_E2E_API_URL: apiUrl
  };
  await provisionViewer(viewerEnvironment);
  // 第二次准备必须只复用同一角色和用户，防止测试重跑产生重复场景数据。
  await provisionViewer(viewerEnvironment);

  activeStack = {
    apiUrl,
    apiProcess,
    container,
    databaseProvider
  };
  writeFileSync(statePath, JSON.stringify({
    apiUrl,
    apiPid: apiProcess.pid,
    containerId: container.getId(),
    databaseProvider
  }, null, 2));

  return activeStack;
}

function withoutTestScenarioHostConfiguration(environment) {
  return Object.fromEntries(
    Object.entries(environment).filter(([key]) =>
      !key.toLowerCase().startsWith('identity__e2eviewer__')));
}

/** 停止 bootstrap 拉起的 API 与 Testcontainer。 */
export async function teardownStack() {
  if (!activeStack) {
    return;
  }

  if (activeStack.apiProcess && !activeStack.apiProcess.killed) {
    activeStack.apiProcess.kill();
  }

  await activeStack.container.stop();
  activeStack = undefined;
}

function runDotnet(args, env) {
  return new Promise((resolve, reject) => {
    const child = spawn('dotnet', args, {
      cwd: repoRoot,
      env,
      stdio: 'inherit'
    });
    child.on('error', reject);
    child.on('exit', code => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`dotnet ${args.join(' ')} 退出码 ${code}`));
    });
  });
}

const isDirectExecution = process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;

if (isDirectExecution) {
  await bootstrapStack();
  console.log(`Real stack ready at ${apiUrl}`);
}
