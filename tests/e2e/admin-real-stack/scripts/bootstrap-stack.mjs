import { spawn } from 'node:child_process';
import { generateKeyPairSync } from 'node:crypto';
import {
  createWriteStream,
  mkdirSync,
  mkdtempSync,
  rmSync,
  writeFileSync
} from 'node:fs';
import { tmpdir } from 'node:os';
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

/** 真实栈启动 Profile：development 含 Development Seed；production-totp 走 Production + TOTP 强认证。 */
function resolveStackProfile() {
  return process.env.FULLNET_E2E_STACK_PROFILE ?? 'development';
}

function createProductionSigningKeyEnv(keyId = 'e2eprodsigning') {
  const { publicKey, privateKey } = generateKeyPairSync('rsa', {
    modulusLength: 2048,
    publicKeyEncoding: { type: 'spki', format: 'pem' },
    privateKeyEncoding: { type: 'pkcs8', format: 'pem' }
  });

  return {
    Identity__ActiveKeyId: keyId,
    [`Identity__SigningKeys__${keyId}__PublicKeyPem`]: publicKey,
    [`Identity__SigningKeys__${keyId}__PrivateKeyPem`]: privateKey
  };
}

async function startRedisContainer() {
  const container = await new GenericContainer('redis:8.6')
    .withExposedPorts(6379)
    .withWaitStrategy(Wait.forListeningPorts())
    .start();

  const connectionString = `${container.getHost()}:${container.getMappedPort(6379)}`;
  return { container, connectionString };
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

  const stackProfile = resolveStackProfile();
  const databaseProvider = resolveDatabaseProvider();
  const { container, connectionString } = await startDatabaseContainer(databaseProvider);
  const { container: redisContainer, connectionString: redisConnectionString } =
    await startRedisContainer();
  const codeGenerationWorkspaceRoot = mkdtempSync(path.join(
    tmpdir(),
    'fullnet-codegeneration-e2e-'
  ));

  const isProductionTotp = stackProfile === 'production-totp';
  const sharedEnv = {
    ...withoutTestScenarioHostConfiguration(process.env),
    Database__Provider: databaseProvider,
    Database__ConnectionString: connectionString,
    Database__MySqlGuidStorageMode: 'Binary16',
    Cache__RedisConnectionString: redisConnectionString,
    Realtime__RedisBackplaneConnectionString: redisConnectionString,
    Realtime__AllowSharedRedisInDevelopment: 'true',
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
    Identity__AllowedOrigins__0: 'http://localhost:25173',
    Identity__AllowedOrigins__1: 'http://localhost:25174',
    Identity__LoginRateLimitPermitLimitPerMinute: '240',
    Identity__SessionMutationRateLimitPermitLimitPerMinute: '240',
    Tenancy__HostDomains__0: 'localhost',
    Realtime__Enabled: 'true',
    Realtime__HubPath: '/hubs/notifications',
    OutboxWorker__PollMilliseconds: '100',
    CodeGeneration__Apply__Enabled: 'true',
    CodeGeneration__Apply__WorkspaceRoot: codeGenerationWorkspaceRoot,
    DOTNET_ENVIRONMENT: isProductionTotp ? 'Production' : 'Development',
    ASPNETCORE_ENVIRONMENT: isProductionTotp ? 'Production' : 'Development',
    ...(isProductionTotp
      ? {
          ...createProductionSigningKeyEnv(),
          Identity__AllowDevelopmentEphemeralSigningKey: 'false',
          Identity__EnableTotpStrongReauthentication: 'true',
          Identity__EnableRemoteSuperAdministratorManagement: 'true',
          Files__Local__RootPath: path.join(repoRoot, '.tmp/e2e-real-stack-files')
        }
      : {
          Identity__AllowDevelopmentEphemeralSigningKey: 'true'
        })
  };

  await runDotnet([
    'run',
    '--project',
    'src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj',
    '--',
    '--seed',
    isProductionTotp ? 'baseline' : 'development'
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

  if (!isProductionTotp) {
    const viewerEnvironment = {
      ...process.env,
      FULLNET_E2E_API_URL: apiUrl
    };
    await provisionViewer(viewerEnvironment);
    // 第二次准备必须只复用同一角色和用户，防止测试重跑产生重复场景数据。
    await provisionViewer(viewerEnvironment);
  }

  // 真实栈保持 API/Worker 角色分离，确保浏览器场景经过事务 Outbox 和 Redis Backplane。
  const workerLogPath = path.join(repoRoot, '.tmp/e2e-real-stack/worker.log');
  mkdirSync(path.dirname(workerLogPath), { recursive: true });
  writeFileSync(workerLogPath, '');
  const workerLogStream = createWriteStream(workerLogPath, { flags: 'a' });
  const workerProcess = spawn(
    'dotnet',
    [
      'run',
      '--project',
      'src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj'
    ],
    {
      cwd: repoRoot,
      env: sharedEnv,
      stdio: 'pipe'
    }
  );
  workerProcess.stdout?.pipe(workerLogStream, { end: false });
  workerProcess.stderr?.pipe(workerLogStream, { end: false });

  activeStack = {
    apiUrl,
    apiProcess,
    workerProcess,
    workerLogStream,
    container,
    redisContainer,
    databaseProvider,
    stackProfile,
    redisConnectionString,
    codeGenerationWorkspaceRoot
  };
  writeFileSync(statePath, JSON.stringify({
    apiUrl,
    apiPid: apiProcess.pid,
    workerPid: workerProcess.pid,
    workerLogPath,
    containerId: container.getId(),
    redisContainerId: redisContainer.getId(),
    databaseProvider,
    stackProfile,
    redisConnectionString,
    codeGenerationWorkspaceRoot
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
  if (activeStack.workerProcess && !activeStack.workerProcess.killed) {
    activeStack.workerProcess.kill();
  }
  activeStack.workerLogStream?.end();

  await activeStack.container.stop();
  if (activeStack.redisContainer) {
    await activeStack.redisContainer.stop();
  }
  if (activeStack.codeGenerationWorkspaceRoot) {
    rmSync(activeStack.codeGenerationWorkspaceRoot, {
      recursive: true,
      force: true
    });
  }
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
