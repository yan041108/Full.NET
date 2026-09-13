import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const directory = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer',
);

function needsGoBefore(lines, index) {
  let cursor = index - 1;
  while (cursor >= 0 && lines[cursor].trim() === '') {
    cursor -= 1;
  }

  if (cursor < 0) {
    return false;
  }

  if (lines[cursor].trim() === 'GO') {
    return false;
  }

  const line = lines[index].trim();
  const previous = lines[cursor].trim();

  if (previous !== 'END;' && previous !== 'END') {
    return false;
  }

  if (/^UPDATE dbo\./.test(line)) {
    return true;
  }

  if (
    line === 'IF NOT EXISTS (' &&
    lines.slice(index, index + 6).some((candidate) => candidate.includes('sys.check_constraints'))
  ) {
    return true;
  }

  if (
    line === 'IF NOT EXISTS (' &&
    lines.slice(index, index + 6).some((candidate) => candidate.includes('sys.indexes'))
  ) {
    return true;
  }

  return false;
}

function insertBatchGo(content) {
  if (!content.includes('IF COL_LENGTH')) {
    return { content, inserted: 0 };
  }

  const lines = content.split(/\r?\n/);
  const output = [];
  let inserted = 0;

  for (let index = 0; index < lines.length; index += 1) {
    if (needsGoBefore(lines, index)) {
      output.push('GO', '');
      inserted += 1;
    }

    output.push(lines[index]);
  }

  return {
    content: output.join('\n'),
    inserted,
  };
}

let fileCount = 0;
let goCount = 0;

for (const name of fs.readdirSync(directory)) {
  if (!name.endsWith('.sql')) {
    continue;
  }

  const filePath = path.join(directory, name);
  const original = fs.readFileSync(filePath, 'utf8');
  const { content, inserted } = insertBatchGo(original);
  if (inserted > 0) {
    fs.writeFileSync(filePath, content, 'utf8');
    fileCount += 1;
    goCount += inserted;
    console.log(`${name}: inserted ${inserted} GO`);
  }
}

console.log(`Inserted ${goCount} GO batch separators into ${fileCount} files`);
