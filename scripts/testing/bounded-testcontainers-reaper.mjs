import net from 'node:net';
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';

export const boundedRyukImage = 'testcontainers/ryuk:0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0';
const sessionLabel = 'org.testcontainers.resource-reaper-session';
const ownerLabel = 'fullnet.bounded-owner';
const reaperLabel = 'fullnet.bounded-reaper';
const memory = 256 * 1024 * 1024;

/** 内核限额不能替代引擎内部预算；持续监测 OOM，避免依赖已退出时仍继续级联验收。 */
export function validateBoundedService(actual, session) {
  const budgets = {
    'mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04': [3 * 1024 ** 3, 2_000_000_000],
    'mysql:8.0': [1536 * 1024 ** 2, 1_000_000_000],
    'redis:8.6': [256 * 1024 ** 2, 500_000_000]
  };
  const image = actual.Config?.Image, budget = Object.hasOwn(budgets, image) ? budgets[image] : null;
  if (!budget || !/^[a-f0-9]{64}$/.test(actual.Id ?? '')
      || actual.Config?.Labels?.[sessionLabel] !== session || actual.Config.Labels[ownerLabel] !== session
      || actual.HostConfig?.Memory !== budget[0] || actual.HostConfig.MemorySwap !== budget[0]
      || actual.HostConfig.NanoCpus !== budget[1]) throw new Error('业务容器实际归属或硬限额不匹配');
  if (image.startsWith('mcr.microsoft.com/mssql/') && !actual.Config.Env?.includes('MSSQL_MEMORY_LIMIT_MB=2048')) {
    throw new Error('SQL Server 缺少已约定的内部内存预算');
  }
  if (actual.State?.OOMKilled === true || actual.State?.Dead === true) throw new Error('业务容器已 OOM 或死亡，停止依赖验收');
  return { id: actual.Id, image };
}

/** 握手前不允许业务容器创建；握手后连接丢失必须停止业务，避免回收正在使用的资源。 */
export async function connectReaper(port, filter, onDisconnect, { timeoutMs = 30_000 } = {}) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    let socket;
    try {
      socket = net.createConnection({ host: '127.0.0.1', port });
      await new Promise((resolve, reject) => {
        const finish = (error) => { clearTimeout(timer); if (error) reject(error); else resolve(); };
        const timer = setTimeout(() => finish(new Error('Ryuk ACK 超时')), Math.min(2000, deadline - Date.now()));
        let response = '';
        socket.once('error', finish);
        socket.once('close', () => finish(new Error('Ryuk 在 ACK 前断开')));
        socket.once('connect', () => socket.write(filter));
        socket.on('data', chunk => {
          response += chunk.toString('utf8');
          if (response.length > 64 || (response.includes('\n') && response !== 'ACK\n')) finish(new Error('Ryuk ACK 无效'));
          if (response === 'ACK\n') finish();
        });
      });
      socket.removeAllListeners('data');
      socket.removeAllListeners('error');
      socket.removeAllListeners('close');
      let closing = false;
      socket.on('error', () => {});
      socket.once('close', () => { if (!closing) onDisconnect(); });
      return { close() { closing = true; socket.destroy(); } };
    } catch (error) {
      socket?.destroy();
      if (Date.now() >= deadline) throw error;
      await delay(Math.min(100, deadline - Date.now()));
    }
  }
  throw new Error('Ryuk 握手预算耗尽');
}

/** 外层必须已获双锁及资源窗口；docker 参数逐项传递，禁止隐式拉取和修改共享资源。 */
export async function startBoundedResourceReaper({ docker, journal, onDisconnect,
  connect = connectReaper, session = randomUUID() }) {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(session)) {
    throw new Error('有界资源会话必须是规范的非空 UUID');
  }
  if (typeof journal !== 'function' || typeof onDisconnect !== 'function') throw new Error('缺少持久记录或断连停止处理');
  const name = `fullnet-bounded-ryuk-${session}`;
  const filters = ['--filter', `label=${sessionLabel}=${session}`, '--filter', `label=${ownerLabel}=${session}`];
  const images = JSON.parse(await docker(['image', 'inspect', boundedRyukImage]));
  const image = images[0];
  if (images.length !== 1 || image.Os !== 'linux' || image.Architecture !== 'amd64'
      || !image.RepoDigests?.some(x => x.endsWith(boundedRyukImage.slice(boundedRyukImage.indexOf('@'))))) {
    throw new Error('Ryuk 本地镜像身份不符合原定平台/digest');
  }
  await journal({ stage: 'reaper-intent', session, name, imageId: image.Id });
  const id = (await docker(['create', '--pull=never', '--rm', '--name', name,
    '--label', `${reaperLabel}=${session}`, '--memory', String(memory), '--memory-swap', String(memory),
    '--cpus', '0.5', '--publish', '127.0.0.1::8080',
    '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock',
    '--env', 'RYUK_RECONNECTION_TIMEOUT=10s', '--env', 'RYUK_SHUTDOWN_TIMEOUT=2m', image.Id])).trim();
  // 创建请求不明确时只保留 intent，不能猜测 ID 或删除同名外部资源。
  if (!/^[a-f0-9]{64}$/.test(id)) throw new Error('Ryuk 创建结果缺少精确容器 ID');
  await journal({ stage: 'reaper-created', session, id });
  const inspect = async () => {
    const values = JSON.parse(await docker(['container', 'inspect', id]));
    const actual = values[0];
    if (values.length !== 1 || actual.Id !== id || actual.Image !== image.Id
        || actual.Config?.Labels?.[reaperLabel] !== session
        || actual.HostConfig?.Memory !== memory || actual.HostConfig.MemorySwap !== memory
        || actual.HostConfig.NanoCpus !== 500_000_000 || !actual.HostConfig.AutoRemove) {
      throw new Error('Ryuk 实际归属或硬限额不匹配');
    }
    return actual;
  };
  const created = await inspect();
  if (created.State?.Status !== 'created') throw new Error('Ryuk 未处于可验证的未启动状态');
  await journal({ stage: 'reaper-budget-verified', session, id, hostConfig: created.HostConfig });
  await docker(['start', id]);
  const running = await inspect();
  const bindings = running.NetworkSettings?.Ports?.['8080/tcp'];
  if (!running.State?.Running || bindings?.length !== 1 || bindings[0].HostIp !== '127.0.0.1') throw new Error('Ryuk 运行端口不符合本机独享边界');
  const port = Number(bindings[0].HostPort);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Ryuk 实际端口无效');
  let disconnected = false, closed = false;
  const connection = await connect(port, `label=${sessionLabel}=${session}&label=${ownerLabel}=${session}\n`, () => {
    disconnected = true;
    onDisconnect();
  });
  const assertReady = () => {
    if (disconnected || closed) throw new Error('Ryuk 连接已失效，禁止启动业务');
  };
  try { assertReady(); } catch (error) { connection.close(); throw error; }
  try { await journal({ stage: 'reaper-ack', session, id, port }); }
  catch (error) { connection.close(); throw error; }
  try { assertReady(); } catch (error) { connection.close(); throw error; }
  return {
    session, id, assertReady,
    get environment() {
      assertReady();
      return { FULLNET_TESTCONTAINERS_BOUNDED: '1', FULLNET_TESTCONTAINERS_REUSE: '0',
        TESTCONTAINERS_REUSE_ENABLE: 'false', TESTCONTAINERS_RYUK_DISABLED: 'true', FULLNET_BOUNDED_RESOURCE_SESSION: session };
    },
    async close({ timeoutMs = 120_000 } = {}) {
      closed = true;
      connection.close();
      const deadline = Date.now() + timeoutMs;
      do {
        const resources = {};
        for (const [kind, args] of Object.entries({ containers: ['container', 'ls', '-aq'], networks: ['network', 'ls', '-q'], volumes: ['volume', 'ls', '-q'] })) {
          resources[kind] = (await docker([...args, ...filters])).trim().split(/\s+/).filter(Boolean);
        }
        const reapers = (await docker(['container', 'ls', '-aq', '--filter', `label=${reaperLabel}=${session}`])).trim();
        if (Object.values(resources).every(x => x.length === 0) && !reapers) {
          await journal({ stage: 'reaper-clean', session, id, resources });
          return;
        }
        if (Date.now() >= deadline) throw new Error('本批资源尚未清理完毕，保留锁与原始凭证');
        await delay(Math.min(1000, deadline - Date.now()));
      } while (true);
    }
  };
}

/** 业务尚未启动时回收创建中断的自有 Ryuk；ID 与随机归属必须同时匹配，禁止按名称或前后差集删除。 */
export async function abortBoundedResourceReaper({ docker, session, id, journal }) {
  if (!/^[a-f0-9]{64}$/.test(id ?? '') || !/^[a-f0-9-]{36}$/.test(session ?? '')) throw new Error('缺少精确创建凭证');
  const observed = JSON.parse(await docker(['container', 'inspect', id]));
  if (observed.length !== 1 || observed[0].Id !== id || observed[0].Config?.Labels?.[reaperLabel] !== session) {
    throw new Error('中断清理的容器归属不匹配');
  }
  await docker(['container', 'rm', '-f', '-v', id]);
  const remaining = (await docker(['container', 'ls', '-aq', '--filter', `label=${reaperLabel}=${session}`])).trim();
  if (remaining) throw new Error('中断清理后仍存在本批 Ryuk');
  await journal({ stage: 'reaper-aborted-before-business', session, id });
}
