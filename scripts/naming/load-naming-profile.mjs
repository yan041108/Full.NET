import { readFile, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

const readFileAsync = promisify(readFile);
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

/** 读取并校验跨工具共享的 Naming Profile。 */
export async function loadNamingProfile(root = repositoryRoot) {
  return validateProfile(JSON.parse(await readFileAsync(
    path.join(root, 'contracts/naming/fullnet-naming-profile.json'),
    'utf8'
  )));
}

/** 为纯同步构建器读取同一份 Naming Profile，避免维护第二套常量。 */
export function loadNamingProfileSync(root = repositoryRoot) {
  return validateProfile(JSON.parse(readFileSync(
    path.join(root, 'contracts/naming/fullnet-naming-profile.json'),
    'utf8'
  )));
}

/** 读取命名债务；债务只用于精确兼容，不改变规范本身。 */
export async function loadNamingDebt(root = repositoryRoot) {
  const debt = JSON.parse(await readFileAsync(
    path.join(root, 'contracts/naming/naming-debt.json'),
    'utf8'
  ));
  if (debt.schemaVersion !== 1 || !Array.isArray(debt.items)) {
    throw new Error('NamingDebtV1 结构无效。');
  }
  return debt;
}

function validateProfile(profile) {
  if (profile.schemaVersion !== 1) {
    throw new Error(`不支持 Naming Profile 版本：${profile.schemaVersion}`);
  }
  return profile;
}
