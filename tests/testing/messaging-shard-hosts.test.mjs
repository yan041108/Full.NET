import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { prepareMessagingShardHosts } from '../../scripts/testing/messaging-shard-hosts.mjs';

async function fixture(action) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-shard-host-test-'));
  try {
    for (const role of ['Api', 'Worker']) {
      const output = path.join(root, 'src', 'Hosts', `Full.NET.Host.${role}`, 'bin', 'Release', 'net10.0');
      await mkdir(output, { recursive: true });
      for (const file of [`Full.NET.Host.${role}.dll`, `Full.NET.Host.${role}.deps.json`, `Full.NET.Host.${role}.runtimeconfig.json`, 'appsettings.json'])
        await writeFile(path.join(output, file), '{}');
    }
    const evidence = path.join(root, 'evidence'); await mkdir(evidence);
    await action(root, { FULLNET_MESSAGING_EVIDENCE_DIR: evidence });
  } finally { await rm(root, { recursive: true, force: true }); }
}

test('正式分片复用两个实际默认构建目录，并拒绝运行期间配置漂移', async () => fixture(async (root, env) => {
  const hosts = await prepareMessagingShardHosts(root, env);
  assert.ok(hosts.env.FULLNET_MESSAGING_API_OUTPUT.endsWith(path.join('Full.NET.Host.Api', 'bin', 'Release', 'net10.0')));
  assert.ok(hosts.env.FULLNET_MESSAGING_WORKER_OUTPUT.endsWith(path.join('Full.NET.Host.Worker', 'bin', 'Release', 'net10.0')));
  await hosts.verify();
  await writeFile(path.join(hosts.env.FULLNET_MESSAGING_WORKER_OUTPUT, 'appsettings.json'), '{"changed":true}');
  await assert.rejects(hosts.verify(), /闭包.*变化/);
}));

test('显式隔离构建根缺少宿主时失败，不回退到默认旧二进制', async () => fixture(async (root, env) => {
  await assert.rejects(prepareMessagingShardHosts(root, { ...env, FULLNET_MESSAGING_BUILD_ROOT: path.join(root, 'missing') }), /ENOENT/);
}));

test('宿主路径由调用方显式指定时仍要求完整运行配置', async () => fixture(async (root, env) => {
  const worker = path.join(root, 'src', 'Hosts', 'Full.NET.Host.Worker', 'bin', 'Release', 'net10.0');
  await rm(path.join(worker, 'Full.NET.Host.Worker.runtimeconfig.json'));
  await assert.rejects(prepareMessagingShardHosts(root, { ...env, FULLNET_MESSAGING_WORKER_OUTPUT: worker }), /ENOENT/);
}));

test('继承的 Native 根不能绕过普通分片的 JIT 输入冻结', async () => fixture(async (root, env) => {
  for (const key of ['FULLNET_MESSAGING_NATIVE_API_ROOT', 'FULLNET_MESSAGING_NATIVE_WORKER_ROOT'])
    await assert.rejects(prepareMessagingShardHosts(root, { ...env, [key]: 'untracked-native-output' }), /普通 JIT.*Native/);
}));
