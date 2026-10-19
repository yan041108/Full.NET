import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {randomUUID} from 'node:crypto';

function temporaryOutput(directory) {
  if (typeof directory !== 'string' || !path.isAbsolute(directory)) throw Error('Capacity output must be absolute');
  const resolved = path.resolve(directory);
  if (!/^fullnet-cache-capacity-[a-z0-9-]+$/.test(path.basename(resolved))
    || fs.realpathSync(path.dirname(resolved)) !== fs.realpathSync(os.tmpdir()))
    throw Error('Capacity output must be a dedicated direct child of system TEMP');
  return resolved;
}

/** 仅创建本轮结果目录；不复制源码、依赖或构建树，现存目录不能覆盖。 */
export function createCapacityOutput(directory, workspace) {
  const resolved = temporaryOutput(directory);
  const parent = fs.realpathSync(path.dirname(resolved));
  const relative = path.relative(fs.realpathSync(workspace), path.join(parent, path.basename(resolved)));
  if (!relative || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative)))
    throw Error('Capacity output cannot be inside the workspace');
  fs.mkdirSync(resolved);
  fs.writeFileSync(path.join(resolved, 'output-owner.json'), JSON.stringify({schemaVersion: 1,
    directory: fs.realpathSync(resolved), token: randomUUID()}), {flag: 'wx'});
}

/** 独立汇总已核对全部原始证据后调用；失败或资源未释放时保留诊断，不清理历史批次。 */
export function cleanVerifiedCapacityOutput(directory, state, terminal) {
  const markerPath = path.join(directory, 'output-owner.json');
  if (!fs.existsSync(markerPath)) return false;
  const resolved = temporaryOutput(directory);
  const marker = JSON.parse(fs.readFileSync(markerPath, 'utf8'));
  if (fs.lstatSync(resolved).isSymbolicLink() || marker.schemaVersion !== 1
    || marker.directory !== fs.realpathSync(resolved) || !/^[0-9a-f-]{36}$/.test(marker.token ?? ''))
    throw Error('Capacity output ownership changed');
  if (state.status !== 'completed-locks-released'
    || ['acceptance', 'cleanupConfirmed', 'resourceReleaseConfirmed', 'workloadCompleted'].some(key => state[key] !== true)
    || !Array.isArray(state.remainingNewContainers) || state.remainingNewContainers.length || state.volumeOwners !== ''
    || !Number.isSafeInteger(state.pid) || state.pid <= 0 || terminal.Pid !== state.pid
    || terminal.ExitCode !== 0 || terminal.ObservedViaLiveProcessHandle !== true)
    throw Error('Capacity process or resource completion has not been verified');
  // 清理只在已解析的本轮目录内进行；拒绝重解析点，不能借链接触及其他任务文件。
  function inspect(root) {
    for (const entry of fs.readdirSync(root, {withFileTypes: true})) {
      if (entry.isSymbolicLink()) throw Error('Capacity output contains a linked resource');
      if (entry.isDirectory()) inspect(path.join(root, entry.name));
    }
  }
  inspect(resolved);
  fs.rmSync(resolved, {recursive: true});
  return true;
}
