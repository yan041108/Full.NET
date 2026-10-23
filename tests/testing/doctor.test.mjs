import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { assessVersion, inspectEnvironment, parseArguments, probePort } from '../../scripts/doctor.mjs';

async function fixture(t) {
  const root = await mkdtemp(path.join(tmpdir(), 'fullnet-doctor-test-'));
  // 只回收本测试创建的唯一临时目录，避免影响真实工作区与其他会话。
  t.after(() => rm(root, { recursive: true, force: true }));
  await writeFile(path.join(root, 'package.json'), JSON.stringify({ packageManager: 'pnpm@10.26.0' }));
  await writeFile(path.join(root, '.nvmrc'), process.versions.node.split('.')[0]);
  await writeFile(path.join(root, 'global.json'), JSON.stringify({ sdk: { version: '10.0.100', rollForward: 'latestFeature' } }));
  return root;
}

function healthyProbe(name) {
  return { ok: true, executable: `/tools/${name}`, output: {
    pnpm: '10.26.0', git: 'git version 2.50.0', dotnet: '10.0.300', docker: '29.0.0'
  }[name] };
}

test('版本检查拒绝 pnpm 主版本漂移和无效 Node 输出', () => {
  assert.equal(assessVersion('11.25.0', '10.26.0'), false);
  assert.equal(assessVersion('10.26.0', '10.26.0'), true);
  assert.equal(assessVersion('v24.20.0', '24', true), true);
  assert.equal(assessVersion('24.bad', '24', true), false);
  assert.equal(assessVersion('v22.0.0', '24', true), false);
});

test('未知参数和缺少范围值不能静默回退为完整检查', () => {
  assert.deepEqual(parseArguments(['--', '--profile', 'frontend', '--json']), { profile: 'frontend', json: true });
  assert.throws(() => parseArguments(['--profile']));
  assert.throws(() => parseArguments(['--repair']));
});

test('后端模式接受 global.json 选择的 SDK 且不检查浏览器或写入配置', async t => {
  const root = await fixture(t);
  const before = await readFile(path.join(root, 'package.json'), 'utf8');
  const calls = [];
  const report = await inspectEnvironment(root, 'backend', {
    probe: (name, args, cwd) => { calls.push({ name, args, cwd }); return healthyProbe(name); },
    probePort: () => { throw new Error('后端模式不应检查浏览器端口'); }
  });
  assert.equal(report.ok, true);
  assert.deepEqual(calls.map(call => call.name), ['pnpm', 'git', 'dotnet', 'docker']);
  assert.ok(calls.every(call => call.cwd === root));
  assert.equal(await readFile(path.join(root, 'package.json'), 'utf8'), before);
  assert.deepEqual((await readdir(root)).sort(), ['.nvmrc', 'global.json', 'package.json']);
});

test('Docker 不可用和 pnpm 错误版本均阻塞，但不自动启动或安装', async t => {
  const root = await fixture(t);
  const report = await inspectEnvironment(root, 'backend', {
    probe: name => name === 'docker'
      ? { ok: false, output: '', executable: '/tools/docker', reason: '检查超时' }
      : name === 'pnpm' ? { ...healthyProbe(name), output: '11.25.0' } : healthyProbe(name)
  });
  assert.equal(report.ok, false);
  assert.deepEqual(report.checks.filter(check => check.status === 'fail').map(check => check.name), ['pnpm', 'docker-engine']);
});

test('缺失 .NET SDK 不能因其他工具正常而报告环境通过', async t => {
  const root = await fixture(t);
  const report = await inspectEnvironment(root, 'backend', {
    probe: name => name === 'dotnet'
      ? { ok: false, output: '', executable: null, reason: 'PATH 中未找到工具' } : healthyProbe(name)
  });
  assert.equal(report.ok, false);
  assert.equal(report.checks.find(check => check.name === 'dotnet-sdk').status, 'fail');
});

test('回环端口探测区分监听与关闭，且不会关闭原服务', async t => {
  const server = createServer(socket => socket.end());
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { if (server.listening) server.close(); });
  const { port } = server.address();
  assert.equal(await probePort('127.0.0.1', port), 'listening');
  assert.equal(server.listening, true);
  await new Promise(resolve => server.close(resolve));
  assert.equal(await probePort('127.0.0.1', port), 'free');
});
