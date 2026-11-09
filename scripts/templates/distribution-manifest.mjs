import { lstatSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { assertUnlinkedPath, hashBytes } from './framework-upgrade-integrity.mjs';

const manifestName = 'distribution-manifest.json';

function describeDistribution(root) {
  const files = new Map();
  const visit = (directory, prefix = '') => {
    for (const name of readdirSync(directory).sort()) {
      const path = prefix + name;
      const absolute = join(directory, name);
      const entry = lstatSync(absolute);
      if (entry.isSymbolicLink()) throw new Error('Distribution contains a symbolic link: ' + path);
      // 受管框架已由 framework-manifest.json 逐文件验证，外层清单只记录该清单及模板文件。
      if (entry.isDirectory()) {
        if (path !== 'framework/fullnet') visit(absolute, path + '/');
      } else if (!entry.isFile()) {
        throw new Error('Unsupported distribution entry: ' + path);
      } else if (path !== manifestName) {
        files.set(path, hashBytes(readFileSync(absolute)));
      }
    }
  };
  assertUnlinkedPath(root);
  visit(root);
  const framework = JSON.parse(readFileSync(join(root, 'framework-manifest.json'), 'utf8'));
  if (!/^[a-f0-9]{40}$/.test(framework.sourceCommit ?? '')) throw new Error('Invalid distribution source commit');
  return { schemaVersion: 1, sourceCommit: framework.sourceCommit, frameworkVersion: framework.frameworkVersion,
    files: Object.fromEntries(files) };
}

export function writeDistributionManifest(root) {
  const bytes = JSON.stringify(describeDistribution(root), null, 2) + '\n';
  writeFileSync(join(root, manifestName), bytes, { flag: 'wx' });
  return hashBytes(bytes);
}

/** 候选摘要来自受信任的交付记录；旧包仍由原受管框架与前端校验保护。 */
export function verifyDistributionManifest(root, expectedDigest) {
  if (expectedDigest !== undefined && !/^[a-f0-9]{64}$/.test(expectedDigest)) {
    throw new Error('Invalid distribution manifest SHA-256');
  }
  const file = join(root, manifestName);
  if (!assertUnlinkedPath(file, true)) {
    if (expectedDigest !== undefined) throw new Error('Distribution manifest is missing');
    return;
  }
  if (!lstatSync(file).isFile()) throw new Error('Distribution manifest must be a file');
  const bytes = readFileSync(file);
  if (expectedDigest !== undefined && hashBytes(bytes) !== expectedDigest) {
    throw new Error('Distribution manifest digest mismatch');
  }
  if (!isDeepStrictEqual(JSON.parse(bytes), describeDistribution(root))) {
    throw new Error('Distribution files do not match the manifest');
  }
}
