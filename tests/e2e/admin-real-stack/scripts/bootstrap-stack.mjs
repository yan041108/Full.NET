import { execFileSync, spawn } from 'node:child_process';
import { generateKeyPairSync } from 'node:crypto';
import {
  createWriteStream,
  existsSync,
  readFileSync,
  mkdirSync,
  mkdtempSync,
  rmSync,
  unlinkSync,
  writeFileSync
} from 'node:fs';
import { platform, tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { GenericContainer, Wait } from 'testcontainers';
import { createOidcStackEnv } from './oidc-stack-env.mjs';
import { provisionViewer } from './provision-viewer.mjs';
import { waitForApi } from './wait-for-api.mjs';

const repoRoot = path.resolve(fileURLToPath(new URL('../../../..', import.meta.url)));
const statePath = path.join(repoRoot, 'tests/e2e/admin-real-stack/.stack-state.json');
const sqlPassword = 'FullNet_Test!123';
const mysqlPassword = 'FullNet_Test!123';
const apiPort = Number.parseInt(process.env.FULLNET_E2E_API_PORT ?? '5149', 10);
// 使用 127.0.0.1 避免 Linux CI 上 localhost→::1 与 Kestrel 绑定不一致导致健康检查 fetch failed。
const apiUrl = `http://127.0.0.1:${apiPort}`;
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

/** Linux CI 子进程环境变量不宜携带多行 PEM；与 RsaSigningKeyRing.NormalizePem 的 \\n 约定一致。 */
function pemForProcessEnvironment(pem) {
  return pem.replace(/\r?\n/g, '\\n');
}

const productionDataProtectionPassword = 'FullNet-Test-Only!';

/** Production 门禁要求非临时目录 Key Ring 与加密证书；与集成测试 IdentityOidcMultiInstanceTestSupport 对齐。 */
function createProductionDataProtectionEnv(repoRoot) {
  const root = path.join(repoRoot, '.tmp', 'e2e-real-stack', 'dataprotection');
  const keyRingPath = path.join(root, 'keys');
  const certificatePath = path.join(root, 'active.pfx');
  mkdirSync(keyRingPath, { recursive: true });

  if (!existsSync(certificatePath)) {
    createSelfSignedPfxCertificate(certificatePath, productionDataProtectionPassword);
  }

  return {
    DataProtection__ApplicationName: 'Full.NET',
    DataProtection__KeyRingPath: keyRingPath,
    DataProtection__CertificatePath: certificatePath,
    DataProtection__CertificatePassword: productionDataProtectionPassword
  };
}

function createSelfSignedPfxCertificate(certificatePath, password) {
  if (platform() === 'win32') {
    const escapedPath = certificatePath.replace(/'/g, "''");
    const escapedPassword = password.replace(/'/g, "''");
    execFileSync(
      'powershell',
      [
        '-NoProfile',
        '-Command',
        `$pwd = ConvertTo-SecureString -String '${escapedPassword}' -Force -AsPlainText; `
          + `$cert = New-SelfSignedCertificate -Subject 'CN=Full.NET.DP.Active' `
          + `-CertStoreLocation 'Cert:\\CurrentUser\\My' -KeyExportPolicy Exportable `
          + `-NotAfter (Get-Date).AddYears(2); `
          + `Export-PfxCertificate -Cert $cert -FilePath '${escapedPath}' -Password $pwd | Out-Null; `
          + `Remove-Item $cert.PSPath`
      ],
      { stdio: 'pipe' }
    );
    return;
  }

  const root = path.dirname(certificatePath);
  const keyPath = path.join(root, 'active.key.pem');
  const certPath = path.join(root, 'active.cert.pem');
  execFileSync(
    'openssl',
    [
      'req',
      '-x509',
      '-newkey',
      'rsa:2048',
      '-keyout',
      keyPath,
      '-out',
      certPath,
      '-days',
      '825',
      '-nodes',
      '-subj',
      '/CN=Full.NET.DP.Active'
    ],
    { stdio: 'pipe' }
  );
  execFileSync(
    'openssl',
    [
      'pkcs12',
      '-export',
      '-out',
      certificatePath,
      '-inkey',
      keyPath,
      '-in',
      certPath,
      '-passout',
      `pass:${password}`
    ],
    { stdio: 'pipe' }
  );
  unlinkSync(keyPath);
  unlinkSync(certPath);
}

function createProductionSigningKeyEnv(keyId = 'e2eprodsigning') {
  const { publicKey, privateKey } = generateKeyPairSync('rsa', {
    modulusLength: 2048,
    publicKeyEncoding: { type: 'spki', format: 'pem' },
    privateKeyEncoding: { type: 'pkcs8', format: 'pem' }
  });

  return {
    Identity__ActiveKeyId: keyId,
    [`Identity__SigningKeys__${keyId}__PublicKeyPem`]: pemForProcessEnvironment(publicKey),
    [`Identity__SigningKeys__${keyId}__PrivateKeyPem`]: pemForProcessEnvironment(privateKey)
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
  const isProductionTotp = stackProfile === 'production-totp';
  const databaseProvider = resolveDatabaseProvider();
  const { container, connectionString } = await startDatabaseContainer(databaseProvider);
  const { container: redisContainer, connectionString: cacheRedisConnectionString } =
    await startRedisContainer();
  let realtimeRedisContainer = redisContainer;
  let realtimeRedisConnectionString = cacheRedisConnectionString;
  // Production 门禁禁止 Cache 与 Realtime Backplane 共用同一 Redis；development 栈仍允许共用。
  if (isProductionTotp) {
    const realtimeRedis = await startRedisContainer();
    realtimeRedisContainer = realtimeRedis.container;
    realtimeRedisConnectionString = realtimeRedis.connectionString;
  }
  const codeGenerationWorkspaceRoot = mkdtempSync(path.join(
    tmpdir(),
    'fullnet-codegeneration-e2e-'
  ));
  const observabilityLogRoot = mkdtempSync(path.join(
    tmpdir(),
    'fullnet-observability-e2e-'
  ));
  writeFileSync(
    path.join(observabilityLogRoot, 'e2e-observability.log'),
    'fullnet-observability-real-stack-start\nfullnet-observability-real-stack-marker\n'
  );

  const sharedEnv = {
    ...withoutTestScenarioHostConfiguration(process.env),
    Database__Provider: databaseProvider,
    Database__ConnectionString: connectionString,
    Database__MySqlGuidStorageMode: 'Binary16',
    Cache__RedisConnectionString: cacheRedisConnectionString,
    Realtime__RedisBackplaneConnectionString: realtimeRedisConnectionString,
    Realtime__AllowSharedRedisInDevelopment: isProductionTotp ? 'false' : 'true',
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
    Identity__AllowedOrigins__2: 'http://localhost:5173',
    Identity__AllowedOrigins__3: 'http://localhost:5174',
    Identity__AllowedOrigins__4: 'http://127.0.0.1:5173',
    Identity__AllowedOrigins__5: 'http://127.0.0.1:5174',
    Identity__AllowedOrigins__6: 'http://localhost:5175',
    Identity__LoginRateLimitPermitLimitPerMinute: '240',
    Identity__SessionMutationRateLimitPermitLimitPerMinute: '240',
    Tenancy__HostDomains__0: 'localhost',
    Tenancy__HostDomains__1: '127.0.0.1',
    Realtime__Enabled: 'true',
    Realtime__HubPath: '/hubs/notifications',
    OutboxWorker__PollMilliseconds: '100',
    CodeGeneration__Apply__Enabled: 'true',
    CodeGeneration__Apply__WorkspaceRoot: codeGenerationWorkspaceRoot,
    FullNet__ObservabilityAdmin__LogRootPath: observabilityLogRoot,
    DOTNET_ENVIRONMENT: isProductionTotp ? 'Production' : 'Development',
    ASPNETCORE_ENVIRONMENT: isProductionTotp ? 'Production' : 'Development',
    ...(isProductionTotp
      ? {
          ...createProductionDataProtectionEnv(repoRoot),
          ...createProductionSigningKeyEnv(),
          Identity__Oidc__Enable: 'false',
          Identity__AllowDevelopmentEphemeralSigningKey: 'false',
          Identity__EnableTotpStrongReauthentication: 'true',
          Identity__EnableRemoteSuperAdministratorManagement: 'true',
          Files__Local__RootPath: path.join(repoRoot, '.tmp/e2e-real-stack-files'),
          // 此套件只验证 TOTP；提供测试专用对象存储配置以通过 Production 启动校验。
          Files__Storage__DefaultProviderKey: 's3',
          Files__S3__BucketName: 'fullnet-e2e-only',
          Files__S3__Region: 'us-east-1',
          Files__S3__AccessKeyId: 'fullnet-e2e-only',
          Files__S3__SecretAccessKey: 'fullnet-e2e-only',
          Files__Oss__BucketName: 'fullnet-e2e-only',
          Files__Oss__Endpoint: 'oss-cn-hangzhou.aliyuncs.com',
          Files__Oss__AccessKeyId: 'fullnet-e2e-only',
          Files__Oss__AccessKeySecret: 'fullnet-e2e-only'
        }
      : {
          ...createOidcStackEnv(apiUrl),
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

  const apiProjectPath = path.join(
    repoRoot,
    'src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj'
  );
  const apiProjectDirectory = path.dirname(apiProjectPath);
  await runDotnet(['build', apiProjectPath], sharedEnv);
  const apiAssemblyPath = path.join(
    repoRoot,
    'src/Hosts/Full.NET.Host.Api/bin/Debug/net10.0/Full.NET.Host.Api.dll'
  );

  const apiLogPath = path.join(repoRoot, '.tmp/e2e-real-stack/api.log');
  mkdirSync(path.dirname(apiLogPath), { recursive: true });
  writeFileSync(apiLogPath, '');
  const apiLogStream = createWriteStream(apiLogPath, { flags: 'a' });
  const apiProcess = spawn(
    'dotnet',
    [apiAssemblyPath],
    {
      cwd: apiProjectDirectory,
      env: {
        ...sharedEnv,
        ASPNETCORE_URLS: apiUrl,
        Identity__EnableRemoteSuperAdministratorManagement: 'true'
      },
      stdio: 'pipe'
    }
  );
  apiProcess.stdout?.pipe(apiLogStream, { end: false });
  apiProcess.stderr?.pipe(apiLogStream, { end: false });

  const apiExit = new Promise((resolve, reject) => {
    apiProcess.once('error', reject);
    apiProcess.once('exit', (code, signal) => {
      resolve({ code, signal });
    });
  });
  const apiReady = waitForApi(apiUrl, 120_000, apiLogPath);
  const earlyExit = await Promise.race([
    apiReady.then(() => null),
    apiExit.then(exit => exit)
  ]);
  if (earlyExit) {
    let logTail = '';
    try {
      const text = readFileSync(apiLogPath, 'utf8').trim();
      if (text) {
        logTail = `\n${text.split(/\r?\n/).slice(-40).join('\n')}`;
      }
    } catch {
      // 忽略日志读取失败，保留退出码信息。
    }

    throw new Error(
      `Host.Api 在健康检查前退出（code=${earlyExit.code ?? 'null'}, signal=${earlyExit.signal ?? 'null'}）。${logTail}`
    );
  }

  await apiReady;

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
  const workerProjectPath = path.join(
    repoRoot,
    'src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj'
  );
  const workerProjectDirectory = path.dirname(workerProjectPath);
  await runDotnet(['build', workerProjectPath], sharedEnv);
  const workerAssemblyPath = path.join(
    repoRoot,
    'src/Hosts/Full.NET.Host.Worker/bin/Debug/net10.0/Full.NET.Host.Worker.dll'
  );
  const workerLogPath = path.join(repoRoot, '.tmp/e2e-real-stack/worker.log');
  mkdirSync(path.dirname(workerLogPath), { recursive: true });
  writeFileSync(workerLogPath, '');
  const workerLogStream = createWriteStream(workerLogPath, { flags: 'a' });
  const workerProcess = spawn(
    'dotnet',
    [workerAssemblyPath],
    {
      cwd: workerProjectDirectory,
      env: sharedEnv,
      stdio: 'pipe'
    }
  );
  workerProcess.stdout?.pipe(workerLogStream, { end: false });
  workerProcess.stderr?.pipe(workerLogStream, { end: false });

  activeStack = {
    apiUrl,
    apiProcess,
    apiLogStream,
    workerProcess,
    workerLogStream,
    container,
    redisContainer,
    realtimeRedisContainer,
    databaseProvider,
    stackProfile,
    cacheRedisConnectionString,
    realtimeRedisConnectionString,
    codeGenerationWorkspaceRoot,
    observabilityLogRoot
  };
  writeFileSync(statePath, JSON.stringify({
    apiUrl,
    apiPid: apiProcess.pid,
    apiLogPath,
    workerPid: workerProcess.pid,
    workerLogPath,
    containerId: container.getId(),
    redisContainerId: redisContainer.getId(),
    realtimeRedisContainerId: realtimeRedisContainer.getId(),
    databaseProvider,
    stackProfile,
    cacheRedisConnectionString,
    realtimeRedisConnectionString,
    codeGenerationWorkspaceRoot,
    observabilityLogRoot
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
  activeStack.apiLogStream?.end();

  await activeStack.container.stop();
  if (activeStack.redisContainer) {
    await activeStack.redisContainer.stop();
  }
  if (
    activeStack.realtimeRedisContainer
    && activeStack.realtimeRedisContainer !== activeStack.redisContainer
  ) {
    await activeStack.realtimeRedisContainer.stop();
  }
  if (activeStack.codeGenerationWorkspaceRoot) {
    rmSync(activeStack.codeGenerationWorkspaceRoot, {
      recursive: true,
      force: true
    });
  }
  if (activeStack.observabilityLogRoot) {
    rmSync(activeStack.observabilityLogRoot, {
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
