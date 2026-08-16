import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  buildCommentCatalog,
  describeTable,
  isColumnDefinitionLine,
  SQL_TYPE_PATTERN,
} from './object-comment-catalog.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const migrationRoot = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer'
);

const SQL_KEYWORDS = new Set([
  'AND', 'OR', 'CONSTRAINT', 'PRIMARY', 'UNIQUE', 'KEY', 'CHECK', 'FOREIGN', 'REFERENCES', 'DEFAULT',
]);

/** 从双 Provider 迁移目录提取表与列清单。 */
export function extractSchemaFromMigrations(migrationDirectory) {
  const schema = {};
  if (migrationDirectory) {
    collectFromDirectory(migrationDirectory, schema);
    return schema;
  }
  const root = path.join(
    repositoryRoot,
    'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations'
  );
  collectFromDirectory(path.join(root, 'SqlServer'), schema);
  collectFromDirectory(path.join(root, 'MySql'), schema);
  return schema;
}

function collectFromDirectory(migrationDirectory, schema) {
  for (const fileName of fs.readdirSync(migrationDirectory).filter(name => name.endsWith('.sql')).sort()) {
    const sql = fs.readFileSync(path.join(migrationDirectory, fileName), 'utf8');
    collectCreateTableColumns(sql, schema);
    collectAlterAddColumns(sql, schema);
  }
}

function collectCreateTableColumns(sql, schema) {
  for (const match of sql.matchAll(/CREATE TABLE\s+(?:dbo\.)?([a-z0-9_]+)\s*\(([\s\S]*?)\n\s*\);/gi)) {
    const tableName = match[1];
    if (!schema[tableName]) {
      schema[tableName] = new Set();
    }
    for (const line of match[2].split('\n')) {
      const columnMatch = line.match(/^\s+([A-Z][A-Za-z0-9]*)\s+/);
      if (!columnMatch || !isColumnDefinitionLine(line)) {
        continue;
      }
      schema[tableName].add(columnMatch[1]);
    }
  }
}

function collectAlterAddColumns(sql, schema) {
  const alterAddPattern = new RegExp(
    `^\\s*ALTER TABLE\\s+(?:dbo\\.)?([a-z0-9_]+)\\s+ADD\\s+(?:COLUMN\\s+)?([A-Z][A-Za-z0-9]*)\\s+${SQL_TYPE_PATTERN}\\b`,
    'gim'
  );
  for (const match of sql.matchAll(alterAddPattern)) {
    const tableName = match[1];
    const columnName = match[2];
    if (!schema[tableName]) {
      schema[tableName] = new Set();
    }
    schema[tableName].add(columnName);
  }
}

/** 将 Set 列清单转换为排序后的数组结构。 */
export function normalizeSchema(schemaWithSets) {
  return Object.fromEntries(
    Object.entries(schemaWithSets)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([tableName, columns]) => [tableName, [...columns].sort()])
  );
}

/** 合并人工维护的注释覆盖项。 */
export function mergeCommentOverrides(catalog, repositoryRootPath = repositoryRoot) {
  const overridePath = path.join(repositoryRootPath, 'contracts/database/object-comment-overrides.json');
  if (!fs.existsSync(overridePath)) {
    return catalog;
  }
  const overrides = JSON.parse(fs.readFileSync(overridePath, 'utf8'));
  for (const [tableName, tableOverride] of Object.entries(overrides.tables ?? {})) {
    if (!catalog.tables[tableName]) {
      catalog.tables[tableName] = { comment: describeTable(tableName), columns: {} };
    }
    if (tableOverride.comment) {
      catalog.tables[tableName].comment = tableOverride.comment;
    }
    catalog.tables[tableName].columns = {
      ...catalog.tables[tableName].columns,
      ...(tableOverride.columns ?? {}),
    };
  }
  return catalog;
}

/** 生成并写入 contracts/database/object-comments.json。 */
export function generateObjectCommentsCatalog(options = {}) {
  const root = path.resolve(options.repositoryRoot ?? repositoryRoot);
  const outputPath = path.join(root, 'contracts/database/object-comments.json');
  const schema = normalizeSchema(extractSchemaFromMigrations());
  const catalog = mergeCommentOverrides(buildCommentCatalog(schema), root);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(catalog, null, 2)}\n`, 'utf8');
  return { outputPath, tableCount: Object.keys(catalog.tables).length };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = generateObjectCommentsCatalog();
  console.log(`Wrote ${result.outputPath} (${result.tableCount} tables)`);
}
