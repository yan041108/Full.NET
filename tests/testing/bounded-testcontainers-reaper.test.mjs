import assert from 'node:assert/strict';
import net from 'node:net';
import { once } from 'node:events';
import test from 'node:test';
import { abortBoundedResourceReaper, boundedRyukImage, connectReaper, startBoundedResourceReaper, validateBoundedService } from '../../scripts/testing/bounded-testcontainers-reaper.mjs';

const session = '3e8af30a-dcbb-4ba0-82ce-15c4041ae14a';
const id = 'b'.repeat(64), imageId = `sha256:${'a'.repeat(64)}`;
function fixture({ mismatch = false, wrongImage = false } = {}) {
  const commands = [], events = [];
  let running = false;
  return { commands, events, options: {
    session, journal: row => events.push(row), onDisconnect() {},
    async docker(args) {
      commands.push(args);
      if (args[0] === 'image') return JSON.stringify([{ Id: imageId, Os: 'linux', Architecture: wrongImage ? 'arm64' : 'amd64', RepoDigests: [boundedRyukImage] }]);
      if (args[0] === 'create') return id;
      if (args[0] === 'start') { running = true; return id; }
      if (args[1] === 'inspect') return JSON.stringify([{
        Id: id, Image: imageId, Config: { Labels: { 'fullnet.bounded-reaper': session } },
        HostConfig: { Memory: mismatch ? 0 : 268435456, MemorySwap: 268435456, NanoCpus: 500000000, AutoRemove: true },
        State: { Status: running ? 'running' : 'created', Running: running },
        NetworkSettings: { Ports: { '8080/tcp': [{ HostIp: '127.0.0.1', HostPort: '51234' }] } }
      }]);
      if (args[1] === 'ls') return '';
      throw new Error(`Unexpected command ${args}`);
    },
    async connect(port, filter) {
      assert.equal(port, 51234);
      assert.equal(filter, `label=org.testcontainers.resource-reaper-session=${session}&label=fullnet.bounded-owner=${session}\n`);
      return { close() { events.push({ stage: 'connection-closed' }); } };
    }
  } };
}

test('限额在启动前读回，ACK 后才提供业务环境；清理只核对双标签本批资源', async () => {
  const f = fixture();
  const lease = await startBoundedResourceReaper(f.options);
  assert.equal(f.commands[2][1], 'inspect');
  assert.equal(f.commands[3][0], 'start');
  assert.ok(f.commands[1].includes('--pull=never'));
  assert.ok(f.commands[1].includes('--memory-swap'));
  assert.equal(lease.environment.FULLNET_BOUNDED_RESOURCE_SESSION, session);
  assert.equal(f.events.at(-1).stage, 'reaper-ack');
  await lease.close();
  assert.equal(f.events.at(-1).stage, 'reaper-clean');
  assert.ok(f.commands.filter(x => x[1] === 'ls').every(x => x.some(a => a.includes(session))));
  assert.ok(!f.commands.some(x => ['rm', 'prune', 'update'].includes(x[0])));
});

test('实际内存上限不匹配时禁止 start 和业务准入', async () => {
  const f = fixture({ mismatch: true });
  await assert.rejects(startBoundedResourceReaper(f.options), /硬限额/);
  assert.ok(!f.commands.some(x => x[0] === 'start'));
});

test('镜像平台不匹配或会话非 UUID 时不创建资源', async () => {
  const f = fixture({ wrongImage: true });
  await assert.rejects(startBoundedResourceReaper(f.options), /镜像身份/);
  await assert.rejects(startBoundedResourceReaper({ ...f.options, session: 'shared' }), /UUID/);
  assert.ok(!f.commands.some(x => x[0] === 'create'));
});

test('缺少 ACK 时不返回业务环境并保留已创建身份', async () => {
  const f = fixture();
  f.options.connect = async () => { throw new Error('ACK absent'); };
  await assert.rejects(startBoundedResourceReaper(f.options), /ACK absent/);
  assert.equal(f.events.find(x => x.stage === 'reaper-created').id, id);
  assert.ok(!f.events.some(x => x.stage === 'reaper-ack'));
});

test('清理超时不能登记 clean 或删除共享资源', async () => {
  const f = fixture();
  const original = f.options.docker;
  f.options.docker = args => args[1] === 'ls' ? Promise.resolve(id) : original(args);
  const lease = await startBoundedResourceReaper(f.options);
  await assert.rejects(lease.close({ timeoutMs: 0 }), /尚未清理/);
  assert.ok(!f.events.some(x => x.stage === 'reaper-clean'));
});

test('创建中断只能删除精确 ID 且归属匹配的 Ryuk', async () => {
  const f = fixture();
  const original = f.options.docker;
  f.options.docker = args => args[1] === 'rm' ? (f.commands.push(args), Promise.resolve(id)) : original(args);
  await abortBoundedResourceReaper({ ...f.options, id });
  assert.deepEqual(f.commands.find(x => x[1] === 'rm'), ['container', 'rm', '-f', '-v', id]);
  const g = fixture();
  await assert.rejects(abortBoundedResourceReaper({ ...g.options, session: '9'.repeat(36), id }), /归属/);
  assert.ok(!g.commands.some(x => x[1] === 'rm'));
});

test('ACK 凭证持久化失败必须关闭连接且不交付业务环境', async () => {
  const f = fixture();
  f.options.journal = row => { if (row.stage === 'reaper-ack') throw new Error('journal failed'); };
  await assert.rejects(startBoundedResourceReaper(f.options), /journal failed/);
  assert.equal(f.events.at(-1).stage, 'connection-closed');
});

test('ACK 后先断连再申请启动时，不得启动业务或交付环境', async () => {
  const f = fixture();
  let disconnect;
  f.options.connect = async (_port, _filter, onDisconnect) => {
    disconnect = onDisconnect;
    return { close() {} };
  };
  const lease = await startBoundedResourceReaper(f.options);
  let started = 0;
  disconnect();
  assert.throws(() => { lease.assertReady(); started++; }, /禁止启动/);
  assert.throws(() => lease.environment, /禁止启动/);
  assert.equal(started, 0);
});

test('ACK 返回边界已断连时直接拒绝准入并关闭连接', async () => {
  const f = fixture();
  let closed = false;
  f.options.connect = async (_port, _filter, onDisconnect) => {
    onDisconnect();
    return { close() { closed = true; } };
  };
  await assert.rejects(startBoundedResourceReaper(f.options), /禁止启动/);
  assert.equal(closed, true);
  assert.ok(!f.events.some(x => x.stage === 'reaper-ack'));
});

function sqlService() {
  return { Id: id, Config: { Image: 'mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04',
    Labels: { 'org.testcontainers.resource-reaper-session': session, 'fullnet.bounded-owner': session },
    Env: ['MSSQL_MEMORY_LIMIT_MB=2048'] },
    HostConfig: { Memory: 3 * 1024 ** 3, MemorySwap: 3 * 1024 ** 3, NanoCpus: 2_000_000_000 },
    State: { Running: true, OOMKilled: false, Dead: false } };
}

test('SQL Server 在容器硬上限内使用明确的内部预算', () => {
  assert.deepEqual(validateBoundedService(sqlService(), session), { id, image: sqlService().Config.Image });
  const missing = sqlService(); missing.Config.Env = [];
  assert.throws(() => validateBoundedService(missing, session), /内部内存预算/);
});

test('限额仍正确但实例已 OOM 时必须停止依赖验收', () => {
  const actual = sqlService(); actual.State.OOMKilled = true; actual.State.Running = false;
  assert.throws(() => validateBoundedService(actual, session), /OOM/);
});

test('错误归属或交换预算扩大不能通过业务资源检查', () => {
  const foreign = sqlService(); foreign.Config.Labels['fullnet.bounded-owner'] = 'foreign';
  assert.throws(() => validateBoundedService(foreign, session), /归属/);
  const swap = sqlService(); swap.HostConfig.MemorySwap += 1024;
  assert.throws(() => validateBoundedService(swap, session), /硬限额/);
  const unknown = sqlService(); unknown.Config.Image = '__proto__'; unknown.HostConfig = {};
  assert.throws(() => validateBoundedService(unknown, session), /硬限额/);
});

test('真实 TCP 分段 ACK 后断连触发停止回调', async t => {
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    socket.once('data', () => { socket.write('A'); setImmediate(() => socket.write('CK\n')); });
  });
  t.after(() => { for (const socket of sockets) socket.destroy(); server.close(); });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  let disconnected;
  const signal = new Promise(resolve => { disconnected = resolve; });
  const lease = await connectReaper(server.address().port, 'label=owned=yes\n', disconnected);
  for (const socket of sockets) socket.destroy();
  await signal;
  lease.close();
});
