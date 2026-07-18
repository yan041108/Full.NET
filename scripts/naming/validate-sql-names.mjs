import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { buildDatabaseObjectName } from './database-object-name.mjs';
import { loadNamingDebt, loadNamingProfile } from './load-naming-profile.mjs';

const defaultRepositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const registeredStaticSqlFiles = [
  'src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxWriter.cs',
  'src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs',
  'src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs',
  'src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs'
];

/** 检查静态 SQL 子集中的数据库对象、列、约束和危险查询命名。 */
export async function validateSqlNaming(paths, options = {}) {
  const repositoryRoot = path.resolve(options.repositoryRoot ?? defaultRepositoryRoot);
  const profile = options.profile ?? await loadNamingProfile(repositoryRoot);
  const debt = options.debt ?? await loadNamingDebt(repositoryRoot);
  const violations = [];
  for (const filePath of paths) {
    const absolutePath = path.resolve(filePath);
    const file = normalizePath(path.relative(repositoryRoot, absolutePath));
    const sql = await readFile(absolutePath, 'utf8');
    inspectSql(sql, file, profile, debt, violations);
  }
  return violations.filter(violation => !isExactDebt(violation, debt));
}

/** 检查两个 Provider 的 DbUp 迁移文件名是否严格配对。 */
export async function validateMigrationPairs(migrationRoot, options = {}) {
  const repositoryRoot = path.resolve(options.repositoryRoot ?? defaultRepositoryRoot);
  const sqlServerRoot = path.join(migrationRoot, 'SqlServer');
  const mySqlRoot = path.join(migrationRoot, 'MySql');
  const [sqlServerFiles, mySqlFiles] = await Promise.all([
    listSqlNames(sqlServerRoot),
    listSqlNames(mySqlRoot)
  ]);
  const sqlServer = new Set(sqlServerFiles);
  const mySql = new Set(mySqlFiles);
  const violations = [];
  for (const name of [...sqlServer].sort()) {
    if (!mySql.has(name)) {
      violations.push(violation(
        'FNMIG001',
        'migration',
        name,
        normalizePath(path.relative(repositoryRoot, path.join(sqlServerRoot, name))),
        1,
        `在 MySql 目录增加同名迁移 ${name}`
      ));
    }
  }
  for (const name of [...mySql].sort()) {
    if (!sqlServer.has(name)) {
      violations.push(violation(
        'FNMIG001',
        'migration',
        name,
        normalizePath(path.relative(repositoryRoot, path.join(mySqlRoot, name))),
        1,
        `在 SqlServer 目录增加同名迁移 ${name}`
      ));
    }
  }
  return violations;
}

/** 检查仓库内全部 SQL 文件以及明确登记的 C# 静态 SQL 容器。 */
export async function validateRepositorySqlNaming(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const migrationRoot = path.join(
    root,
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations'
  );
  const files = [
    ...await collectFiles(path.join(root, 'src'), '.sql'),
    ...registeredStaticSqlFiles.map(file => path.join(root, file))
  ];
  return [
    ...await validateSqlNaming(files, { repositoryRoot: root }),
    ...await validateMigrationPairs(migrationRoot, { repositoryRoot: root })
  ];
}

function inspectSql(sql, file, profile, debt, violations) {
  const lines = sql.split(/\r?\n/);
  const cteNames = collectCteNames(lines);
  const tables = collectTableReferences(lines, cteNames);
  for (const reference of tables) {
    validateTable(reference, file, profile, debt, violations);
  }

  inspectCreateTableBlocks(lines, file, profile, debt, violations);
  inspectDatabaseObjectNames(lines, file, profile, violations);
  lines.forEach((line, index) => {
    const text = stripComment(line);
    if (/\bSELECT\s+(?:TOP\s*\([^)]*\)\s+)?\*/i.test(text)) {
      violations.push(violation(
        'FNSQL001',
        'query',
        'select_all_columns',
        file,
        index + 1,
        '显式列出查询投影'
      ));
    }
    const containsDynamicSql = /\bEXEC\s*\(|\bPREPARE\s+[A-Za-z][A-Za-z0-9_]*\s+FROM\b|N?'\s*(?:ALTER|CREATE|DROP|RENAME|UPDATE|DELETE|INSERT)\b/i
      .test(text);
    if (containsDynamicSql) {
      violations.push(violation(
        'FNSQL002',
        'dynamic_sql',
        'dynamic_sql',
        file,
        index + 1,
        '由人工审查动态 SQL，并为既有语句登记精确、有限期债务'
      ));
    }
    const containsUnsupportedDdl = /\bCREATE\s+(?:OR\s+ALTER\s+)?(?:VIEW|PROCEDURE|FUNCTION|TRIGGER|SCHEMA|DATABASE)\b|\bDROP\s+(?:TABLE|VIEW|COLUMN|INDEX|CONSTRAINT|PROCEDURE|FUNCTION|TRIGGER|SCHEMA|DATABASE)\b|\bRENAME\s+(?:TABLE|COLUMN)\b|\bALTER\s+TABLE\b.*\bRENAME\b/i
      .test(text);
    if (!containsDynamicSql && containsUnsupportedDdl) {
      violations.push(violation(
        'FNSQL003',
        'unsupported_sql',
        'unsupported_ddl',
        file,
        index + 1,
        '当前静态命名扫描器不支持该 DDL，必须人工审查后扩展解析器或登记精确债务'
      ));
    }
  });
}

function collectTableReferences(lines, cteNames) {
  const references = [];
  const pattern = /\b(?:CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?|ALTER\s+TABLE|INSERT\s+INTO|FROM|JOIN)\s+(?:([A-Za-z][A-Za-z0-9_]*)\.)?([A-Za-z][A-Za-z0-9_]*)/gi;
  const updatePattern = /(?:^|;)\s*UPDATE\s+(?:([A-Za-z][A-Za-z0-9_]*)\.)?([A-Za-z][A-Za-z0-9_]*)/gi;
  lines.forEach((line, index) => {
    const text = stripComment(line);
    for (const match of text.matchAll(pattern)) {
      if (isSystemSchema(match[1]) || cteNames.has(match[2].toLowerCase())) continue;
      references.push({ name: match[2], line: index + 1 });
    }
    for (const match of text.matchAll(updatePattern)) {
      if (isSystemSchema(match[1]) || cteNames.has(match[2].toLowerCase())) continue;
      references.push({ name: match[2], line: index + 1 });
    }
    const indexTable = text.match(/\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+[A-Za-z][A-Za-z0-9_]*\s+ON\s+(?:([A-Za-z][A-Za-z0-9_]*)\.)?([A-Za-z][A-Za-z0-9_]*)/i);
    if (indexTable && !isSystemSchema(indexTable[1])) {
      references.push({ name: indexTable[2], line: index + 1 });
    }
  });
  return references;
}

function collectCteNames(lines) {
  const names = new Set();
  for (const line of lines) {
    const match = stripComment(line).match(/(?:^|;)\s*WITH\s+([A-Za-z][A-Za-z0-9_]*)\s+AS\s*$/i);
    if (match) names.add(match[1].toLowerCase());
  }
  return names;
}

function validateTable(reference, file, profile, debt, violations) {
  const tablePattern = new RegExp(profile.database.tablePattern);
  if (!tablePattern.test(reference.name) || reference.name !== reference.name.toLowerCase()) {
    violations.push(violation(
      'FNDB001',
      'table',
      reference.name,
      file,
      reference.line,
      '使用 {owner}_{module}_{entity} 小写 snake_case 表名'
    ));
  }
  const owner = reference.name.split('_', 1)[0]?.toLowerCase();
  if (profile.database.reservedOwnerKeys.includes(owner) && owner !== profile.database.frameworkOwnerKey) {
    violations.push(violation(
      'FNDB005',
      'table',
      reference.name,
      file,
      reference.line,
      '使用已冻结且非保留的项目 OwnerKey'
    ));
  }
  if (debt.items.some(item => item.kind === 'table' && item.value === reference.name)) {
    violations.push(violation(
      'FNDB006',
      'table',
      reference.name,
      file,
      reference.line,
      '按命名规范化计划迁移到登记的规范表名'
    ));
  }
}

function inspectCreateTableBlocks(lines, file, profile, debt, violations) {
  for (let index = 0; index < lines.length; index += 1) {
    const match = stripComment(lines[index]).match(/\bCREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?\s+(?:dbo\.)?([A-Za-z][A-Za-z0-9_]*)/i);
    if (!match) continue;
    const table = match[1];
    for (let cursor = index + 1; cursor < lines.length; cursor += 1) {
      const line = stripComment(lines[cursor]).trim();
      if (/^\)\b|^\)\s*;?$/.test(line)) break;
      if (!line || /^\($/.test(line)) continue;
      if (/\bPRIMARY\s+KEY\b/i.test(line) && !/\bCONSTRAINT\s+PK_[A-Za-z0-9_]+\s+PRIMARY\s+KEY\b/i.test(line)) {
        violations.push(violation(
          'FNDB003',
          'primary_key',
          `${table}.PRIMARY KEY`,
          file,
          cursor + 1,
          `显式命名为 PK_${table}`
        ));
      }
      const columnMatch = line.match(/^([A-Za-z][A-Za-z0-9_]*)\s+/);
      if (!columnMatch || /^(?:CONSTRAINT|PRIMARY|UNIQUE|KEY|INDEX|CHECK|FOREIGN)$/i.test(columnMatch[1])) continue;
      const column = columnMatch[1];
      if (!(new RegExp(profile.database.columnPattern)).test(column)) {
        violations.push(violation(
          'FNDB002',
          'column',
          `${table}.${column}`,
          file,
          cursor + 1,
          '使用与 C# 属性直接映射的 PascalCase 列名'
        ));
      }
      if (debt.items.some(item => item.kind === 'column' && item.value === `${table}.${column}`)) {
        violations.push(violation(
          'FNDB007',
          'column',
          `${table}.${column}`,
          file,
          cursor + 1,
          '按命名规范化计划迁移到登记的规范列名'
        ));
      }
    }
  }
}

function inspectDatabaseObjectNames(lines, file, profile, violations) {
  const pattern = /\b(?:CONSTRAINT|INDEX|KEY)\s+([A-Za-z][A-Za-z0-9_]*)/gi;
  lines.forEach((line, index) => {
    for (const match of stripComment(line).matchAll(pattern)) {
      const name = match[1];
      if (name.length > profile.database.maxIdentifierLength) {
        violations.push(violation(
          'FNDB004',
          'database_object',
          name,
          file,
          index + 1,
          `使用确定性压缩名称 ${buildDatabaseObjectName(name)}`
        ));
      }
    }
  });
}

function isExactDebt(item, debt) {
  return debt.items.some(candidate => candidate.kind === item.kind
    && candidate.value === item.actual
    && normalizePath(candidate.file) === item.file);
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

function isSystemSchema(value) {
  return /^(?:sys|information_schema)$/i.test(value ?? '');
}

async function listSqlNames(directory) {
  return (await readdir(directory, { withFileTypes: true }))
    .filter(entry => entry.isFile() && entry.name.endsWith('.sql'))
    .map(entry => entry.name);
}

async function collectFiles(directory, extension, output = []) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const target = path.join(directory, entry.name);
    if (entry.isDirectory()) await collectFiles(target, extension, output);
    else if (entry.isFile() && entry.name.endsWith(extension)) output.push(target);
  }
  return output;
}

async function runCli() {
  const repositoryRoot = process.cwd();
  const violations = await validateRepositorySqlNaming(repositoryRoot);
  if (violations.length === 0) return;
  for (const item of violations) {
    console.error(`${item.ruleId} ${item.file}:${item.line} ${item.actual} -> ${item.recommendation}`);
  }
  process.exitCode = 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runCli();
}
