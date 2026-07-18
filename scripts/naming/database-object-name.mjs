import { createHash } from 'node:crypto';
import { loadNamingProfileSync } from './load-naming-profile.mjs';

const profile = loadNamingProfileSync();
const allowedPrefixes = new Set(['PK', 'FK', 'UX', 'IX', 'CK', 'DF']);

/**
 * 按跨数据库共同上限生成确定性的索引或约束名称。
 * 表名和列名不得调用此函数静默截断，它们超长时必须重新设计。
 */
export function buildDatabaseObjectName(fullName) {
  if (typeof fullName !== 'string' || fullName.length === 0) {
    throw new TypeError('数据库对象名不能为空。');
  }
  if (!/^[A-Za-z0-9_]+$/.test(fullName)) {
    throw new TypeError('数据库对象名只能包含 ASCII 字母、数字和下划线。');
  }
  if (!allowedPrefixes.has(fullName.split('_', 1)[0])) {
    throw new TypeError('数据库对象名使用了未知对象前缀。');
  }
  if (fullName.length <= profile.database.maxIdentifierLength) {
    return fullName;
  }

  const digest = createHash(profile.database.constraintDigest.algorithm)
    .update(fullName, 'utf8')
    .digest('hex')
    .slice(0, profile.database.constraintDigest.hexLength);
  return `${fullName.slice(0, profile.database.constraintDigest.prefixLength)}`
    + `${profile.database.constraintDigest.separator}${digest}`;
}
