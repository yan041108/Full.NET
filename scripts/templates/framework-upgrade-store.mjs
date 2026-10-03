/** 使用独占锁、同卷暂存和保留备份提交框架升级；中断时保留恢复入口。 */
import { closeSync, cpSync, existsSync, mkdirSync, mkdtempSync, openSync, readFileSync, renameSync, unlinkSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { assertFrameworkTreeUnlinked, assertUnlinkedPath, hashBytes, readFrameworkSnapshot } from './framework-upgrade-integrity.mjs';

export const upgradeLockPath = (appRoot) => join(appRoot, '.fullnet-upgrade.lock');

export function assertNoPendingUpgrade(appRoot) {
  if (existsSync(upgradeLockPath(appRoot))) {
    throw new Error('An upgrade is active or interrupted; inspect ' + upgradeLockPath(appRoot) + ' and its recoveryRoot before retrying');
  }
}

/** 不触碰应用宿主和业务文件；旧框架与清单在成功或失败后均保留。 */
export function commitFrameworkUpgrade({ appRoot, files, nextManifest, expectedSnapshot, verifyUnchanged }) {
  const frameworkRoot = join(appRoot, 'framework/fullnet');
  const manifestPath = join(appRoot, 'framework-manifest.json');
  assertFrameworkTreeUnlinked(frameworkRoot);
  const lockPath = upgradeLockPath(appRoot);
  const descriptor = openSync(lockPath, 'wx');
  let recoveryRoot;
  let frameworkMoved = false;
  let frameworkInstalled = false;
  let manifestMoved = false;
  let manifestInstalled = false;
  let safeToUnlock = false;
  try {
    recoveryRoot = mkdtempSync(join(dirname(frameworkRoot), '.fullnet-upgrade-'));
    const nextRoot = join(recoveryRoot, 'next');
    const nextManifestPath = join(recoveryRoot, 'next-manifest.json');
    const encoded = JSON.stringify(nextManifest, null, 2) + '\n';
    const nextEntries = new Map(Object.entries(expectedSnapshot));
    for (const [path, content] of files) {
      const segments = path.split('/');
      for (let count = 1; count < segments.length; count += 1) nextEntries.set(segments.slice(0, count).join('/'), 'directory');
      nextEntries.set(path, hashBytes(content));
    }
    nextEntries.set('framework-manifest.json', hashBytes(encoded));
    const expectedNextSnapshot = Object.fromEntries(nextEntries);
    // 先记录恢复位置，进程中断后禁止下一次升级猜测当前版本。
    writeFileSync(descriptor, JSON.stringify({ recoveryRoot, state: 'pending' }) + '\n');
    cpSync(frameworkRoot, nextRoot, { recursive: true, force: false, errorOnExist: true });
    const assertSnapshot = (root) => {
      if (!isDeepStrictEqual(readFrameworkSnapshot(root), expectedSnapshot)) {
        throw new Error('Framework snapshot changed during upgrade; original content is retained');
      }
    };
    const assertPreviousManifest = () => {
      if (hashBytes(readFileSync(join(recoveryRoot, 'previous-manifest.json'))) !== expectedSnapshot['framework-manifest.json']) {
        throw new Error('Application manifest snapshot changed during upgrade');
      }
    };
    const assertNextSnapshot = (root) => {
      if (!isDeepStrictEqual(readFrameworkSnapshot(root), expectedNextSnapshot)) {
        throw new Error('New framework snapshot changed during upgrade; changed content is retained');
      }
    };
    assertSnapshot(nextRoot);
    for (const [relative, content] of files) {
      const target = join(nextRoot, relative);
      assertUnlinkedPath(target, true);
      mkdirSync(dirname(target), { recursive: true });
      writeFileSync(target, content);
    }
    writeFileSync(join(nextRoot, 'framework-manifest.json'), encoded);
    writeFileSync(nextManifestPath, encoded);
    assertNextSnapshot(nextRoot);
    // 暂存可能耗时；发布前再次核对预览输入，防止人工修改被覆盖。
    verifyUnchanged();
    renameSync(frameworkRoot, join(recoveryRoot, 'previous'));
    frameworkMoved = true;
    assertSnapshot(join(recoveryRoot, 'previous'));
    if (existsSync(frameworkRoot)) throw new Error('Framework directory reappeared after claim; manual recovery is required');
    renameSync(nextRoot, frameworkRoot);
    frameworkInstalled = true;
    assertNextSnapshot(frameworkRoot);
    renameSync(manifestPath, join(recoveryRoot, 'previous-manifest.json'));
    manifestMoved = true;
    assertPreviousManifest();
    assertSnapshot(join(recoveryRoot, 'previous'));
    assertNextSnapshot(frameworkRoot);
    if (existsSync(manifestPath)) throw new Error('Framework manifest reappeared after claim; manual recovery is required');
    renameSync(nextManifestPath, manifestPath);
    manifestInstalled = true;
    assertPreviousManifest();
    assertSnapshot(join(recoveryRoot, 'previous'));
    assertNextSnapshot(frameworkRoot);
    if (hashBytes(readFileSync(manifestPath)) !== hashBytes(encoded)) throw new Error('New application manifest snapshot changed during upgrade');
    writeFileSync(join(recoveryRoot, 'state.json'), JSON.stringify({ state: 'committed', targetVersion: nextManifest.frameworkVersion }) + '\n');
    safeToUnlock = true;
    return recoveryRoot;
  } catch (error) {
    try {
      // 新目录只移动到恢复区，不删除；失败期间出现的内容同样保留。
      if (manifestInstalled) renameSync(manifestPath, join(recoveryRoot, 'failed-manifest.json'));
      if (manifestMoved) {
        if (existsSync(manifestPath)) throw new Error('Cannot overwrite a reappeared application manifest');
        renameSync(join(recoveryRoot, 'previous-manifest.json'), manifestPath);
      }
      if (frameworkInstalled) renameSync(frameworkRoot, join(recoveryRoot, 'failed'));
      if (frameworkMoved) {
        if (existsSync(frameworkRoot)) throw new Error('Cannot overwrite a reappeared framework directory');
        renameSync(join(recoveryRoot, 'previous'), frameworkRoot);
      }
      if (recoveryRoot) writeFileSync(join(recoveryRoot, 'state.json'), JSON.stringify({ state: 'rolled-back' }) + '\n');
      safeToUnlock = true;
    } catch (rollbackError) {
      throw new AggregateError([error, rollbackError], 'Upgrade recovery requires manual intervention at ' + recoveryRoot);
    }
    if (recoveryRoot) {
      error.recoveryRoot = recoveryRoot;
      error.message += ' Recovery materials: ' + recoveryRoot;
    }
    throw error;
  } finally {
    closeSync(descriptor);
    // 只移除本次独占创建的运行锁，框架备份和未知恢复目录不自动清理。
    if (safeToUnlock) unlinkSync(lockPath);
  }
}
