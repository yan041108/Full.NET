import { spawn, execFileSync } from 'node:child_process';
import { randomBytes, randomUUID, createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validatePublishWarnings } from '../../scripts/testing/api-native-aot-publish-warnings.mjs';

// 只管理本次新建的唯一命名容器/网络；不读取既有业务库，不清理用户资源或工作区文件。
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const output = path.join(root, 'artifacts/ai-evaluation/r03');
mkdirSync(output, { recursive: true });
const prefix = `fullnet-ai-r03-${randomUUID().slice(0, 8)}`;
const password = `R03!${randomBytes(20).toString('hex')}`;
const owned = [];
let networkCreated = false;
const images = {
  sqlserver: 'mcr.microsoft.com/mssql/server@sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74',
  mysql: 'mysql@sha256:7dcddc01f13bab2f15cde676d44d01f61fc9f99fe7785e86196dfc07d358ae2b',
  qdrant: 'qdrant/qdrant@sha256:12364fe851b9f17356fc88189fc06d1b521262e04659ec7345975b00c9246a10',
  sdk: 'fullnet-native-aot-publish-sdk@sha256:963c5de8c62d5c1cf3bf3c38ade4ef44fec541b7cbc735dbcec0ba9425ad0fc3'
};
const records = [];
let completedManifest = null;
const sourcePaths = ['tests/ai-evaluation/run-retrieval-probe.mjs', 'scripts/testing/api-native-aot-publish-warnings.mjs',
  ...['Full.NET.AiRetrieval.Probe.csproj', 'linux.packages.lock.json', 'Program.cs', 'ProbeContracts.cs', 'ProbeParser.cs', 'ProbeSql.cs', 'ProbeQdrant.cs', 'ProbeVectorSearch.cs'].map(x => `tests/ai-evaluation/probe/${x}`)];
function sourceHashes() {
  return Object.fromEntries(sourcePaths.map(file => [file,
    createHash('sha256').update(readFileSync(path.join(root, file), 'utf8').replaceAll('\r\n', '\n')).digest('hex')]));
}
const sourceSha256 = sourceHashes();
writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({ status: 'running', runId: prefix, startedAtUtc: new Date().toISOString() }, null, 2) + '\n');

async function run(args, { env = {}, timeout = 120000, allowFailure = false, log = null } = {}) {
  return await new Promise((resolve, reject) => {
    const child = spawn('docker', args, { cwd: root, env: { ...process.env, ...env }, windowsHide: true });
    let stdout = '', stderr = '';
    const timer = setTimeout(() => child.kill(), timeout);
    child.stdout.on('data', data => { stdout += data; });
    child.stderr.on('data', data => { stderr += data; });
    child.on('error', reject);
    child.on('close', code => {
      clearTimeout(timer);
      const safe = (stdout + stderr).replaceAll(password, '[redacted]');
      if (log) writeFileSync(path.join(output, log), safe);
      if (code !== 0 && !allowFailure) reject(new Error(`实验命令失败（exit=${code}）：${safe.slice(-3000)}`));
      else resolve({ code, stdout, stderr });
    });
  });
}

async function ready(name, command) {
  for (let attempt = 0; attempt < 90; attempt++) {
    const result = await run(['exec', name, 'sh', '-c', command], { allowFailure: true, timeout: 5000 });
    if (result.code === 0 && result.stdout.trim() === '1') return;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`${name} SQL readiness 未通过。`);
}

try {
  for (const [role, image] of Object.entries(images)) {
    const result = await run(['image', 'inspect', image, '--format', '{{.Id}}']);
    records.push({ role, image, imageId: result.stdout.trim() });
  }
  await run(['network', 'create', '--label', `fullnet.ai-probe.run=${prefix}`, prefix]);
  networkCreated = true;
  for (const role of ['sqlserver', 'mysql', 'qdrant']) {
    const name = `${prefix}-${role}`;
    owned.push(name);
    const options = role === 'sqlserver' ? ['-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_SA_PASSWORD']
      : role === 'mysql' ? ['-e', 'MYSQL_ROOT_PASSWORD'] : ['-p', '127.0.0.1::6333'];
    await run(['run', '-d', '--name', name, '--label', `fullnet.ai-probe.run=${prefix}`, '--network', prefix, '--network-alias', role,
      '--memory', role === 'sqlserver' ? '2g' : '1g', ...options, images[role]],
    { env: { MSSQL_SA_PASSWORD: password, MYSQL_ROOT_PASSWORD: password } });
  }
  console.log('等待新建实验数据库与 Qdrant 的真实 readiness。');
  await ready(`${prefix}-sqlserver`, 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -h -1 -W -Q "SET NOCOUNT ON; SELECT 1;"; else /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -h -1 -W -Q "SET NOCOUNT ON; SELECT 1;"; fi');
  await ready(`${prefix}-mysql`, 'export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"; mysql --protocol=TCP -h127.0.0.1 -uroot -N -e "SELECT 1"');
  const portResult = await run(['port', `${prefix}-qdrant`, '6333/tcp']);
  const port = portResult.stdout.trim().split(':').at(-1);
  let qdrantReady = false;
  for (let attempt = 0; attempt < 30; attempt++) {
    try { qdrantReady = (await fetch(`http://127.0.0.1:${port}/readyz`, { signal: AbortSignal.timeout(2000) })).ok; } catch { }
    if (qdrantReady) break;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  if (!qdrantReady) throw new Error('Qdrant readiness 未通过。');
  console.log('执行 linux-x64 Native AOT publish，保留完整编译日志。');
  const project = '/src/tests/ai-evaluation/probe/Full.NET.AiRetrieval.Probe.csproj';
  const cache = path.join(process.env.USERPROFILE || process.env.HOME, '.nuget/packages');
  const mounts = ['-v', `${root}:/src:ro`, '-v', `${output}:/evidence`, '-v', `${cache}:/root/.nuget/packages`];
  const props = '-p:UseArtifactsOutput=true -p:ArtifactsPath=/tmp/probe-build -p:NuGetLockFilePath=/evidence/linux.packages.lock.json';
  writeFileSync(path.join(output, 'linux.packages.lock.json'), readFileSync(path.join(root, 'tests/ai-evaluation/probe/linux.packages.lock.json')));
  const buildName = `${prefix}-build`;
  owned.push(buildName);
  const build = await run(['run', '--rm', '--name', buildName, '--label', `fullnet.ai-probe.run=${prefix}`, ...mounts, images.sdk, 'bash', '-c',
    `set -euo pipefail; dotnet --version; dotnet restore ${project} -r linux-x64 -p:PublishAot=true ${props} --locked-mode --nologo; dotnet publish ${project} -c Release -r linux-x64 -p:PublishAot=true -p:StripSymbols=true ${props} -o /evidence/native --no-restore --nologo`],
  { timeout: 900000, log: 'publish.log' });
  // 只复用 ADR-0008 的精确白名单，不扩展边界或抑制实验代码/PdfPig 的新告警。
  const publishOutput = build.stdout + build.stderr;
  const approvedWarnings = validatePublishWarnings(publishOutput);
  if (publishOutput.split(/\r?\n/).some(line => /\bwarning\b/i.test(line) && !/warning IL\d{4}:/.test(line)))
    throw new Error('Native AOT publish 存在其他告警，请检查 publish.log。');
  for (const mode of ['parser', 'sqlserver', 'mysql', 'qdrant']) {
    const name = `${prefix}-probe-${mode}`;
    owned.push(name);
    const connection = mode === 'sqlserver'
      ? `Server=sqlserver;User ID=sa;Password=${password};Encrypt=True;TrustServerCertificate=True;Connect Timeout=15`
      : `Server=mysql;User ID=root;Password=${password};SslMode=None;Connection Timeout=15`;
    // 仅实验自签名/无 TLS 本地网络使用该连接配置，禁止复制到生产配置。
    await run(['run', '--rm', '--name', name, '--label', `fullnet.ai-probe.run=${prefix}`, '--network', prefix, '--memory', '512m', '--cpus', '2',
      '-v', `${root}:/src:ro`, '-v', `${output}:/evidence`, '-e', 'PROBE_CONNECTION', '-e', 'PROBE_QDRANT',
      images.sdk, '/evidence/native/Full.NET.AiRetrieval.Probe', mode,
      '/src/tests/ai-evaluation/rag-cases.json', `/evidence/${mode}-native.json`],
    { env: { PROBE_CONNECTION: connection, PROBE_QDRANT: 'http://qdrant:6333/' }, timeout: 60000, log: `${mode}.log` });
    const report = JSON.parse(readFileSync(path.join(output, `${mode}-native.json`), 'utf8'));
    if (report.runtime !== 'NativeAOT' || report.observations.length === 0 || report.observations.some(x => !x.passed))
      throw new Error(`${mode} 原生证据未通过。`);
    console.log(`PASS ${mode}: ${report.observations.length} observations; Recall@5=${report.recallAt5}; real Embedding=${report.embeddingStatus}`);
  }
  if (JSON.stringify(sourceHashes()) !== JSON.stringify(sourceSha256)) throw new Error('实验期间源码变化，必须重新执行。');
  completedManifest = { status: 'passed', runId: prefix, images: records, approvedWarnings,
    sourceSha256, baselineHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, windowsHide: true, encoding: 'utf8' }).trim(),
    runtime: 'linux-x64 NativeAOT', memoryLimitMiB: 512, cpuLimit: 2, processTimeoutSeconds: 60,
    datasetSha256: '3380bf8d0b8352df48093b1c0dee7d4216b0eeae30733cc37f04cd65e778ec6b' };
} catch (error) {
  writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({ status: 'failed', runId: prefix, failedAtUtc: new Date().toISOString() }, null, 2) + '\n');
  console.error(error.message.replaceAll(password, '[redacted]'));
  process.exitCode = 1;
} finally {
  const cleanupFailures = [];
  for (const name of owned.reverse()) {
    try {
      const exists = await run(['container', 'inspect', name, '--format', '{{index .Config.Labels "fullnet.ai-probe.run"}}'], { allowFailure: true });
      if (exists.code !== 0) continue;
      if (exists.stdout.trim() !== prefix) throw new Error('容器归属标签不一致，拒绝清理。');
      const mounts = JSON.parse((await run(['container', 'inspect', name, '--format', '{{json .Mounts}}'])).stdout);
      // -v 仅释放本次容器的匿名卷，不删除只读源码/输出/NuGet 缓存的 bind mount。
      await run(['rm', '-f', '-v', name]);
      for (const mount of mounts.filter(x => x.Type === 'volume')) {
        const remaining = await run(['volume', 'inspect', mount.Name], { allowFailure: true });
        if (remaining.code === 0) throw new Error('本次容器匿名卷未释放。');
      }
    } catch (error) { cleanupFailures.push(error.message.replaceAll(password, '[redacted]')); }
  }
  if (networkCreated) {
    try {
      const network = await run(['network', 'inspect', prefix, '--format', '{{index .Labels "fullnet.ai-probe.run"}}']);
      if (network.stdout.trim() !== prefix) throw new Error('网络归属标签不一致，拒绝清理。');
      await run(['network', 'rm', prefix]);
    } catch (error) { cleanupFailures.push(error.message.replaceAll(password, '[redacted]')); }
  }
  if (cleanupFailures.length > 0) {
    writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({ status: 'failed', runId: prefix,
      failedAtUtc: new Date().toISOString(), cleanupFailures }, null, 2) + '\n');
    console.error(cleanupFailures.join('\n'));
    process.exitCode = 1;
  }
}
if (!process.exitCode && completedManifest)
  writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({ ...completedManifest,
    resourcesRemoved: true, completedAtUtc: new Date().toISOString() }, null, 2) + '\n');
