import assert from 'node:assert/strict';
import { afterEach, beforeEach, test } from 'node:test';
import { createRequire } from 'node:module';
import { realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { startDatabaseContainer, startRedisContainer } from './support/created-app-real-stack.mjs';
import * as minimal from './support/created-app-real-stack.mjs';
import * as delivery from './support/created-enterprise-data-delivery.mjs';

const requireFromStack = createRequire(new URL('../e2e/admin-real-stack/package.json', import.meta.url));
const { GenericContainer } = requireFromStack('testcontainers');
let originalStart;
let originalProfile;
let created;

beforeEach(() => {
  originalStart = GenericContainer.prototype.start;
  originalProfile = process.env.FULLNET_TEMPLATE_TEST_RESOURCE_PROFILE;
  process.env.FULLNET_TEMPLATE_TEST_RESOURCE_PROFILE = 'bounded-local';
  created = undefined;
  // 只替换启动动作，实际 SDK 配额转换仍运行，不创建容器或访问 Docker。
  GenericContainer.prototype.start = async function () {
    created = { ...this.hostConfig };
    return { getHost: () => '127.0.0.1', getMappedPort: port => port };
  };
});

afterEach(() => {
  GenericContainer.prototype.start = originalStart;
  if (originalProfile === undefined) delete process.env.FULLNET_TEMPLATE_TEST_RESOURCE_PROFILE;
  else process.env.FULLNET_TEMPLATE_TEST_RESOURCE_PROFILE = originalProfile;
});

for (const [role, memoryGiB, cpu] of [
  ['sqlserver', 3, 1],
  ['mysql', 1.5, 1],
  ['redis', 0.25, 0.25],
]) {
  test(`bounded local ${role} applies actual SDK memory and CPU before start`, async () => {
    if (role === 'redis') await startRedisContainer();
    else await startDatabaseContainer(role);
    assert.equal(created.Memory, memoryGiB * 1024 ** 3);
    assert.equal(created.NanoCpus, cpu * 10 ** 9);
  });
}

test('an unknown local profile refuses to start a container', async () => {
  process.env.FULLNET_TEMPLATE_TEST_RESOURCE_PROFILE = 'bounded-lcoal';
  await assert.rejects(startRedisContainer(), /resource profile/iu);
  assert.equal(created, undefined);
});

test('an absent local profile preserves the existing execution mode', async () => {
  delete process.env.FULLNET_TEMPLATE_TEST_RESOURCE_PROFILE;
  await startRedisContainer();
  assert.equal(created.Memory, undefined);
  assert.equal(created.NanoCpus, undefined);
});

test('enterprise delivery evidence resolves inside system temp and outside the repository', () => {
  assert.equal(typeof delivery.resolveDataDeliveryReportParent, 'function');
  const parent = delivery.resolveDataDeliveryReportParent('mysql');
  const relative = path.relative(realpathSync.native(tmpdir()), parent);
  assert.ok(relative && !relative.startsWith('..') && !path.isAbsolute(relative));
  assert.equal(path.basename(parent), 'mysql');
  const repository = path.resolve(import.meta.dirname, '../..');
  const fromRepository = path.relative(repository, parent);
  // Windows 跨盘返回绝对路径，不能只用 ../ 判断目录归属。
  assert.ok(path.isAbsolute(fromRepository) || fromRepository.startsWith(`..${path.sep}`));
});

test('minimal application evidence resolves inside system temp for each supported provider', () => {
  assert.equal(typeof minimal.resolveCreatedAppReportParent, 'function');
  for (const provider of ['sqlserver', 'mysql']) {
    const parent = minimal.resolveCreatedAppReportParent(provider);
    const relative = path.relative(realpathSync.native(tmpdir()), parent);
    assert.ok(relative && !relative.startsWith('..') && !path.isAbsolute(relative));
    assert.equal(path.basename(parent), provider);
  }
  assert.throws(() => minimal.resolveCreatedAppReportParent('../outside'));
});
