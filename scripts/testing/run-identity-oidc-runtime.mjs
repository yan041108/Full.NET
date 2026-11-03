import { spawn } from 'node:child_process';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { withTestRun, testRunEnvironment, enterHeavyTestPhase } from './test-run-context.mjs';
import { validatePublishWarnings } from './api-native-aot-publish-warnings.mjs';
import { apiNativeAotPublishContract as nativeContract } from './api-native-aot-publish-contract.mjs';
import { summarizeTrxOutcomes } from './summarize-trx-outcomes.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const native = process.argv.includes('--native');
if (process.argv.slice(2).some(value => value !== '--native')) throw new Error('用法：node scripts/testing/run-identity-oidc-runtime.mjs [--native]');

// 同一工作树和资源门禁串行准备；全部输出只进本轮系统临时目录，成功自动清理。
await withTestRun({ cwd: repository, buildBudget: { cpu: 2, memoryGiB: native ? 12 : 3 }, timeoutMs: 60 * 60_000 }, async () => {
  const env = { ...testRunEnvironment(), DOTNET_CLI_UI_LANGUAGE: 'en-US' };
  const root = path.dirname(env.TEMP);
  async function run(file, args, name) {
    const chunks = [];
    const child = spawn(file, args, { cwd: repository, env, windowsHide: true });
    child.stdout.on('data', data => chunks.push(data)); child.stderr.on('data', data => chunks.push(data));
    const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    const output = Buffer.concat(chunks);
    await writeFile(path.join(root, name + '.log'), output);
    console.log(`${name}: exit ${code}; ${path.join(root, name + '.log')}`);
    if (code !== 0) throw new Error(name + ' failed');
    return output.toString('utf8');
  }
  await run('dotnet', ['build', 'tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj', '-c', 'Release',
    '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false', '--nologo', '-v:q'], 'driver-build');
  const assembly = 'tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll';
  if (!native) {
    await enterHeavyTestPhase();
    await run('dotnet', [assembly, '--filter', 'FullyQualifiedName~IdentityBrowserUiTests', '--minimum-expected-tests', '2',
      '--timeout', '8m', '--no-ansi', '--progress', 'off', '--results-directory', root,
      '--report-trx', '--report-trx-filename', 'browser.trx'], 'browser');
  } else {
    // 源码只读挂载；Linux bin/obj 与 ELF 均在本轮 TEMP，禁止污染 Windows 还原图。
    const mounts = ['--mount', `type=bind,source=${repository},target=/src,readonly`, '--mount', `type=bind,source=${root},target=/out`,
      '--mount', `type=bind,source=${process.env.NUGET_PACKAGES ?? path.join(os.homedir(), '.nuget', 'packages')},target=/root/.nuget/packages`];
    const image = nativeContract.publishSdkImage;
    const common = ['run', '--rm', ...mounts, '--cpus', '2', '--memory', '12g', '-w', '/src', '-e', 'DOTNET_PROCESSOR_COUNT=2'];
    const available = Number((await run('docker', [...common, image, 'sh', '-c', "awk '/MemAvailable:/ {print $2}' /proc/meminfo"], 'guest-memory')).trim());
    if (!Number.isFinite(available) || available < 10 * 1024 * 1024) throw new Error('Linux guest 可用内存不足 10 GiB');
    const output = await run('docker', [...common, image, 'timeout', '30m', 'dotnet', 'publish', nativeContract.projectRelativePath,
      '-c', 'Release', '-r', nativeContract.runtimeIdentifier, '-p:SelfContained=true', '-p:FullNetPublishMode=NativeAot',
      `-p:RuntimeFrameworkVersion=${nativeContract.publishMsBuildProperties.RuntimeFrameworkVersion}`,
      '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=/out/build', '-p:UseSharedCompilation=false', '-m:1', '-nodeReuse:false',
      '-o', '/out/publish', '--nologo', '-v:minimal'], 'native-publish');
    console.log('登记的第三方 ILC 告警：', validatePublishWarnings(output).length);
    const elf = await readFile(path.join(root, 'publish', nativeContract.executableName));
    if (elf.subarray(0, 4).toString('hex') !== '7f454c46' || elf.length < nativeContract.minimumExecutableBytes) throw new Error('缺少有效原生 ELF');
    console.log('ELF', elf.length, 'SHA256', createHash('sha256').update(elf).digest('hex'));
    await enterHeavyTestPhase();
    await run('docker', [...common, '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock',
      ...(process.platform === 'linux' ? ['--add-host', 'host.docker.internal:host-gateway'] : []),
      '-e', 'TMPDIR=/out/temp',
      '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal', '-e', 'FULLNET_NATIVE_AOT_PUBLISH_DIR=/out/publish',
      '-e', 'FULLNET_NATIVE_MIGRATOR_DLL=/src/src/Hosts/Full.NET.Host.Migrator/bin/Release/net10.0/Full.NET.Host.Migrator.dll', image,
      'dotnet', assembly, '--filter', 'FullyQualifiedName~NativeApiBrowserSessionTests', '--minimum-expected-tests', '2', '--timeout', '15m',
      '--no-ansi', '--progress', 'off', '--results-directory', '/out', '--report-trx', '--report-trx-filename', 'browser.trx'], 'native-browser');
  }
  const result = summarizeTrxOutcomes(await readFile(path.join(root, 'browser.trx'), 'utf8'));
  if (result.total !== 2 || result.outcomes.Passed !== 2) throw new Error('必须双库均实际通过，不能跳过');
  console.log(JSON.stringify(result));
});
