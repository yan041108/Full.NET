import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createWriteStream, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildAppTemplate } from '../../../scripts/templates/build-app-template.mjs';
import { createApp } from '../../../scripts/templates/create-app.mjs';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { startDatabaseContainer, startRedisContainer, buildSharedEnv, runDotnet } from './created-app-real-stack.mjs';
import { verifyEnterpriseApprovalBrowser } from './application-enterprise-approval-browser.mjs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';
import { waitForApi } from '../../e2e/admin-real-stack/scripts/wait-for-api.mjs';
import { assertNoWorkerTimeoutScanFailures, crashLoggedWorker } from './worker-crash-lifecycle.mjs';

const repoRoot = resolve(fileURLToPath(new URL('../../../', import.meta.url)));
async function freePort() {
  const server = createServer();
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  const port = server.address().port;
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return port;
}

// 同一份固定源码只生成一次应用，API 始终在线；独立 Worker 的停启验证持久化交付。
export async function verifyCreatedEnterpriseApproval(provider, { signal } = {}) {
  assert.ok(['sqlserver', 'mysql'].includes(provider));
  const reportParent = join(repoRoot, '.tmp/template-real-stack/enterprise-approval', provider);
  mkdirSync(reportParent, { recursive: true });
  const root = mkdtempSync(join(reportParent, 'run-'));
  const processes = [];
  const report = { completed: false, provider, hosts: [] };
  let database; let redis; let workspace; let workspaceToken; let worker; let failure;
  try {
    signal?.throwIfAborted();
    const { templateRoot } = buildAppTemplate({ output: join(root, 'package') });
    // 使用仓库同级短目录规避 Windows 路径限制；清理前核验本次随机目录与所有权凭据。
    workspace = mkdtempSync(join(dirname(repoRoot), 'fn-approval-'));
    workspaceToken = randomUUID(); writeFileSync(join(workspace, '.acceptance-owner'), workspaceToken);
    const appRoot = join(workspace, 'app');
    createApp({ packageRoot: templateRoot, output: appRoot, name: 'EnterpriseApproval', ownerKey: 'approval',
      database: provider, preset: 'enterprise', httpPort: await freePort() });
    const manifest = JSON.parse(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'));
    assert.equal(manifest.projectedPreset, 'enterprise'); report.sourceCommit = manifest.sourceCommit;
    const recovery = manifest.migrationInventory.scripts.find(item => item.name.startsWith('249_'));
    assert.ok(recovery); assert.deepEqual(Object.keys(recovery.providers).sort(), ['MySql', 'SqlServer']);
    report.applicationRoot = appRoot;
    const db = await startDatabaseContainer(provider); database = db.container;
    const cache = await startRedisContainer(); redis = cache.container;
    const browserPort = await freePort();
    const env = { ...testRunEnvironment(), ...buildSharedEnv(db.connectionString, db.databaseProvider, cache.connectionString),
      Identity__AllowedOrigins__3: 'http://localhost:' + browserPort, Files__Local__RootPath: join(root, 'files'),
      FullNet__ImportExport__ExecutionEnabled: 'false', FullNet__Reporting__Export__ExecutionEnabled: 'false',
      OutboxWorker__PollMilliseconds: '250', OutboxWorker__MaximumIdlePollMilliseconds: '1000' };
    const profile = JSON.parse(readFileSync(join(appRoot, 'fullnet-app.json'), 'utf8'));
    const apiUrl = 'http://127.0.0.1:' + profile.httpPort;
    const workerUrl = 'http://127.0.0.1:' + await freePort();
    const projects = {};
    for (const host of ['Api', 'Worker', 'Migrator']) {
      signal?.throwIfAborted();
      runDotnet(['build', join(appRoot, `src/EnterpriseApproval.Host.${host}/EnterpriseApproval.Host.${host}.csproj`),
        '-c', 'Release', '--nologo', '-v', 'quiet'], appRoot, env, 300_000, join(root, host.toLowerCase() + '-build.log'));
      projects[host] = join(appRoot, `src/EnterpriseApproval.Host.${host}/bin/Release/net10.0/EnterpriseApproval.Host.${host}.dll`);
    }
    runDotnet([projects.Migrator, '--seed', 'development'], appRoot, env, 600_000, join(root, 'migrator.log'));
    signal?.throwIfAborted();
    runDotnet([projects.Migrator], appRoot, env, 600_000, join(root, 'migrator-repeat.log'));
    report.hostsBuilt = 3; report.migratorRepeated = true;
    const start = async (host, url) => {
      signal?.throwIfAborted();
      const logPath = join(root, host.toLowerCase() + '-' + processes.length + '.log');
      const stream = createWriteStream(logPath);
      const child = spawn('dotnet', [projects[host]], { cwd: appRoot, env: { ...env, ASPNETCORE_URLS: url,
        Kestrel__Endpoints__Http__Url: url }, stdio: 'pipe', windowsHide: true });
      const owned = { child, stream, stopped: false, host, logPath }; processes.push(owned);
      child.stdout.pipe(stream, { end: false }); child.stderr.pipe(stream, { end: false });
      await waitForApi(url, 180_000, logPath, { signal });
      const ready = await fetch(url + '/health/ready', { signal: AbortSignal.timeout(15_000) });
      assert.equal(ready.status, 200, host + ' readiness'); assert.equal(child.exitCode, null);
      assert.ok(!report.hosts.some(item => item.pid === child.pid)); report.hosts.push({ host, pid: child.pid, ready: true });
      return owned;
    };
    await start('Api', apiUrl);
    const startWorker = async () => { assert.ok(!worker || worker.stopped); worker = await start('Worker', workerUrl); };
    const stopWorker = async () => {
      assert.ok(worker && !worker.stopped); await stopLoggedProcess(worker.child, worker.stream); worker.stopped = true;
    };
    const crashWorker = async () => {
      assert.ok(worker && processes.includes(worker) && !worker.stopped);
      const receipt = await crashLoggedWorker(worker.child, worker.stream); worker.stopped = true;
      (report.workerCrashes ??= []).push(receipt);
    };
    report.browser = await verifyEnterpriseApprovalBrowser(appRoot, apiUrl, root,
      { port: browserPort, signal, startWorker, stopWorker, crashWorker });
    for (const { child, stopped } of processes) if (!stopped) { assert.equal(child.exitCode, null); assert.equal(child.signalCode, null); }
    await stopWorker();
    // 四次启动均接受门禁；不能由一次健康响应推导超时 Worker 的运行正确性。
    const workerLogs = processes.filter(item => item.host === 'Worker');
    assert.equal(workerLogs.length, 4);
    assertNoWorkerTimeoutScanFailures(workerLogs.map(item => readFileSync(item.logPath, 'utf8')));
    report.timeoutWorkerLogsChecked = workerLogs.length;
    report.completed = true; return report;
  } catch (error) {
    failure = error; report.error = error instanceof Error ? error.message : String(error); throw error;
  } finally {
    const errors = [];
    for (const owned of processes.reverse()) if (!owned.stopped) {
      try { await stopLoggedProcess(owned.child, owned.stream); } catch (error) { errors.push(error); }
    }
    for (const container of [database, redis]) try { await container?.stop(); } catch (error) { errors.push(error); }
    try {
      if (workspace) {
        assert.equal(dirname(realpathSync(workspace)), realpathSync(dirname(repoRoot)));
        assert.ok(basename(workspace).startsWith('fn-approval-'));
        assert.equal(readFileSync(join(workspace, '.acceptance-owner'), 'utf8'), workspaceToken);
        rmSync(workspace, { recursive: true, force: true }); report.applicationWorkspaceCleaned = true;
      }
    } catch (error) { errors.push(error); }
    report.cleanupSucceeded = errors.length === 0;
    writeFileSync(join(root, 'result.json'), JSON.stringify(report, null, 2));
    console.log('Enterprise approval evidence: ' + root);
    if (errors.length && !failure) throw new AggregateError(errors, 'Enterprise approval cleanup failed');
  }
}
