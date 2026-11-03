import { spawn, spawnSync, execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, readdir, writeFile, lstat, realpath } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { apiNativeAotPublishContract, nativeAotPublishContract } from './api-native-aot-publish-contract.mjs';
import { validatePublishWarnings } from './api-native-aot-publish-warnings.mjs';
import { probeNativeAotSdk } from './native-aot-sdk-policy.mjs';
import { withTestRun } from './test-run-context.mjs';
import { nativeSourceInputs, assertManagedResourcesNotRuntimeInputs, buildOnlyContractIndexes } from './messaging-native-inputs.mjs';

let contract = apiNativeAotPublishContract;
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const within = (parent, child) => { const relative = path.relative(parent, child); return relative === '' || !path.isAbsolute(relative) && relative !== '..' && !relative.startsWith(`..${path.sep}`); };

// 只记录真实输入摘要，不复制仓库；托管编译阶段必须取得稳定源码窗口。
async function sourceIdentity() {
  const projects = new Set(); const queue = [path.join(contract.repositoryRoot, contract.projectRelativePath)];
  while (queue.length) {
    const project = queue.pop();
    if (projects.has(project)) continue;
    if (!within(contract.repositoryRoot, project)) throw new Error('Native 项目引用离开当前仓库，不能猜测输入归属。');
    projects.add(project);
    for (const match of (await readFile(project, 'utf8')).matchAll(/<ProjectReference\b[^>]*\bInclude="([^"]+)"/gu)) {
      if (match[1].includes('$') || match[1].includes('*')) throw new Error('Native 动态项目引用需要显式解析，不能漏记编译输入。');
      queue.push(path.resolve(path.dirname(project), match[1].replaceAll('\\', path.sep)));
    }
  }
  const files = new Set();
  async function visit(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      if (['bin', 'obj', '.git', 'artifacts', 'node_modules'].includes(entry.name)) continue;
      const absolute = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Native 输入存在符号链接，须先明确其真实归属。');
      if (entry.isDirectory()) await visit(absolute);
      else if (entry.isFile()) files.add(absolute);
    }
  }
  for (const directory of new Set([...projects].map(project => path.dirname(project)))) await visit(directory);
  await visit(path.join(contract.repositoryRoot, 'contracts'));
  for (const file of await readdir(contract.repositoryRoot))
    if (/^(?:Directory\..*\.(?:props|targets)|global\.json|nuget\.config)$/iu.test(file)) files.add(path.join(contract.repositoryRoot, file));
  const entries = [];
  for (const file of [...files].sort()) entries.push({ file: path.relative(contract.repositoryRoot, file).replaceAll('\\', '/'), sha256: hash(await readFile(file)) });
  return entries;
}

// --no-build 阶段不再编译 C#；继续冻结项目/配置/内容及本任务源码，同时验证实际 ILC 托管输入没有被改写。
async function managedIdentity(directory) {
  const entries = [];
  async function visit(folder) {
    for (const entry of await readdir(folder, { withFileTypes: true })) {
      const file = path.join(folder, entry.name);
      if (entry.isSymbolicLink()) throw new Error('托管编译输入存在符号链接，不能冻结未知目标。');
      if (entry.isDirectory()) await visit(file);
      else if (entry.isFile() && /(?:\.dll|project\.assets\.json|\.deps\.json|\.runtimeconfig\.json)$/u.test(entry.name))
        entries.push({ file: path.relative(directory, file).replaceAll('\\', '/'), sha256: hash(await readFile(file)) });
    }
  }
  await visit(directory);
  return entries.sort((left, right) => left.file.localeCompare(right.file));
}

/** 在进程开始时冻结工具版本；排队或链接期间变化必须拒绝，不能到结束才为旧实现打新摘要。 */
export async function freezeNativePublishTooling(base = new URL('.', import.meta.url)) {
  const toolingFiles = ['run-messaging-native-publish.mjs', 'api-native-aot-publish-contract.mjs',
    'api-native-aot-publish-warnings.mjs', 'messaging-native-inputs.mjs', 'native-aot-sdk-policy.mjs',
    'test-run-context.mjs', 'build-resource-pool.mjs'];
  const toolingIdentity = async () => Promise.all(toolingFiles.map(async file =>
    ({ file, sha256: hash(await readFile(new URL(file, base))) })));
  const before = await toolingIdentity();
  return { before, async verify() {
    const after = await toolingIdentity();
    if (JSON.stringify(before) !== JSON.stringify(after)) throw new Error('发布工具在运行期间变化，禁止旧进程声明新策略。');
    return after;
  } };
}

async function main() {
  const tooling = await freezeNativePublishTooling();
  const toolingBefore = tooling.before;
  const args = process.argv.slice(2); const flags = Object.fromEntries(args.reduce((all, value, index) => index % 2 ? all : [...all, [value, args[index + 1]]], []));
  if (args.length % 2 || !flags['--output'] || Object.keys(flags).some(key => !['--output', '--resume', '--resume-managed', '--host'].includes(key))
      || flags['--resume-managed'] && !flags['--resume'] || flags['--host'] && !['api', 'worker', 'migrator', 'log-consumer'].includes(flags['--host']))
    throw new Error('用法：node scripts/testing/run-messaging-native-publish.mjs --output <任务系统临时目录> [--host api|worker|migrator|log-consumer] [--resume <同任务已归属的原生输出目录> [--resume-managed <同目录已冻结阶段>]]');
  const host = flags['--host'] ?? 'api';
  contract = nativeAotPublishContract(host);
  const output = path.resolve(flags['--output']);
  if (!within(os.tmpdir(), output) || within(contract.repositoryRoot, output)) throw new Error('Native 验证产物必须在仓库外系统临时目录。');
  await mkdir(output, { recursive: true });
  const directory = flags['--resume'] ? path.resolve(flags['--resume']) : await mkdtemp(path.join(output, host + '-'));
  if (flags['--resume']) {
    if (!within(output, directory) || directory === output) throw new Error('只能复用本任务输出目录的子目录。');
    const previous = JSON.parse(await readFile(path.join(directory, 'command.json'), 'utf8'));
    if (previous.executable !== 'docker' || !previous.args.includes(`fullnet.messaging.native.owner=${hash(directory)}`)
      || !previous.args.includes(`${contract.repositoryRoot.replaceAll('\\', '/')}:/src:ro`)
      || !previous.args.includes(`/src/${contract.projectRelativePath}`)) throw new Error('原生输出归属或宿主不匹配，禁止混用不同角色目录。');
  }
  const attempt = await mkdtemp(path.join(directory, 'attempt-'));
  // 各宿主按真实发布证据分别预留预算，仍须由资源池准入并执行容器硬限额。
  await withTestRun({ cwd: directory, build: true, buildBudget: contract.buildBudget, timeoutMs: 150 * 60_000 }, async () => {
    await tooling.verify();
    await writeFile(path.join(attempt, 'tooling-input.json'), JSON.stringify(toolingBefore, null, 2));
    const run = (command, args, options = {}) => spawnSync(command, args, { encoding: 'utf8', windowsHide: true, ...options });
    const imageId = execFileSync('docker', ['image', 'inspect', contract.publishSdkImage, '--format', '{{.Id}}'], { encoding: 'utf8' }).trim();
    const sdkVersion = probeNativeAotSdk({ run, repositoryRoot: contract.repositoryRoot,
      dockerArgs: ['run', '--rm', '--memory', '512m', imageId], minimumVersion: contract.minimumSdkVersion });
    const head = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: contract.repositoryRoot, encoding: 'utf8' }).trim();
    const packages = process.env.NUGET_PACKAGES ?? path.join(os.homedir(), '.nuget', 'packages');
    const owner = hash(directory); const containerName = `fullnet-messaging-native-${owner.slice(0, 16)}`;
    const dockerArgs = ['run', '--rm', '--name', containerName, '--label', `fullnet.messaging.native.owner=${owner}`,
      '--cpus', String(contract.buildBudget.cpu), '--memory', `${contract.buildBudget.memoryGiB}g`, '--memory-swap', `${contract.buildBudget.memoryGiB}g`,
      '-v', `${contract.repositoryRoot.replaceAll('\\', '/')}:/src:ro`, '-v', `${directory.replaceAll('\\', '/')}:/work`,
      '-v', `${packages.replaceAll('\\', '/')}:/root/.nuget/packages`, '-e', 'DOTNET_PROCESSOR_COUNT=2',
      '-e', 'NUGET_PACKAGES=/root/.nuget/packages', '-w', '/src', imageId];
    const properties = [`/src/${contract.projectRelativePath}`, '-c', 'Release', '-r', contract.runtimeIdentifier,
      '--self-contained', 'true', '-p:FullNetPublishMode=NativeAot', `-p:RuntimeFrameworkVersion=${contract.publishMsBuildProperties.RuntimeFrameworkVersion}`,
      '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=/work/build', '-p:UseSharedCompilation=false', '-m:1', '--nologo'];
    const startedAt = new Date().toISOString();
    async function phase(name, command, extra, timeoutMinutes) {
      const args = [...dockerArgs, 'dotnet', command, ...properties, ...extra];
      await writeFile(path.join(directory, 'command.json'), JSON.stringify({ executable: 'docker', args }, null, 2));
      await writeFile(path.join(attempt, `${name}-command.json`), JSON.stringify({ executable: 'docker', args }, null, 2));
      const chunks = [];
      const exit = await new Promise((resolve, reject) => {
        const child = spawn('docker', args, { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
        // 只清理带有本轮精确归属的容器；不能因 CLI 中断而留下继续占用资源的后台编译。
        const cleanup = () => {
          const inspected = run('docker', ['inspect', '--format', '{{ index .Config.Labels "fullnet.messaging.native.owner" }}', containerName]);
          if (inspected.status === 0 && inspected.stdout.trim() === owner) run('docker', ['rm', '--force', containerName]);
        };
        let interrupted = false;
        const stop = () => { interrupted = true; cleanup(); child.kill(); };
        const timeout = setTimeout(stop, timeoutMinutes * 60 * 1000);
        process.once('SIGINT', stop); process.once('SIGTERM', stop);
        for (const stream of [child.stdout, child.stderr]) stream.on('data', chunk => { chunks.push(chunk); process.stdout.write(chunk); });
        child.on('error', reject); child.on('close', code => {
          clearTimeout(timeout); process.removeListener('SIGINT', stop); process.removeListener('SIGTERM', stop);
          cleanup(); resolve(interrupted ? 124 : code);
        });
      });
      const log = Buffer.concat(chunks).toString();
      await writeFile(path.join(attempt, `${name}.log`), log);
      if (exit !== 0) throw new Error(`${name} 退出 ${exit}；保留失败日志。`);
      return log;
    }

    async function runtimeInputEvidence(name, result) {
      const jsonStart = result.indexOf('{');
      const items = JSON.parse(result.slice(jsonStart)).Items.ResolvedFileToPublish;
      const rsp = await readFile(path.join(directory, `build/obj/${contract.executableName}/release_linux-x64/native/${contract.executableName}.ilc.rsp`), 'utf8');
      assertManagedResourcesNotRuntimeInputs(rsp, items, frozenManagedResources, contract.executableName);
      const files = [];
      async function visit(folder) {
        for (const entry of await readdir(folder, { withFileTypes: true })) {
          const file = path.join(folder, entry.name);
          if (entry.isSymbolicLink()) throw new Error('发布目录出现未知链接，禁止登记通过。');
          if (entry.isDirectory()) await visit(file);
          else files.push(path.relative(path.join(directory, 'publish'), file).replaceAll('\\', '/'));
        }
      }
      await visit(path.join(directory, 'publish'));
      assertManagedResourcesNotRuntimeInputs(rsp, files, frozenManagedResources, contract.executableName);
      await writeFile(path.join(attempt, `${name}.json`), JSON.stringify({ ilcResponseSha256: hash(rsp), publishedFiles: items,
        finalPublishedFiles: files.sort() }, null, 2));
    }
    let before; let managedLog; let managedInputsVerified = false; let resumedManagedAttempt = null; let resumedManagedInputs;
    let frozenManagedResources = [];
    function readFrozenManagedResources(source, managed) {
      const project = 'src/Modules/Full.NET.Modules.Ai/Full.NET.Modules.Ai.csproj';
      if (!source.some(item => item.file === project) || !managed.some(item => item.file.endsWith('/Full.NET.Modules.Ai.dll'))) return [];
      // 使用与编译相同的项目、配置、SDK及隔离输出，只求值真实资源条目，不编译或改写冻结IL。
      const result = run('docker', [...dockerArgs, 'dotnet', 'msbuild', '/src/' + project, '--nologo',
        '-getItem:EmbeddedResource', '-p:Configuration=Release', '-p:FullNetPublishMode=NativeAot',
        '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=/work/build'], { timeout: 60_000, maxBuffer: 4 * 1024 * 1024 });
      if (result.status !== 0) throw new Error('无法求值真实嵌入资源，不能放宽输入分类。');
      const entries = JSON.parse(result.stdout.slice(result.stdout.indexOf('{'))).Items.EmbeddedResource;
      return entries.map(item => path.posix.relative('/src', item.FullPath.replaceAll('\\', '/')))
        .filter(file => file.endsWith('.resx') && source.some(item => item.file === file)).sort();
    }
    if (flags['--resume-managed']) {
      const previousAttempt = path.resolve(flags['--resume-managed']);
      if (path.dirname(previousAttempt) !== directory || !path.basename(previousAttempt).startsWith('attempt-'))
        throw new Error('只能复用同一归属目录内的已冻结托管阶段。');
      if ((await lstat(previousAttempt)).isSymbolicLink()
          || path.dirname(await realpath(previousAttempt)).toLowerCase() !== (await realpath(directory)).toLowerCase())
        throw new Error('不能通过符号链接复用未知归属的编译输入。');
      const rejected = JSON.parse(await readFile(path.join(previousAttempt, 'rejected-input.json'), 'utf8'));
      const previousCommand = JSON.parse(await readFile(path.join(previousAttempt, 'publish-command.json'), 'utf8'));
      // 只规范化元数据输出开关；Docker、镜像和全部编译参数必须逐项相同。
      const metadataFlag = '-getItem:ResolvedFileToPublish';
      const previousArgs = previousCommand.args?.filter(value => value !== metadataFlag);
      if (rejected.reason !== 'native_inputs_changed_during_publish' || !rejected.managedArtifactsUnchanged || !rejected.headUnchanged
          || rejected.head !== head || previousCommand.executable !== 'docker'
          || previousCommand.args.filter(value => value === metadataFlag).length > 1
          || JSON.stringify(previousArgs) !== JSON.stringify([...dockerArgs, 'dotnet', 'publish', ...properties, '--no-build', '-o', '/work/publish']))
        throw new Error('原托管阶段的拒绝原因、镜像、HEAD 或归属不符合安全复用条件。');
      before = JSON.parse(await readFile(path.join(previousAttempt, 'source-input.json'), 'utf8'));
      for (let iteration = 3; iteration >= 1; iteration--) {
        try {
          const recorded = JSON.parse(await readFile(path.join(previousAttempt, `managed-${iteration}-source.json`), 'utf8'));
          if (JSON.stringify(recorded) !== JSON.stringify(before)) throw new Error('最后一轮托管构建不匹配稳定源码快照。');
          managedLog = await readFile(path.join(previousAttempt, `managed-${iteration}.log`), 'utf8');
          managedInputsVerified = /Build succeeded\./u.test(managedLog) && /0 Warning\(s\)/u.test(managedLog) && /0 Error\(s\)/u.test(managedLog);
          break;
        } catch (error) { if (error.code !== 'ENOENT') throw error; }
      }
      if (!managedInputsVerified) throw new Error('未找到原稳定托管构建的完整成功证据。');
      const frozen = JSON.parse(await readFile(path.join(previousAttempt, 'managed-input.json'), 'utf8'));
      resumedManagedInputs = frozen;
      if (JSON.stringify(frozen) !== JSON.stringify(await managedIdentity(path.join(directory, 'build'))))
        throw new Error('原冻结托管输入已变化，禁止复用。');
      frozenManagedResources = readFrozenManagedResources(before, frozen);
      if (JSON.stringify(nativeSourceInputs(before, frozenManagedResources, contract.executableName)) !== JSON.stringify(nativeSourceInputs(await sourceIdentity(), frozenManagedResources, contract.executableName)))
        throw new Error('实际配置、内容或本任务源码已变化，禁止复用原托管构建。');
      managedLog += '\n' + await readFile(path.join(previousAttempt, 'publish.log'), 'utf8');
      resumedManagedAttempt = previousAttempt;
    }
    for (let iteration = 1; !managedInputsVerified && iteration <= 3; iteration++) {
      before = await sourceIdentity();
      await writeFile(path.join(attempt, `managed-${iteration}-source.json`), JSON.stringify(before, null, 2));
      managedLog = await phase(`managed-${iteration}`, 'build', [], 15);
      managedInputsVerified = JSON.stringify(before) === JSON.stringify(await sourceIdentity());
      if (managedInputsVerified) break;
      process.stdout.write('托管编译期间输入变化；仅重试增量托管阶段，不使用混合输入生成机器码。\n');
    }
    if (!managedInputsVerified) throw new Error('三轮托管编译均未取得稳定输入；不生成 Native 机器码。');
    await writeFile(path.join(attempt, 'source-input.json'), JSON.stringify(before, null, 2));
    const managed = resumedManagedInputs ?? await managedIdentity(path.join(directory, 'build'));
    if (!managed.some(entry => entry.file.endsWith(`/${contract.executableName}.dll`))) throw new Error(`缺少已冻结的 ${contract.executableName} 托管输入。`);
    await writeFile(path.join(attempt, 'managed-input.json'), JSON.stringify(managed, null, 2));
    frozenManagedResources = readFrozenManagedResources(before, managed);
    await writeFile(path.join(attempt, 'frozen-managed-resources.json'), JSON.stringify(frozenManagedResources, null, 2));
    // 从同一次实际 Publish 读取清单；额外 ComputeFilesToPublish 会执行 LinkNative/ILLink 并扰动缓存。
    const log = await phase('publish', 'publish', ['--no-build', '-o', '/work/publish', '-getItem:ResolvedFileToPublish'], 45);
    await runtimeInputEvidence('verified-runtime-inputs', log);
    const warnings = validatePublishWarnings(managedLog + '\n' + log, host);
    const after = await sourceIdentity();
    await writeFile(path.join(attempt, 'source-after-native.json'), JSON.stringify(after, null, 2));
    const sourceUnchanged = JSON.stringify(before) === JSON.stringify(after);
    const nativeInputsUnchanged = JSON.stringify(nativeSourceInputs(before, frozenManagedResources, contract.executableName)) === JSON.stringify(nativeSourceInputs(after, frozenManagedResources, contract.executableName));
    const managedAfter = await managedIdentity(path.join(directory, 'build'));
    const managedArtifactsUnchanged = JSON.stringify(managed) === JSON.stringify(managedAfter);
    const headUnchanged = head === execFileSync('git', ['rev-parse', 'HEAD'], { cwd: contract.repositoryRoot, encoding: 'utf8' }).trim();
    const toolingAfter = await tooling.verify();
    const toolingInputsUnchanged = JSON.stringify(toolingBefore) === JSON.stringify(toolingAfter);
    await writeFile(path.join(attempt, 'tooling-after-native.json'), JSON.stringify(toolingAfter, null, 2));
    if (!toolingInputsUnchanged) throw new Error('发布工具在运行期间变化，不能将已加载旧策略登记为当前文件版本。');
    if (!nativeInputsUnchanged || !managedArtifactsUnchanged || !headUnchanged) {
      await writeFile(path.join(attempt, 'managed-after-native.json'), JSON.stringify(managedAfter, null, 2));
      await writeFile(path.join(attempt, 'rejected-input.json'), JSON.stringify({ head, startedAt,
        finishedAt: new Date().toISOString(), reason: 'native_inputs_changed_during_publish', nativeInputsUnchanged, managedArtifactsUnchanged, headUnchanged }, null, 2));
      throw new Error('Native 阶段实际输入或本任务源码变化；不能登记通过。');
    }
    const executable = path.join(directory, 'publish', contract.executableName);
    const bytes = await readFile(executable);
    if (bytes.length < contract.minimumExecutableBytes || bytes.subarray(0, 4).toString('hex') !== '7f454c46') throw new Error('没有生成符合契约的 Linux 原生 ELF。');
    const previous = new Map(before.map(entry => [entry.file, entry.sha256]));
    const current = new Map(after.map(entry => [entry.file, entry.sha256]));
    const manifest = { schemaVersion: 2, directory, executable, sha256: hash(bytes), bytes: bytes.length, startedAt, finishedAt: new Date().toISOString(),
      sourceUnchanged, managedInputsVerified, nativeInputsUnchanged, managedArtifactsUnchanged, headUnchanged, toolingInputsUnchanged,
      sourceChangesDuringNative: [...new Set([...previous.keys(), ...current.keys()])].filter(file => previous.get(file) !== current.get(file)),
      managedSha256: hash(JSON.stringify(managed)), sdkVersion, imageId, runtimeIdentifier: contract.runtimeIdentifier, allowedWarnings: warnings.length,
      head, resumedManagedAttempt, excludedBuildOnlyIndexes: buildOnlyContractIndexes,
      inputPolicySha256: toolingBefore.find(item => item.file === 'messaging-native-inputs.mjs').sha256,
      toolingInputsSha256: hash(JSON.stringify(toolingBefore)), frozenManagedResources,
      sourceSha256: hash(JSON.stringify(before)), host, scope: '真实 Native ELF 发布；对应角色运行范围由关联外部进程验收决定' };
    await writeFile(path.join(directory, 'manifest.json'), JSON.stringify(manifest, null, 2));
    await writeFile(path.join(attempt, 'manifest.json'), JSON.stringify(manifest, null, 2));
    await writeFile(path.join(output, host === 'api' ? 'latest.json' : `latest-${host}.json`), JSON.stringify(manifest, null, 2));
    process.stdout.write(`Native ELF 已生成：${executable}\n`);
  });
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href)
  main().catch(error => { process.stderr.write(`${error.stack}\n`); process.exitCode = 1; });
