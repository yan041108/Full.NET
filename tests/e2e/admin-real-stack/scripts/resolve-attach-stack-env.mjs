/**
 * 附着本机 Docker SQL/Redis 时解析推荐环境变量（不写密钥到仓库）。
 * 用法：node scripts/resolve-attach-stack-env.mjs
 * PowerShell：node scripts/resolve-attach-stack-env.mjs | ForEach-Object { Invoke-Expression $_ }
 */
import { execSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createOidcStackEnv } from './oidc-stack-env.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '../../../..');
const observabilityLogRoot = join(repoRoot, '.tmp/attach-e2e-observability');
mkdirSync(observabilityLogRoot, { recursive: true });
writeFileSync(
  join(observabilityLogRoot, 'e2e-observability.log'),
  'fullnet-observability-real-stack-start\nfullnet-observability-real-stack-marker\n'
);

const apiUrl = (process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149').replace(/\/$/, '');

function tryDockerSqlPassword(containerName = 'sql-hskdsqbw') {
  try {
    const out = execSync(`docker inspect ${containerName} --format "{{range .Config.Env}}{{println .}}{{end}}"`, {
      encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore']
    });
    const line = out.split('\n').find(l => l.startsWith('MSSQL_SA_PASSWORD='));
    return line ? line.slice('MSSQL_SA_PASSWORD='.length) : '';
  } catch {
    return '';
  }
}

const sqlPassword =
  process.env.FULLNET_E2E_SQL_PASSWORD ?? tryDockerSqlPassword() ?? 'FullNet_Test!123';
const sqlPort = process.env.FULLNET_E2E_SQL_PORT ?? '60099';
const redisPort = process.env.FULLNET_E2E_REDIS_PORT ?? '60100';

function tryDockerRedisPassword(containerName) {
  try {
    const out = execSync(`docker inspect ${containerName} --format "{{range .Config.Env}}{{println .}}{{end}}"`, {
      encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore']
    });
    const line = out.split('\n').find(l => l.startsWith('REDIS_PASSWORD='));
    return line ? line.slice('REDIS_PASSWORD='.length) : '';
  } catch {
    return '';
  }
}

const db = `Server=127.0.0.1,${sqlPort};User Id=sa;Password=${sqlPassword};TrustServerCertificate=True`;
let redis = `127.0.0.1:${redisPort},abortConnect=false`;
const legacyPassword = tryDockerRedisPassword('redis-hkatvkvr');
if (legacyPassword && redisPort === '60091') {
  redis = `127.0.0.1:60090,password=${legacyPassword},ssl=true,abortConnect=false`;
}

const lines = [
  `$env:Database__Provider='SqlServer'`,
  `$env:Database__ConnectionString='${db}'`,
  `$env:Cache__RedisConnectionString='${redis}'`,
  `$env:Realtime__RedisBackplaneConnectionString='${redis}'`,
  `$env:Realtime__AllowSharedRedisInDevelopment='true'`,
  `$env:FullNet__ObservabilityAdmin__LogRootPath='${observabilityLogRoot.replace(/\\/g, '/')}'`,
  `$env:Identity__LoginRateLimitPermitLimitPerMinute='240'`,
  `$env:Identity__SessionMutationRateLimitPermitLimitPerMinute='240'`,
  `$env:ASPNETCORE_ENVIRONMENT='Development'`
];

for (const [key, value] of Object.entries(createOidcStackEnv(apiUrl))) {
  lines.push(`$env:${key}='${value}'`);
}

console.log(lines.join('\n'));
console.log('\n# 附着 L3：另终端启动 Worker（同上 env）：');
console.log('# cd src/Hosts/Full.NET.Host.Worker && dotnet run');
console.log(
  '# 空库：dotnet run --project src/Hosts/Full.NET.Host.Migrator -- --seed development'
);
console.log(
  '#   + UuidBinaryContract__MaintenanceMode=true 等（见 bootstrap-stack.mjs sharedEnv）'
);
console.log(
  '# 推荐无 TLS Redis：docker run -d --name fullnet-l3-redis -p 127.0.0.1:60100:6379 redis:8.6'
);
console.log('# 受限账号用例：node scripts/provision-viewer.mjs');
