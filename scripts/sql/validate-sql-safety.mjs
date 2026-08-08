import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultRepositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

/** 与命名门禁共用的 C# 静态 SQL 容器；Organization 必须纳入，避免应用 SQL 漏扫。 */
export const registeredStaticSqlFiles = [
  'src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxWriter.cs',
  'src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperAppendOnlyOutboxWriter.cs',
  'src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs',
  'src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedExecutionStore.cs',
  'src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs',
  'src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs',
  'src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSql.cs',
];

/**
 * 扫描应用与迁移 SQL 的破坏性/无 WHERE 写操作。
 * 命名规则仍由 validate-sql-names 负责，本门禁不重复 SELECT *。
 */
export async function validateSqlSafety(paths, options = {}) {
  const repositoryRoot = path.resolve(options.repositoryRoot ?? defaultRepositoryRoot);
  const waivers = options.waivers ?? await loadSqlSafetyWaivers(repositoryRoot);
  const violations = [];
  for (const filePath of paths) {
    const absolutePath = path.resolve(filePath);
    const file = normalizePath(path.relative(repositoryRoot, absolutePath));
    const content = await readFile(absolutePath, 'utf8');
    inspectSqlSafety(content, file, violations);
  }
  return violations.filter(item => !isExactWaiver(item, waivers));
}

/** 扫描仓库生产 SQL 与已登记 C# 静态 SQL 容器。 */
export async function validateRepositorySqlSafety(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const files = [
    ...await collectFiles(path.join(root, 'src'), '.sql'),
    ...registeredStaticSqlFiles.map(file => path.join(root, file)),
  ];
  return validateSqlSafety(files, { repositoryRoot: root });
}

export async function loadSqlSafetyWaivers(repositoryRoot = defaultRepositoryRoot) {
  const waiverPath = path.join(
    path.resolve(repositoryRoot),
    'contracts/sql-safety/waivers.json');
  const raw = JSON.parse(await readFile(waiverPath, 'utf8'));
  validateWaiverDocument(raw);
  return raw;
}

function inspectSqlSafety(content, file, violations) {
  const lines = content.split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    const text = stripComment(lines[index]);
    if (!text.trim()) continue;

    if (/\bTRUNCATE\s+TABLE\b/i.test(text)) {
      violations.push(violation(
        'FNSAFETY002',
        'truncate',
        'TRUNCATE TABLE',
        file,
        index + 1,
        '禁止 TRUNCATE；改用受控分批删除或登记带备份/审查的限期豁免'));
      continue;
    }

    if (/\bDROP\s+TEMPORARY\s+TABLE\b/i.test(text)
      || /\bDROP\s+(?:PROCEDURE|FUNCTION|TRIGGER|INDEX|CONSTRAINT)\b/i.test(text)) {
      continue;
    }

    if (/\bDROP\s+TABLE\b/i.test(text)) {
      violations.push(violation(
        'FNSAFETY003',
        'drop_table',
        'DROP TABLE',
        file,
        index + 1,
        '迁移 DROP TABLE 必须进入 contracts/sql-safety/waivers.json 并附备份/审查证据'));
    }

    if (/\bDROP\s+COLUMN\b/i.test(text)) {
      violations.push(violation(
        'FNSAFETY003',
        'drop_column',
        'DROP COLUMN',
        file,
        index + 1,
        '迁移 DROP COLUMN 必须进入 contracts/sql-safety/waivers.json 并附备份/审查证据'));
    }

    if (/\bRENAME\s+(?:TABLE|COLUMN)\b/i.test(text)
      || /\bALTER\s+TABLE\b[\s\S]*\bRENAME\b/i.test(text)) {
      violations.push(violation(
        'FNSAFETY004',
        'rename',
        'RENAME',
        file,
        index + 1,
        '直接重命名必须走 expand/contract 或登记限期豁免'));
    }

    if (isBareWriteWithoutWhere(lines, index)) {
      const kind = /^\s*DELETE\b/i.test(text) || /\bDELETE\s+FROM\b/i.test(text)
        ? 'delete_without_where'
        : 'update_without_where';
      violations.push(violation(
        'FNSAFETY001',
        kind,
        kind,
        file,
        index + 1,
        '应用与迁移写操作必须带 WHERE；全表修正须登记豁免并断言预期行数'));
    }
  }
}

/**
 * 判断从当前行开始的 UPDATE/DELETE 语句在分号结束前是否缺少 WHERE。
 * 跳过 MERGE、触发器 BEFORE UPDATE、CTE UPDATE Pending 等非裸写形态。
 */
function isBareWriteWithoutWhere(lines, startIndex) {
  const start = stripComment(lines[startIndex]);
  if (/\bBEFORE\s+UPDATE\b|\bAFTER\s+UPDATE\b|\bWHEN\s+MATCHED\b|\bMERGE\b/i.test(start)) {
    return false;
  }

  const isDelete = /(?:^|;)\s*DELETE\s+FROM\b/i.test(start) || /^\s*DELETE\s+FROM\b/i.test(start);
  const isUpdate = /(?:^|;)\s*UPDATE\s+(?:dbo\.)?[A-Za-z][A-Za-z0-9_]*/i.test(start)
    && !/\bUPDATE\s+SET\b/i.test(start);
  if (!isDelete && !isUpdate) {
    return false;
  }

  let statement = start;
  let cursor = startIndex;
  while (!/;/.test(statement) && cursor + 1 < lines.length) {
    cursor += 1;
    statement += `\n${stripComment(lines[cursor])}`;
  }

  // 向前并入同一批 CTE，使 WITH Pending AS (...) UPDATE Pending 不被误判。
  let lookBack = startIndex;
  while (lookBack > 0) {
    lookBack -= 1;
    const previous = stripComment(lines[lookBack]);
    if (!previous.trim()) {
      continue;
    }
    statement = `${previous}\n${statement}`;
    if (/\bWITH\b/i.test(previous) || /;\s*$/.test(previous)) {
      break;
    }
  }

  if (/\bWHERE\b/i.test(statement)) {
    return false;
  }

  const cteNames = [...statement.matchAll(/\bWITH\s+([A-Za-z][A-Za-z0-9_]*)\s+AS\b/gi)]
    .map(match => match[1].toLowerCase());
  const updateTarget = statement.match(/\bUPDATE\s+(?:dbo\.)?([A-Za-z][A-Za-z0-9_]*)/i);
  if (updateTarget && cteNames.includes(updateTarget[1].toLowerCase())) {
    return false;
  }

  return true;
}

function validateWaiverDocument(document) {
  if (document.schemaVersion !== 1 || !Array.isArray(document.items)) {
    throw new Error('sql-safety waivers must declare schemaVersion 1 and items[].');
  }
  for (const item of document.items) {
    for (const field of [
      'ruleId',
      'file',
      'line',
      'actual',
      'reason',
      'risk',
      'reviewer',
      'removalMilestone',
    ]) {
      if (!item[field] && item[field] !== 0) {
        throw new Error(`sql-safety waiver missing required field: ${field}`);
      }
    }
    if (item.backupVerified !== true) {
      throw new Error(
        `sql-safety waiver for ${item.file}:${item.line} must set backupVerified=true`);
    }
    if (!Number.isInteger(item.line) || item.line < 1) {
      throw new Error(`sql-safety waiver line must be a positive integer: ${item.file}`);
    }
  }
}

function isExactWaiver(item, waivers) {
  return waivers.items.some(candidate =>
    candidate.ruleId === item.ruleId
    && normalizePath(candidate.file) === item.file
    && candidate.line === item.line
    && candidate.actual === item.actual);
}

function violation(ruleId, kind, actual, file, line, recommendation) {
  return { ruleId, kind, actual, file, line, recommendation };
}

function stripComment(line) {
  return line.replace(/--.*$/, '');
}

function normalizePath(value) {
  return value.replaceAll('\\', '/');
}

async function collectFiles(root, extension) {
  const entries = await readdir(root, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const absolute = path.join(root, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectFiles(absolute, extension));
      continue;
    }
    if (entry.isFile() && entry.name.endsWith(extension)) {
      files.push(absolute);
    }
  }
  return files;
}
