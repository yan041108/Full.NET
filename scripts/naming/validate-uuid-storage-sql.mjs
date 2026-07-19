import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const defaultRepositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const migrationRelativeRoot =
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations';
const highWriteUuidTables = new Set([
  'fn_outbox_message',
  'fn_identity_auth_audit'
]);

/** 检查 010+ 迁移是否遵守 UUID Binary16 与 SQL Server 聚集索引门禁。 */
export async function validateUuidStorageSql(paths, options = {}) {
  const repositoryRoot = path.resolve(options.repositoryRoot ?? defaultRepositoryRoot);
  const violations = [];
  for (const filePath of paths) {
    const absolutePath = path.resolve(filePath);
    const relativePath = normalizePath(path.relative(repositoryRoot, absolutePath));
    const fileName = path.basename(absolutePath);
    if (!isGovernedMigration(fileName)) {
      continue;
    }

    const provider = detectProvider(relativePath);
    const sql = await readFile(absolutePath, 'utf8');
    if (provider === 'MySql') {
      inspectMySqlGovernedMigration(sql, relativePath, violations);
    } else if (provider === 'SqlServer') {
      inspectSqlServerGovernedMigration(sql, relativePath, violations);
    }
  }

  return violations;
}

/** 扫描仓库内全部受治理的 DbUp 迁移脚本。 */
export async function validateRepositoryUuidStorageSql(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const files = [
    ...await collectMigrationFiles(path.join(root, migrationRelativeRoot, 'MySql')),
    ...await collectMigrationFiles(path.join(root, migrationRelativeRoot, 'SqlServer'))
  ];
  return validateUuidStorageSql(files, { repositoryRoot: root });
}

function inspectMySqlGovernedMigration(sql, file, violations) {
  const lines = sql.split(/\r?\n/);
  lines.forEach((line, index) => {
    const text = stripComment(line);
    const columnPattern =
      /\b([A-Za-z][A-Za-z0-9_]*)\s+char\s*\(\s*36\s*\)(?:\s+CHARACTER\s+SET\s+ascii(?:\s+COLLATE\s+ascii_bin)?)?/giu;
    for (const match of text.matchAll(columnPattern)) {
      if (!isUuidColumnName(match[1])) {
        continue;
      }

      violations.push(violation(
        'FNUUID001',
        'mysql_uuid_column',
        `${match[1]} char(36)`,
        file,
        index + 1,
        '010+ MySQL 迁移中的 UUID 列必须使用 BINARY(16)'
      ));
    }
  });
}

function inspectSqlServerGovernedMigration(sql, file, violations) {
  const lines = sql.split(/\r?\n/);
  let currentTable = null;

  for (let index = 0; index < lines.length; index += 1) {
    const text = stripComment(lines[index]);
    const createMatch = text.match(/\bCREATE\s+TABLE\s+(?:dbo\.)?([A-Za-z][A-Za-z0-9_]*)/i);
    if (createMatch) {
      currentTable = createMatch[1];
      continue;
    }

    if (currentTable && /^\)\s*;?\s*$/i.test(text.trim())) {
      currentTable = null;
      continue;
    }

    if (!/\bPRIMARY\s+KEY\b/i.test(text)) {
      continue;
    }

    if (!/\b(?:NON)?CLUSTERED\b/i.test(text)) {
      violations.push(violation(
        'FNUUID002',
        'sqlserver_primary_key_cluster',
        text.trim(),
        file,
        index + 1,
        '010+ SQL Server UUID 主键必须显式声明 CLUSTERED 或 NONCLUSTERED'
      ));
    }

    const tableFromConstraint = text.match(/\bCONSTRAINT\s+PK_([A-Za-z0-9_]+)\s+PRIMARY\s+KEY\b/i)?.[1]
      ?? currentTable;
    if (tableFromConstraint
      && highWriteUuidTables.has(tableFromConstraint)
      && !/\bNONCLUSTERED\b/i.test(text)) {
      violations.push(violation(
        'FNUUID003',
        'sqlserver_high_write_cluster',
        text.trim(),
        file,
        index + 1,
        '高写入表 UUID 主键必须使用 NONCLUSTERED，并配套显式时间聚集索引'
      ));
    }
  }
}

function isGovernedMigration(fileName) {
  const match = fileName.match(/^(\d{3})_/u);
  if (!match) {
    return false;
  }

  const ordinal = Number(match[1], 10);
  return ordinal >= 10;
}

function detectProvider(relativePath) {
  if (relativePath.includes('/MySql/')) {
    return 'MySql';
  }

  if (relativePath.includes('/SqlServer/')) {
    return 'SqlServer';
  }

  return null;
}

function isUuidColumnName(column) {
  return column === 'Id' || /Id$/u.test(column);
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

async function collectMigrationFiles(directory) {
  try {
    return (await readdir(directory, { withFileTypes: true }))
      .filter(entry => entry.isFile() && entry.name.endsWith('.sql'))
      .map(entry => path.join(directory, entry.name));
  } catch {
    return [];
  }
}

async function runCli() {
  const repositoryRoot = process.cwd();
  const violations = await validateRepositoryUuidStorageSql(repositoryRoot);
  if (violations.length === 0) {
    return;
  }

  for (const item of violations) {
    console.error(`${item.ruleId} ${item.file}:${item.line} ${item.actual} -> ${item.recommendation}`);
  }

  process.exitCode = 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runCli();
}
