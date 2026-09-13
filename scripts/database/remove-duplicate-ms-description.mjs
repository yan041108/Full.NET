import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const directory = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer',
);

const execStatementRe = /EXEC sys\.sp_addextendedproperty @name=N'MS_Description'[\s\S]*?;/;
const keyRe = /@level1name=N'([^']+)'(?:, @level2type=N'COLUMN', @level2name=N'([^']+)')?/;

const multilineGuardedBlockRe =
  /[ \t]*IF NOT EXISTS \(\s*\n[\s\S]*?\n[ \t]*\)\s*(?:\n[ \t]*)*\n[ \t]*EXEC sys\.sp_addextendedproperty @name=N'MS_Description'[\s\S]*?;/g;

const singleLineGuardedBlockRe =
  /[ \t]*IF NOT EXISTS \(SELECT 1 FROM sys\.extended_properties[^\n]*\)\s*\n[ \t]*EXEC sys\.sp_addextendedproperty @name=N'MS_Description'[\s\S]*?;/g;

const unguardedStatementRe =
  /^[ \t]*EXEC sys\.sp_addextendedproperty @name=N'MS_Description'[\s\S]*?;/gm;

function statementKey(block) {
  const match = block.match(execStatementRe);
  if (!match) {
    return null;
  }

  const keyMatch = match[0].match(keyRe);
  if (!keyMatch) {
    return null;
  }

  return `${keyMatch[1]}|${keyMatch[2] ?? ''}`;
}

function collectGuardedKeys(content) {
  const keys = new Set();

  for (const pattern of [multilineGuardedBlockRe, singleLineGuardedBlockRe]) {
    content.replace(pattern, (block) => {
      const key = statementKey(block);
      if (key) {
        keys.add(key);
      }

      return block;
    });
  }

  return keys;
}

function cleanupContent(content) {
  const guardedKeys = collectGuardedKeys(content);
  const seen = new Set(guardedKeys);
  let removed = 0;

  let cleaned = content.replace(unguardedStatementRe, (statement, offset, full) => {
    const prefix = full.slice(0, offset);
    const lastLine = prefix.split(/\r?\n/).filter((line) => line.trim().length > 0).at(-1) ?? '';
    if (/\)\s*$/.test(lastLine) || /IF NOT EXISTS\s*\(/.test(lastLine)) {
      return statement;
    }

    const key = statementKey(statement);
    if (!key) {
      return statement;
    }

    if (!seen.has(key)) {
      seen.add(key);
      return statement;
    }

    removed += 1;
    return '';
  });

  cleaned = cleaned.replace(
    /[ \t]*IF NOT EXISTS \(\s*\n[\s\S]*?name = N'MS_Description'\s*\n[ \t]*\)\s*(?=\n[ \t]*\n|\n[ \t]*IF NOT EXISTS|\n[ \t]*END\b)/g,
    (block) => {
      if (execStatementRe.test(block)) {
        return block;
      }

      removed += 1;
      return '';
    },
  );

  cleaned = cleaned.replace(
    /[ \t]*IF NOT EXISTS \(SELECT 1 FROM sys\.extended_properties[^\n]*\)\s*\n(?=\s*\n|\s*IF NOT EXISTS|\s*END\b)/g,
    () => {
      removed += 1;
      return '';
    },
  );

  return {
    content: cleaned.replace(/\n{3,}/g, '\n\n'),
    removed,
  };
}

let fileCount = 0;
let lineCount = 0;

for (const name of fs.readdirSync(directory)) {
  if (!name.endsWith('.sql')) {
    continue;
  }

  const filePath = path.join(directory, name);
  const original = fs.readFileSync(filePath, 'utf8');
  const { content, removed } = cleanupContent(original);
  if (removed > 0) {
    fs.writeFileSync(filePath, content, 'utf8');
    fileCount += 1;
    lineCount += removed;
    console.log(`${name}: removed ${removed}`);
  }
}

console.log(`Removed ${lineCount} duplicate statements from ${fileCount} files`);
