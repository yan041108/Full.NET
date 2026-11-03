#!/usr/bin/env node
/** 调用方先完成隔离构建及资源准入；此入口只复用已准备产物，核对原生角色和精确 UID，不隐式构建。 */
import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { nativeAotPublishContract } from './api-native-aot-publish-contract.mjs';
import { validateNativeAotRuntimeResults } from './native-aot-runtime-results.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const args = process.argv.slice(2);
const flags = Object.fromEntries(args.reduce((all, value, index) => index % 2 ? all : [...all, [value, args[index + 1]]], []));
if (args.length % 2 || Object.keys(flags).some(key => !['--host','--assembly','--artifact','--manifest','--source-input','--results'].includes(key))
    || !['migrator','log-consumer'].includes(flags['--host']) || ['--assembly','--artifact','--manifest','--source-input','--results'].some(key => !flags[key]))
  throw Error('用法：--host migrator|log-consumer --assembly <已构建控制器DLL> --artifact <ELF> --manifest <发布manifest.json> --source-input <同次source-input.json> --results <仓库外任务目录>');
if (process.platform !== 'linux') throw Error('独立角色运行验收要求 Linux；Windows 请使用已预算的 Linux 容器或 WSL，不以发现/跳过冒充 Native。');
const host = flags['--host'], contract = nativeAotPublishContract(host), results = path.resolve(flags['--results']);
const relative = path.relative(repository, results);
if (relative === '' || !relative.startsWith('..') && !path.isAbsolute(relative)) throw Error('本地结果必须进入仓库外任务目录。');
const manifest = JSON.parse(readFileSync(flags['--manifest'], 'utf8'));
const source = JSON.parse(readFileSync(flags['--source-input'], 'utf8'));
const elf = readFileSync(flags['--artifact']);
if (manifest.host !== host || manifest.runtimeIdentifier !== 'linux-x64' || path.basename(flags['--artifact']) !== contract.executableName
    || !manifest.managedInputsVerified || !manifest.nativeInputsUnchanged || !manifest.managedArtifactsUnchanged || !manifest.headUnchanged || !manifest.toolingInputsUnchanged
    || manifest.sha256 !== hash(elf) || manifest.bytes !== elf.length || elf.subarray(0,4).toString('hex') !== '7f454c46'
    || manifest.sourceSha256 !== hash(JSON.stringify(source))) throw Error('独立 Native 角色身份或发布输入证明不完整。');
const matrix = JSON.parse(readFileSync(path.join(repository, 'eng/testing/test-matrix.json'), 'utf8'));
const gate = matrix[host === 'migrator' ? 'migratorNativeAotIntegration' : 'logConsumerNativeAotIntegration'];
mkdirSync(results, { recursive: true });
const environment = { ...process.env,
  ['FULLNET_NATIVE_' + host.replaceAll('-', '_').toUpperCase() + '_PATH']: path.resolve(flags['--artifact']),
  ['FULLNET_NATIVE_' + host.replaceAll('-', '_').toUpperCase() + '_SHA256']: manifest.sha256,
  FULLNET_NATIVE_ROLE_EVIDENCE_DIR: path.join(results, 'native-processes') };
delete environment.DOTNET_STARTUP_HOOKS;
for (const provider of ['SqlServer', 'MySql'])
  environment['FULLNET_NATIVE_MIGRATOR_EXPECTED_SCRIPTS_' + provider.toUpperCase()] = String(source.filter(x => x.file.includes('/Migrations/' + provider + '/') && x.file.endsWith('.sql')).length);

function identity() {
  const entries = [];
  function visit(directory, prefix) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const absolute = path.join(directory, entry.name), file = prefix + '/' + entry.name;
      if (entry.isSymbolicLink()) throw Error('运行闭包不接受未知符号链接。');
      if (entry.isDirectory()) visit(absolute, file);
      else if (/\.(dll|pdb|so|json|xml)$/.test(entry.name) || absolute === path.resolve(flags['--artifact'])) entries.push({ file, sha256: hash(readFileSync(absolute)) });
    }
  }
  visit(path.dirname(path.resolve(flags['--assembly'])), 'controller');
  visit(path.dirname(path.resolve(flags['--artifact'])), 'native');
  for (const name of ['--manifest','--source-input']) entries.push({file:name,sha256:hash(readFileSync(flags[name]))});
  return entries.sort((a,b)=>a.file.localeCompare(b.file));
}
const before = identity();
const run = extra => spawnSync('dotnet', [flags['--assembly'], ...extra], { cwd:repository, env:environment, encoding:'utf8', windowsHide:true, maxBuffer:32*1024**2 });
const discovery = run(['--list-tests','json','--no-ansi','--filter',gate.filter]);
if (discovery.status !== 0) throw Error(discovery.stderr || discovery.stdout || '原生角色发现失败。');
const discovered = JSON.parse(discovery.stdout).tests ?? [], expected = discovered.map(x=>x.uid);
if (discovered.length !== gate.minimum || new Set(expected).size !== expected.length) throw Error('独立角色精确发现集合与机器门槛不符。');
const report = `${host}-${randomUUID()}.trx`;
writeFileSync(path.join(results,'input.json'), JSON.stringify({host,manifestSha256:hash(readFileSync(flags['--manifest'])),expectedUids:expected,before,filter:gate.filter,report},null,2));
const result = run(['--no-ansi','--progress','off','--timeout',gate.timeout,'--filter',gate.filter,'--minimum-expected-tests',String(gate.minimum),
  '--results-directory',results,'--report-trx','--report-trx-filename',report]);
writeFileSync(path.join(results,'controller.stdout.log'),result.stdout??'');
writeFileSync(path.join(results,'controller.stderr.log'),result.stderr??'');
const after = identity();
writeFileSync(path.join(results,'result.json'),JSON.stringify({host,at:new Date().toISOString(),exit:result.status,unchanged:JSON.stringify(before)===JSON.stringify(after),after},null,2));
if (result.status !== 0 || result.error || JSON.stringify(before)!==JSON.stringify(after)) throw Error('原生角色运行失败或冻结闭包改变；保留本次原始结果，不登记通过。');
const verified = validateNativeAotRuntimeResults(readFileSync(path.join(results,report),'utf8'),expected);
console.log(JSON.stringify({host,passed:verified.passed,skipped:0,results}));
