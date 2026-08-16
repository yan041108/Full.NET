import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  escapeSqlString,
  isColumnDefinitionLine,
  SQL_TYPE_PATTERN,
} from './object-comment-catalog.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

/** 读取注释目录。 */
export function loadObjectCommentsCatalog(repositoryRootPath = repositoryRoot) {
  const catalogPath = path.join(repositoryRootPath, 'contracts/database/object-comments.json');
  return JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
}

function getTableCatalog(catalog, tableName) {
  return catalog.tables[tableName] ?? null;
}

function getColumnComment(catalog, tableName, columnName) {
  const table = getTableCatalog(catalog, tableName);
  return table?.columns?.[columnName] ?? null;
}

function extractCreateTableColumns(body) {
  const columns = [];
  for (const line of body.split('\n')) {
    const columnMatch = line.match(/^\s+([A-Z][A-Za-z0-9]*)\s+/);
    if (columnMatch && isColumnDefinitionLine(line)) {
      columns.push(columnMatch[1]);
    }
  }
  return columns;
}

function buildSqlServerExtendedProperties(tableName, tableCatalog, columnNames) {
  const lines = [];
  const escapedTable = escapeSqlString(tableName);
  const escapedTableComment = escapeSqlString(tableCatalog.comment);
  lines.push(
    `    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'${escapedTableComment}', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'${escapedTable}';`
  );
  for (const columnName of columnNames.sort()) {
    const comment = tableCatalog.columns[columnName];
    if (!comment) {
      continue;
    }
    const escapedColumn = escapeSqlString(columnName);
    const escapedComment = escapeSqlString(comment);
    lines.push(
      `    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'${escapedComment}', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'${escapedTable}', @level2type=N'COLUMN', @level2name=N'${escapedColumn}';`
    );
  }
  return lines.join('\n');
}

function injectMySqlColumnComment(line, comment) {
  if (/\bCOMMENT\b/i.test(line)) {
    return line;
  }
  const trimmed = line.trimEnd();
  const suffix = trimmed.endsWith(',') ? ',' : '';
  const body = suffix ? trimmed.slice(0, -1) : trimmed;
  return `${body} COMMENT '${escapeSqlString(comment)}'${suffix}`;
}

function injectMySqlTableComment(createBlock, tableName, tableCatalog) {
  if (/\bCOMMENT\s*=/i.test(createBlock)) {
    return createBlock;
  }
  const tableComment = escapeSqlString(tableCatalog.comment);
  if (/\)\s*ENGINE\s*=\s*InnoDB/i.test(createBlock)) {
    return createBlock.replace(
      /\)\s*(ENGINE\s*=\s*InnoDB)/i,
      `) COMMENT='${tableComment}' $1`
    );
  }
  return createBlock.replace(/\)\s*;/, `) COMMENT='${tableComment}';`);
}

function applyMySqlCreateTableComments(sql, catalog) {
  return sql.replace(
    /CREATE TABLE IF NOT EXISTS\s+([a-z0-9_]+)\s*\(([\s\S]*?)\)(\s*(?:COMMENT\s*=\s*'(?:''|[^'])*'\s*)?(?:ENGINE\s*=\s*InnoDB[\s\S]*?|;))/gi,
    (fullMatch, tableName, body, tail) => {
      const tableCatalog = getTableCatalog(catalog, tableName);
      if (!tableCatalog) {
        return fullMatch;
      }
      const updatedBody = body
        .split('\n')
        .map(line => {
          const columnMatch = line.match(/^\s+([A-Z][A-Za-z0-9]*)\s+/);
          if (!columnMatch || !isColumnDefinitionLine(line)) {
            return line;
          }
          const comment = getColumnComment(catalog, tableName, columnMatch[1]);
          return comment ? injectMySqlColumnComment(line, comment) : line;
        })
        .join('\n');
      const withTableComment = injectMySqlTableComment(`(${updatedBody})${tail}`, tableName, tableCatalog);
      return `CREATE TABLE IF NOT EXISTS ${tableName} ${withTableComment}`;
    }
  );
}

function applySqlServerCreateTableComments(sql, catalog) {
  return sql.replace(
    /(CREATE TABLE\s+dbo\.([a-z0-9_]+)\s*\(([\s\S]*?)\n\s*\);)/gi,
    (createStatement, _full, tableName, body) => {
      const tableCatalog = getTableCatalog(catalog, tableName);
      if (!tableCatalog) {
        return createStatement;
      }
      const columnNames = extractCreateTableColumns(body);
      return `${createStatement}\n${buildSqlServerExtendedProperties(tableName, tableCatalog, columnNames)}`;
    }
  );
}

function stripSqlServerComments(sql) {
  return sql
    .split('\n')
    .filter(line => !line.includes('sp_addextendedproperty'))
    .join('\n');
}

function stripMySqlComments(sql) {
  return sql
    .replace(/\s+COMMENT\s*=\s*'(?:''|[^'])*'/gi, '')
    .replace(/\s+COMMENT\s+'(?:''|[^'])*'/gi, '');
}

function applyMySqlAlterAddComments(sql, catalog) {
  const alterAddPattern = new RegExp(
    `^\\s*ALTER TABLE\\s+([a-z0-9_]+)\\s+ADD\\s+(?:COLUMN\\s+)?([A-Z][A-Za-z0-9]*)\\s+(${SQL_TYPE_PATTERN})((?:(?!\\n\\s*ALTER\\s+TABLE)[\\s\\S])*?);`,
    'gim'
  );
  return sql.replace(
    alterAddPattern,
    (statement, tableName, columnName, typeToken, rest) => {
      const comment = getColumnComment(catalog, tableName, columnName);
      if (!comment || /\bCOMMENT\b/i.test(statement)) {
        return statement;
      }
      const trimmedRest = rest ?? '';
      if (/\bCOMMENT\b/i.test(statement)) {
        return statement;
      }
      const leading = statement.match(/^\s*/)?.[0] ?? '';
      const afterMatch = trimmedRest.match(/(\s+)((?:FIRST|AFTER\s+[A-Za-z0-9]+)\s*;?\s*)$/i);
      if (afterMatch) {
        const beforeAfter = trimmedRest.slice(0, trimmedRest.length - afterMatch[0].length);
        const terminator = /;\s*$/.test(afterMatch[2]) ? ';' : '';
        const position = afterMatch[2].replace(/;\s*$/, '').trim();
        return `${leading}ALTER TABLE ${tableName} ADD ${columnName} ${typeToken}${beforeAfter} COMMENT '${escapeSqlString(comment)}' ${position}${terminator}`;
      }
      return `${leading}ALTER TABLE ${tableName} ADD ${columnName} ${typeToken}${trimmedRest} COMMENT '${escapeSqlString(comment)}';`;
    }
  );
}

/** 判断 ALTER 是否位于 EXEC(N'...') 动态 SQL 内部，避免把扩展属性写进字符串字面量。 */
function isInsideSqlServerExecString(sql, matchIndex) {
  const before = sql.slice(0, matchIndex);
  const lastExec = before.lastIndexOf("EXEC(N'");
  if (lastExec < 0) {
    return false;
  }
  return before.lastIndexOf("');") < lastExec;
}

function applySqlServerAlterAddComments(sql, catalog) {
  const alterAddPattern = new RegExp(
    `^\\s*ALTER TABLE\\s+dbo\\.([a-z0-9_]+)\\s+ADD\\s+(?:COLUMN\\s+)?([A-Z][A-Za-z0-9]*)\\s+(${SQL_TYPE_PATTERN})([^;]*);`,
    'gim'
  );
  return sql.replace(
    alterAddPattern,
    (statement, tableName, columnName, typeToken, rest, offset, source) => {
      if (isInsideSqlServerExecString(source, offset)) {
        return statement;
      }
      const comment = getColumnComment(catalog, tableName, columnName);
      if (!comment || statement.includes('sp_addextendedproperty')) {
        return statement;
      }
      const escapedTable = escapeSqlString(tableName);
      const escapedColumn = escapeSqlString(columnName);
      const escapedComment = escapeSqlString(comment);
      return `${statement}\nEXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'${escapedComment}', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'${escapedTable}', @level2type=N'COLUMN', @level2name=N'${escapedColumn}';`;
    }
  );
}

/** 将目录中的注释应用到单个迁移文件。 */
export function applyMigrationFileComments(filePath, catalog, provider, options = {}) {
  let original = fs.readFileSync(filePath, 'utf8');
  if (options.stripExisting) {
    original = provider === 'mysql' ? stripMySqlComments(original) : stripSqlServerComments(original);
  }
  let updated = original;
  if (provider === 'mysql') {
    updated = applyMySqlCreateTableComments(updated, catalog);
    updated = applyMySqlAlterAddComments(updated, catalog);
  } else {
    updated = applySqlServerCreateTableComments(updated, catalog);
    updated = applySqlServerAlterAddComments(updated, catalog);
  }
  if (updated !== original) {
    fs.writeFileSync(filePath, updated, 'utf8');
    return true;
  }
  return false;
}

/** 批量应用到 DbUp 迁移目录。 */
export function applyAllMigrationComments(options = {}) {
  const root = path.resolve(options.repositoryRoot ?? repositoryRoot);
  const catalog = options.catalog ?? loadObjectCommentsCatalog(root);
  const migrationRoot = path.join(root, 'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations');
  const changed = [];
  for (const provider of ['SqlServer', 'MySql']) {
    const directory = path.join(migrationRoot, provider);
    for (const fileName of fs.readdirSync(directory).filter(name => name.endsWith('.sql')).sort()) {
      const filePath = path.join(directory, fileName);
      if (applyMigrationFileComments(
        filePath,
        catalog,
        provider === 'MySql' ? 'mysql' : 'sqlserver',
        { stripExisting: options.stripExisting === true }
      )) {
        changed.push(path.relative(root, filePath));
      }
    }
  }
  return changed;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const stripExisting = process.argv.includes('--strip-existing');
  const changed = applyAllMigrationComments({ stripExisting });
  console.log(`Updated ${changed.length} migration files`);
  for (const file of changed) {
    console.log(`  ${file}`);
  }
}
