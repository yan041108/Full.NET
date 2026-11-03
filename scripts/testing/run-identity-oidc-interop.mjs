import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import os from 'node:os';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { withTestRun, testRunEnvironment, enterHeavyTestPhase } from './test-run-context.mjs';
import { summarizeTrxOutcomes } from './summarize-trx-outcomes.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contract = JSON.parse(await readFile(path.join(repository, 'eng/testing/test-matrix.json'), 'utf8')).integration.shards['identity-oidc-network-interop'];
const args = process.argv.slice(2);
let evidence;
for (let index = 0; index < args.length; index++) {
  if (args[index] === '--conformance') continue;
  if (args[index] === '--evidence' && args[index + 1]) { evidence = path.resolve(repository, args[++index]); continue; }
  throw Error('用法：node scripts/testing/run-identity-oidc-interop.mjs [--conformance] [--evidence docs/verification/file.json]');
}
if (evidence) {
  const relative = path.relative(path.join(repository, 'docs/verification'), evidence);
  if (relative.startsWith('..') || path.isAbsolute(relative) || !relative.endsWith('.json')) throw Error('证据只能写入 docs/verification 下明确指定的 JSON');
}
const inputFiles = ['src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityOidcServiceCollectionExtensions.cs',
  'src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityBrowserServiceCollectionExtensions.cs',
  'src/Modules/Full.NET.Modules.Identity/Oidc/IdentityOidcAuthorizationService.cs', 'src/Modules/Full.NET.Modules.Identity/Features/OidcSession/Endpoint.cs',
  'tests/Full.NET.IntegrationTests/Identity/IdentityOidcNetworkFixture.cs', 'tests/Full.NET.IntegrationTests/Identity/IdentityOidcNetworkInteropTests.cs',
  'tests/Full.NET.IntegrationTests/Identity/IdentityOidcConformance.cs', 'tests/e2e/admin-real-stack/scripts/oidc-network-rp.mjs',
  'tests/e2e/admin-real-stack/package.json', 'pnpm-lock.yaml', 'scripts/testing/run-identity-oidc-interop.mjs',
  'eng/testing/test-matrix.json', 'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj',
  'tests/Full.NET.IntegrationTests/Identity/IdentityBrowserTestSupport.cs', 'tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs'];
async function digest() {
  const hash = createHash('sha256');
  for (const file of inputFiles) hash.update(file).update(Uint8Array.of(0)).update(await readFile(path.join(repository, file)));
  return hash.digest('hex');
}
// 同工作区复用构建；测试同时只运行一个 Provider，真实进程与私有文件都由该轮拥有。
await withTestRun({ cwd: repository, buildBudget: { cpu: 2, memoryGiB: 8 }, timeoutMs: 60 * 60_000 }, async () => {
  const env = { ...testRunEnvironment(), DOTNET_CLI_UI_LANGUAGE: 'en-US' };
  if (process.argv.includes('--conformance')) env.FULLNET_OIDC_CONFORMANCE = '1';
  const root = path.dirname(env.TEMP); console.log('OIDC interop evidence:', root);
  const source = await digest();
  async function run(args, name) {
    const chunks = []; const child = spawn('dotnet', args, { cwd: repository, env, windowsHide: true });
    child.stdout.on('data', value => chunks.push(value)); child.stderr.on('data', value => chunks.push(value));
    const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    const output = Buffer.concat(chunks); await writeFile(path.join(root, name + '.log'), output);
    console.log(name, 'exit', code, 'log', path.join(root, name + '.log'));
    if (code !== 0) throw Error(name + ' failed');
  }
  await run(['build', 'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj', '-c', 'Release', '--no-restore', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false', '--nologo', '-v:q'], 'driver');
  await enterHeavyTestPhase();
  await run(['tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll', '--filter', contract.filter, '--minimum-expected-tests', String(contract.minimum), '--timeout', contract.timeout, '--no-ansi', '--progress', 'off', '--results-directory', root, '--report-trx', '--report-trx-filename', 'network.trx'], 'network');
  const xml = await readFile(path.join(root, 'network.trx'), 'utf8');
  const result = summarizeTrxOutcomes(xml);
  console.log(JSON.stringify(result));
  if (result.total !== contract.minimum || result.outcomes.Passed !== contract.minimumPassedOnTestcontainers) throw Error('必须双库实际通过，不能跳过');
  if (source !== await digest()) throw Error('测试输入在构建或运行期间发生变化，需要重新验证');
  const records = [];
  for (const match of xml.matchAll(/<StdOut>([\s\S]*?)<\/StdOut>/g)) {
    const text = match[1].replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
    for (const record of text.matchAll(/OIDC_NETWORK_RESULT (\{[^\r\n]+\})/g)) records.push(JSON.parse(record[1]));
  }
  if (records.length !== 2 || new Set(records.map(record => record.provider)).size !== 2) throw Error('缺少双库原始度量');
  if (args.includes('--conformance') && records.some(record => record.officialModules.length !== 3 || record.officialModules.some(module => module.result !== 'PASSED'))) throw Error('官方选定模块必须逐一通过');
  if (evidence) await writeFile(evidence, JSON.stringify({ schemaVersion: 1, recordedAtUtc: new Date().toISOString(),
    environment: { platform: process.platform, cpu: os.cpus()[0].model, logicalCpus: os.cpus().length, ramBytes: os.totalmem(), node: process.version },
    inputScope: inputFiles, inputSha256: source, outcomes: result, records }, null, 2) + '\n');
  console.log('Measured providers:', records.map(record => ({ provider: record.provider, healthySamplesMs: record.healthySamplesMs,
    recoveryMs: record.recoveryAfterRestartMs, officialModules: record.officialModules.length, probes: record.probes.map(probe => ({ concurrency: probe.concurrency, requests: probe.requests, errors: probe.errors, p99Ms: probe.p99Ms })) })));
});
