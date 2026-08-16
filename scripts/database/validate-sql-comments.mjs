import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isColumnDefinitionLine, SQL_TYPE_PATTERN } from './object-comment-catalog.mjs';
import { loadObjectCommentsCatalog } from './apply-migration-comments.mjs';
import { extractSchemaFromMigrations, generateObjectCommentsCatalog, mergeCommentOverrides, normalizeSchema } from './generate-object-comments.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function loadCommentCatalog(root, catalog) {
  return mergeCommentOverrides(catalog ?? loadObjectCommentsCatalog(root), root);
}

function violation(ruleId, kind, value, file, line, recommendation) {
  return { ruleId, kind, value, file, line, actual: value, recommendation };
}

/** 校验迁移脚本是否包含数据库对象注释。 */
export function validateSqlComments(paths, options = {}) {
  const root = path.resolve(options.repositoryRoot ?? repositoryRoot);
  const catalog = loadCommentCatalog(root, options.catalog);
  const violations = [];

  for (const filePath of paths) {
    const absolutePath = path.resolve(filePath);
    const relativeFile = normalizePath(path.relative(root, absolutePath));
    const sql = fs.readFileSync(absolutePath, 'utf8');
    const provider = relativeFile.includes('/MySql/') || relativeFile.includes('\\MySql\\')
      ? 'mysql'
      : /ENGINE\s*=\s*InnoDB/i.test(sql)
        ? 'mysql'
        : 'sqlserver';
    inspectSql(sql, relativeFile, provider, catalog, violations);
  }

  return violations;
}

function normalizePath(relativePath) {
  return relativePath.split(path.sep).join('/');
}

function inspectSql(sql, file, provider, catalog, violations) {
  for (const match of sql.matchAll(/CREATE TABLE\s+(?:IF NOT EXISTS\s+)?(?:dbo\.)?([a-z0-9_]+)\s*\(([\s\S]*?)\n\s*\)\s*(?:COMMENT\s*=\s*'[^']*'\s*)?(?:ENGINE|;)/gi)) {
    const tableName = match[1];
    const body = match[2];
    const tableCatalog = catalog.tables[tableName];
    const line = lineNumberAt(sql, match.index);
    if (!tableCatalog) {
      violations.push(violation(
        'FNDBC001',
        'table',
        tableName,
        file,
        line,
        `在 contracts/database/object-comments.json 登记 ${tableName} 的表注释`
      ));
      continue;
    }
    if (provider === 'mysql') {
      const tableSection = sql.slice(match.index, match.index + match[0].length);
      if (!/\bCOMMENT\s*=\s*'/i.test(tableSection)) {
        violations.push(violation(
          'FNDBC002',
          'table',
          tableName,
          file,
          line,
          `为 MySQL 表 ${tableName} 添加 COMMENT='${tableCatalog.comment}'`
        ));
      }
    } else if (!sqlIncludesSqlServerTableComment(sql, match.index, tableName)) {
      violations.push(violation(
        'FNDBC002',
        'table',
        tableName,
        file,
        line,
        `为 SQL Server 表 ${tableName} 添加 MS_Description`
      ));
    }

    inspectCreateTableColumns(body, tableName, file, provider, catalog, violations, sql, match.index);
  }

  inspectAlterAddColumns(sql, file, provider, catalog, violations);
}

function sqlIncludesSqlServerTableComment(sql, createIndex, tableName) {
  const window = sql.slice(createIndex, createIndex + 4000);
  const pattern = new RegExp(
    `sp_addextendedproperty[^;]*@level1name=N'${tableName.replaceAll("'", "''")}'[^;]*@level2type=N'COLUMN'`,
    'i'
  );
  if (/@level1type=N'TABLE'/.test(window) && !pattern.test(window)) {
    return true;
  }
  return window.includes('sp_addextendedproperty') && /@level1type=N'TABLE'/.test(window);
}

function columnLineHasComment(line, lines, lineIndex) {
  if (/\bCOMMENT\b/i.test(line)) {
    return true;
  }
  for (let index = lineIndex + 1; index < Math.min(lines.length, lineIndex + 6); index += 1) {
    const nextLine = lines[index];
    if (/\bCOMMENT\b/i.test(nextLine)) {
      return true;
    }
    if (isColumnDefinitionLine(nextLine) || /^\s+CONSTRAINT\b/i.test(nextLine)) {
      break;
    }
  }
  return false;
}

function inspectCreateTableColumns(body, tableName, file, provider, catalog, violations, sql, baseIndex) {
  const lines = body.split('\n');
  let offset = 0;
  for (let lineIndex = 0; lineIndex < lines.length; lineIndex += 1) {
    const line = lines[lineIndex];
    const columnMatch = line.match(/^\s+([A-Z][A-Za-z0-9]*)\s+/);
    if (columnMatch && isColumnDefinitionLine(line)) {
      const columnName = columnMatch[1];
      const expected = catalog.tables[tableName]?.columns?.[columnName];
      if (!expected) {
        violations.push(violation(
          'FNDBC003',
          'column',
          `${tableName}.${columnName}`,
          file,
          lineNumberAt(body, offset),
          `在 contracts/database/object-comments.json 登记 ${tableName}.${columnName}`
        ));
      } else if (provider === 'mysql' && !columnLineHasComment(line, lines, lineIndex)) {
        violations.push(violation(
          'FNDBC004',
          'column',
          `${tableName}.${columnName}`,
          file,
          lineNumberAt(sql, baseIndex + offset),
          `为 MySQL 列 ${tableName}.${columnName} 添加 COMMENT`
        ));
      } else if (provider === 'sqlserver' && !sqlIncludesSqlServerColumnComment(sql, baseIndex + offset, tableName, columnName)) {
        violations.push(violation(
          'FNDBC004',
          'column',
          `${tableName}.${columnName}`,
          file,
          lineNumberAt(sql, baseIndex + offset),
          `为 SQL Server 列 ${tableName}.${columnName} 添加 MS_Description`
        ));
      }
    }
    offset += line.length + 1;
  }
}

function sqlIncludesSqlServerColumnComment(sql, index, tableName, columnName) {
  const window = sql.slice(index, index + 20000);
  const tablePattern = tableName.replaceAll("'", "''");
  const columnPattern = columnName.replaceAll("'", "''");
  const regex = new RegExp(
    `sp_addextendedproperty[\\s\\S]{0,400}@level1name=N'${tablePattern}'[\\s\\S]{0,200}@level2name=N'${columnPattern}'`,
    'i'
  );
  return regex.test(window);
}

function inspectAlterAddColumns(sql, file, provider, catalog, violations) {
  const alterAddPattern = new RegExp(
    `^\\s*ALTER TABLE\\s+(?:dbo\\.)?([a-z0-9_]+)\\s+ADD\\s+(?:COLUMN\\s+)?([A-Z][A-Za-z0-9]*)\\s+${SQL_TYPE_PATTERN}[^;]*;`,
    'gim'
  );
  for (const match of sql.matchAll(alterAddPattern)) {
    const tableName = match[1];
    const columnName = match[2];
    const statementWindow = sql.slice(match.index, match.index + 800);
    const expected = catalog.tables[tableName]?.columns?.[columnName];
    const line = lineNumberAt(sql, match.index);
    if (!expected) {
      violations.push(violation(
        'FNDBC003',
        'column',
        `${tableName}.${columnName}`,
        file,
        line,
        `在 contracts/database/object-comments.json 登记 ${tableName}.${columnName}`
      ));
      continue;
    }
    if (provider === 'mysql' && !/\bCOMMENT\b/i.test(statementWindow)) {
      violations.push(violation(
        'FNDBC004',
        'column',
        `${tableName}.${columnName}`,
        file,
        line,
        `为 MySQL 列 ${tableName}.${columnName} 添加 COMMENT`
      ));
    } else if (provider === 'sqlserver' && !sqlIncludesSqlServerColumnComment(sql, match.index, tableName, columnName)) {
      violations.push(violation(
        'FNDBC004',
        'column',
        `${tableName}.${columnName}`,
        file,
        line,
        `为 SQL Server 列 ${tableName}.${columnName} 添加 MS_Description`
      ));
    }
  }
}

function lineNumberAt(text, index) {
  return text.slice(0, index).split('\n').length;
}

/** 校验目录与迁移抽取的列清单一致。 */
export function validateCommentCatalogCoverage(options = {}) {
  const root = path.resolve(options.repositoryRoot ?? repositoryRoot);
  const catalog = loadCommentCatalog(root, options.catalog);
  const schema = normalizeSchema(extractSchemaFromMigrations());
  const violations = [];
  for (const [tableName, columns] of Object.entries(schema)) {
    if (!catalog.tables[tableName]) {
      violations.push(violation(
        'FNDBC001',
        'table',
        tableName,
        'contracts/database/object-comments.json',
        1,
        `补充表 ${tableName} 注释`
      ));
      continue;
    }
    for (const columnName of columns) {
      if (!catalog.tables[tableName].columns[columnName]) {
        violations.push(violation(
          'FNDBC003',
          'column',
          `${tableName}.${columnName}`,
          'contracts/database/object-comments.json',
          1,
          `补充列 ${tableName}.${columnName} 注释`
        ));
      }
    }
  }
  return violations;
}

/** 校验仓库内全部迁移脚本注释。 */
export async function validateRepositorySqlComments(repositoryRootPath = repositoryRoot) {
  const root = path.resolve(repositoryRootPath);
  const migrationRoot = path.join(root, 'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations');
  const files = [
    ...collectSqlFiles(path.join(migrationRoot, 'SqlServer')),
    ...collectSqlFiles(path.join(migrationRoot, 'MySql')),
  ];
  return [
    ...validateCommentCatalogCoverage({ repositoryRoot: root }),
    ...validateSqlComments(files, { repositoryRoot: root }),
  ];
}

function collectSqlFiles(directory) {
  return fs.readdirSync(directory)
    .filter(name => name.endsWith('.sql'))
    .map(name => path.join(directory, name));
}
