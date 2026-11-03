import { spawn } from 'node:child_process';
import { appendFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startBoundedResourceReaper } from './bounded-testcontainers-reaper.mjs';
import { testRunEnvironment, withTestRun } from './test-run-context.mjs';
import { loadTestMatrix } from './run-dotnet-test-suite.mjs';
import { summarizeTrxOutcomes } from './summarize-trx-outcomes.mjs';

// 只运行已明确选择的 CodeGeneration HTTP 场景，不隐式编译或扩大为整个 Integration 套件。
const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const matrix = loadTestMatrix();
const selections = Object.fromEntries(['contracts', 'runs', 'lifecycle', 'completion', 'repair', 'acceptance', 'chain', 'current-workspace']
  .map(name => [name, matrix.integration.shards[`code-generation-http-${name}`]]));
const args = process.argv.slice(2);
if (args.length !== 2 || args[0] !== '--selection' || !Object.hasOwn(selections, args[1])) {
  throw new Error('用法：node scripts/testing/run-codegeneration-http.mjs --selection contracts|runs|lifecycle|completion|repair|acceptance|chain|current-workspace；先通过构建资源入口编译 Integration 工程。');
}
const selection = selections[args[1]];
const docker = parameters => new Promise((resolve, reject) => {
  const child = spawn('docker', parameters, { windowsHide: true });
  let output = '', errors = '';
  child.stdout.on('data', data => { output += data; });
  child.stderr.on('data', data => { errors += data; });
  child.on('error', reject);
  child.on('close', code => code === 0 ? resolve(output) : reject(new Error(`Docker ${parameters[0]} 失败：${errors}`)));
});

await withTestRun({ cwd: repository, heavy: true }, async () => {
  const environment = testRunEnvironment();
  const output = path.join(environment.TEMP, 'codegeneration-http');
  await mkdir(output, { recursive: true });
  const settings = path.join(output, 'serial.runsettings');
  await writeFile(settings, '<RunSettings><MSTest><Parallelize><Workers>1</Workers><Scope>ClassLevel</Scope></Parallelize></MSTest></RunSettings>');
  let test;
  const reaper = await startBoundedResourceReaper({ docker,
    journal: entry => appendFile(path.join(output, 'containers.jsonl'), JSON.stringify(entry) + '\n'),
    onDisconnect: () => { if (test?.exitCode === null) test.kill(); },
  });
  try {
    reaper.assertReady();
    const code = await new Promise((resolve, reject) => {
      test = spawn('dotnet', [path.join(repository, matrix.integration.assembly),
        '--filter', selection.filter, '--minimum-expected-tests', String(selection.minimum), '--settings', settings,
        '--timeout', selection.timeout, '--no-ansi', '--progress', 'off', '--results-directory', output,
        '--report-trx', '--report-trx-filename', `${args[1]}.trx`],
      { cwd: repository, env: { ...environment, ...reaper.environment, DOTNET_PROCESSOR_COUNT: '4' }, stdio: 'inherit', windowsHide: true });
      test.on('error', reject);
      test.on('close', resolve);
    });
    if (code !== 0) throw new Error(`HTTP 聚焦验收退出码：${code}`);
    // 在成功上下文回收前核对原始 TRX，不能以零退出码把跳过或未执行的场景计为通过。
    const report = summarizeTrxOutcomes(await readFile(path.join(output, `${args[1]}.trx`), 'utf8'));
    if (report.outcomes.Passed < selection.minimum || report.outcomes.Passed !== report.total) {
      throw new Error(`HTTP 原始结果未全部通过：${JSON.stringify(report)}`);
    }
    process.stdout.write(`HTTP 原始结果核对：${JSON.stringify(report)}\n`);
  } finally {
    // 本批随机标签与 Ryuk ACK 确保只回收自有资源；共享数据库容器不在此会话中。
    await reaper.close();
  }
});
