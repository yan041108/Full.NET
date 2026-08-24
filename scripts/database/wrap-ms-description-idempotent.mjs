import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const dir = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer',
);
const lineRe = /^(\s*)EXEC dbo\.fn_ensure_ms_description (.+);$/;

function wrapLine(line) {
  const match = line.match(lineRe);
  if (!match) {
    return line;
  }

  const indent = match[1];
  const args = match[2];
  const table = args.match(/@level1name=N'([^']+)'/)[1];
  const column = args.match(/@level2name=N'([^']+)'/);
  const objectId = `OBJECT_ID(N'dbo.${table}')`;
  const minorId = column
    ? `COLUMNPROPERTY(${objectId}, N'${column[1]}', 'ColumnId')`
    : '0';

  return `${indent}IF NOT EXISTS (
${indent}    SELECT 1
${indent}    FROM sys.extended_properties
${indent}    WHERE class = 1
${indent}      AND major_id = ${objectId}
${indent}      AND minor_id = ${minorId}
${indent}      AND name = N'MS_Description'
${indent})
${indent}    EXEC sys.sp_addextendedproperty ${args};`;
}

let fileCount = 0;
let lineCount = 0;

for (const name of fs.readdirSync(dir)) {
  if (!name.endsWith('.sql')) {
    continue;
  }

  const filePath = path.join(dir, name);
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.split(/\r?\n/);
  const updatedLines = lines.map(line => {
    const wrapped = wrapLine(line);
    if (wrapped !== line) {
      lineCount++;
    }
    return wrapped;
  });
  const updated = updatedLines.join('\n');
  if (updated !== content) {
    fs.writeFileSync(filePath, updated, 'utf8');
    fileCount++;
  }
}

console.log(`Wrapped ${lineCount} lines in ${fileCount} files`);
