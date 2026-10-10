import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { prepareCiTestImages } from '../../scripts/testing/prepare-ci-test-images.mjs';

const imageId = `sha256:${'a'.repeat(64)}`;
function recorder(fail = () => {}) {
  const calls = [];
  return { calls, run(args) {
    calls.push(args);
    fail(args);
    return args[0] === 'image' ? imageId : '';
  } };
}

test('官方源预拉取后按顺序核对镜像与原有标签', () => {
  const adapter = recorder();
  assert.deepEqual(prepareCiTestImages(adapter), ['mysql:8.0', 'redis:8.6']);
  assert.deepEqual(adapter.calls, ['mysql:8.0', 'redis:8.6'].flatMap(target => {
    const source = `public.ecr.aws/docker/library/${target}`;
    return [['pull', source], ['image', 'inspect', '--format', '{{.Id}}', source],
      ['tag', source, target], ['image', 'inspect', '--format', '{{.Id}}', target]];
  }));
});

test('SQL Server 作业只准备 Redis，生成应用保留 MySQL 8.4', () => {
  assert.deepEqual(prepareCiTestImages({ ...recorder(), redisOnly: true }), ['redis:8.6']);
  assert.deepEqual(prepareCiTestImages({ ...recorder(), includeMySql84: true }),
    ['mysql:8.0', 'mysql:8.4', 'redis:8.6']);
});

test('拉取失败不能设置别名或继续准备下一镜像', () => {
  const adapter = recorder(() => { throw new Error('registry unavailable'); });
  assert.throws(() => prepareCiTestImages(adapter), /registry unavailable/);
  assert.equal(adapter.calls.length, 1);
});

test('源镜像元数据无效不能覆盖原有标签', () => {
  const calls = [];
  assert.throws(() => prepareCiTestImages({ run(args) { calls.push(args); return 'not-an-image'; } }), /ID 无效/);
  assert.equal(calls.length, 2);
});

test('别名核对失败不能开始下一镜像', () => {
  const adapter = recorder();
  assert.throws(() => prepareCiTestImages({ run(args) {
    const value = adapter.run(args);
    return args.at(-1) === 'mysql:8.0' && args[0] === 'image' ? `sha256:${'b'.repeat(64)}` : value;
  } }), /别名不匹配/);
  assert.equal(adapter.calls.length, 4);
});

test('相互矛盾的选择在运行 Docker 前拒绝', () => {
  const adapter = recorder();
  assert.throws(() => prepareCiTestImages({ ...adapter, redisOnly: true, includeMySql84: true }), /不能同时/);
  assert.equal(adapter.calls.length, 0);
});

test('真实数据库 CI 在昂贵构建之前准备官方测试镜像', async () => {
  for (const file of ['api-native-aot-linux.yml', 'worker-native-aot-linux.yml']) {
    const source = await readFile(new URL(`../../.github/workflows/${file}`, import.meta.url), 'utf8');
    const prepare = source.indexOf('node scripts/testing/prepare-ci-test-images.mjs');
    assert.ok(prepare >= 0, `${file} 缺少镜像准备`);
    assert.ok(prepare < source.indexOf('run: dotnet restore'), `${file} 应在构建前暴露拉取失败`);
  }
  const ci = await readFile(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
  assert.ok(ci.includes('run: node --test tests/testing/prepare-ci-test-images.test.mjs'), 'CI 必须执行镜像准备契约测试');
  for (const job of ['template-created-app-real-stack', 'foundation-sample-e2e', 'real-stack-e2e',
    'real-stack-e2e-mysql', 'real-stack-e2e-production-totp', 'build-and-module-test',
    'affected-migration-recovery', 'integration-shard']) {
    const block = ci.split(`\n  ${job}:`)[1]?.split(/\n  [a-z][\w-]*:/)[0];
    assert.ok(block?.includes('node scripts/testing/prepare-ci-test-images.mjs'), `${job} 缺少镜像准备`);
  }
});
