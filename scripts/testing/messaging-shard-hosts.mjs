import { mkdtemp, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { binaryIdentity } from './run-messaging-reliability.mjs';

/** 复用调用方已经构建的实际宿主；验证不负责隐式构建或回退到源码配置。 */
export async function prepareMessagingShardHosts(cwd, environment = process.env) {
  const env = { ...environment };
  if (['FULLNET_MESSAGING_NATIVE_API_ROOT', 'FULLNET_MESSAGING_NATIVE_WORKER_ROOT'].some(key => env[key]?.trim()))
    throw new Error('普通 JIT 分片不能继承 Native 根；原生验收必须使用冻结两份发布目录的专属入口。');
  const outputs = {};
  for (const role of ['Api', 'Worker']) {
    const key = `FULLNET_MESSAGING_${role.toUpperCase()}_OUTPUT`;
    const directory = env[key] ?? (env.FULLNET_MESSAGING_BUILD_ROOT
      ? path.join(env.FULLNET_MESSAGING_BUILD_ROOT, 'bin', `Full.NET.Host.${role}`, 'release')
      : path.join(cwd, 'src', 'Hosts', `Full.NET.Host.${role}`, 'bin', 'Release', 'net10.0'));
    for (const file of [`Full.NET.Host.${role}.dll`, `Full.NET.Host.${role}.deps.json`, `Full.NET.Host.${role}.runtimeconfig.json`, 'appsettings.json'])
      await readFile(path.join(directory, file));
    outputs[role] = path.resolve(directory); env[key] = outputs[role];
  }
  const identity = async () => Object.fromEntries(await Promise.all(Object.entries(outputs).map(async ([role, directory]) => [role, await binaryIdentity(directory)])));
  const before = await identity();
  env.FULLNET_MESSAGING_EVIDENCE_DIR ??= await mkdtemp(path.join(os.tmpdir(), 'fullnet-messaging-shard-'));
  await writeFile(path.join(env.FULLNET_MESSAGING_EVIDENCE_DIR, 'shard-host-inputs.json'), JSON.stringify({ outputs, before }, null, 2));
  return { env, async verify() {
    if (JSON.stringify(before) !== JSON.stringify(await identity())) throw new Error('Messaging 分片实际 API/Worker 闭包在运行期间变化。');
  } };
}
