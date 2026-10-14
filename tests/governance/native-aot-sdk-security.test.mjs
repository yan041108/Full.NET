import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { apiNativeAotPublishContract, workerNativeAotPublishContract } from '../../scripts/testing/api-native-aot-publish-contract.mjs';

let verifyNativeAotSdkProbe;
let probeNativeAotSdk;
try {
  ({ verifyNativeAotSdkProbe, probeNativeAotSdk } = await import('../../scripts/testing/native-aot-sdk-policy.mjs'));
} catch (error) {
  if (error.code !== 'ERR_MODULE_NOT_FOUND') throw error;
}

for (const version of ['10.0.402', '10.0.403', '10.0.500']) {
  test(`原生发布接受已修补的稳定 SDK ${version}`, () => {
    assert.equal(typeof verifyNativeAotSdkProbe, 'function');
    assert.equal(verifyNativeAotSdkProbe({ status: 0, stdout: ` ${version}\n` }, '10.0.402'), version);
  });
}
for (const version of ['10.0.400', '10.0.401', '10.0.100', '9.0.402', '11.0.100', '10.0.402-preview.1', '', 'SDK version: 10.0.402', '10.0.402\n10.0.500']) {
  test(`原生发布在清除旧证据前拒绝未受支持 SDK 输出 ${JSON.stringify(version)}`, () => {
    assert.equal(typeof verifyNativeAotSdkProbe, 'function');
    assert.throws(() => verifyNativeAotSdkProbe({ status: 0, stdout: version }, '10.0.402'), /Native AOT SDK/);
  });
}
test('SDK 探测失败不能使用输出或降级到旧缓存镜像', () => {
  assert.equal(typeof verifyNativeAotSdkProbe, 'function');
  for (const result of [{ status: 1, stdout: '10.0.402' }, { status: null, stdout: '10.0.402' }, { status: 0, stdout: '10.0.402', error: new Error('spawn failed') }]) {
    assert.throws(() => verifyNativeAotSdkProbe(result, '10.0.402'), /Native AOT SDK/);
  }
});
test('API 与 Worker 使用有版本身份的修补 SDK 及自包含运行时', async () => {
  for (const contract of [apiNativeAotPublishContract, workerNativeAotPublishContract]) {
    assert.equal(contract.minimumSdkVersion, '10.0.402');
    assert.equal(contract.sdkImage, 'mcr.microsoft.com/dotnet/sdk:10.0.402');
    assert.equal(contract.publishSdkImage, 'fullnet-native-aot-publish-sdk:10.0.402');
    assert.equal(contract.publishMsBuildProperties.RuntimeFrameworkVersion, '10.0.13');
  }
  const dockerfile = await readFile(new URL('../../eng/docker/Dockerfile.api-native-aot-linux-sdk', import.meta.url), 'utf8');
  assert.match(dockerfile, /^FROM mcr\.microsoft\.com\/dotnet\/sdk:10\.0\.402\s*$/m);
  for (const host of ['api', 'worker']) {
    const workflow = await readFile(new URL(`../../.github/workflows/${host}-native-aot-linux.yml`, import.meta.url), 'utf8');
    assert.match(workflow, /docker build -t fullnet-native-aot-publish-sdk:10\.0\.402 /);
  }
});
test('实际 SDK 探测在旧产物清理前执行且 manifest 记录探测版本', async () => {
  const script = await readFile(new URL('../../scripts/testing/run-api-aot-publish-linux.mjs', import.meta.url), 'utf8');
  assert.match(script, /probeNativeAotSdk/);
  assert(script.indexOf('const sdkVersion = probeSdkVersion(') < script.indexOf('clearPreviousPublishEvidence();'));
  assert.match(script, /sdkVersion,/);
});
test('SDK 探测与发布共用仓库挂载和工作目录，不绕过 global.json', async () => {
  const script = await readFile(new URL('../../scripts/testing/run-api-aot-publish-linux.mjs', import.meta.url), 'utf8');
  assert.match(script, /dockerArgs: publishImage \? dockerPublishArgs\(publishImage\) : null/);
  assert.match(script, /const dockerArgs = \[\.\.\.dockerPublishArgs\(publishImage\), 'bash'/);
  assert.match(script, /repositoryRoot: contract\.repositoryRoot/);
});
test('Linux 探测使用仓库固定的旧 SDK，不能用调用目录的新 SDK 通过', () => {
  assert.equal(typeof probeNativeAotSdk, 'function');
  let probe;
  const run = (command, args, options) => {
    probe = { command, args, options };
    return { status: 0, stdout: options?.cwd === '/repo' ? '10.0.401' : '10.0.402' };
  };
  assert.throws(() => probeNativeAotSdk({ run, repositoryRoot: '/repo', dockerArgs: null, minimumVersion: '10.0.402' }), /未达到/);
  assert.deepEqual(probe, { command: 'dotnet', args: ['--version'], options: { cwd: '/repo' } });
});
test('Docker 探测复用发布挂载和 /src，使仓库 SDK 选择不能被镜像默认目录替代', () => {
  assert.equal(typeof probeNativeAotSdk, 'function');
  const dockerArgs = ['run', '--rm', '-v', '/repo:/src', '-w', '/src', 'fullnet-native-aot-publish-sdk:10.0.402'];
  const original = [...dockerArgs];
  let probe;
  const run = (command, args, options) => {
    probe = { command, args, options };
    const scoped = args.includes('/repo:/src') && args[args.indexOf('-w') + 1] === '/src';
    return { status: 0, stdout: scoped ? '10.0.401' : '10.0.402' };
  };
  assert.throws(() => probeNativeAotSdk({ run, repositoryRoot: '/repo', dockerArgs, minimumVersion: '10.0.402' }), /未达到/);
  assert.deepEqual(probe, { command: 'docker', args: [...original, 'dotnet', '--version'], options: { cwd: '/repo' } });
  assert.deepEqual(dockerArgs, original);
});
