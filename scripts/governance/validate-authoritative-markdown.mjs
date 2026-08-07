import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultRepositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

const utf8Decoder = new TextDecoder('utf-8', { fatal: true });

export const authoritativeMarkdownRoots = [
  'AGENTS.md',
  'rules',
  '.agents/skills',
  'docs/architecture',
  'docs/roadmap',
  'docs/superpowers/specs',
  'docs/superpowers/plans',
  'docs/operations',
];

export async function collectAuthoritativeMarkdownFiles(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const files = new Set();

  for (const entry of authoritativeMarkdownRoots) {
    const absolute = path.join(root, entry);
    try {
      const info = await stat(absolute);
      if (info.isFile()) {
        if (entry.endsWith('.md')) {
          files.add(normalizeRelative(entry));
        }
        continue;
      }
      if (info.isDirectory()) {
        await collectMarkdownUnder(root, entry, files);
      }
    } catch {
      throw new Error(`Authoritative Markdown root is missing: ${entry}`);
    }
  }

  return [...files].sort((left, right) => left.localeCompare(right));
}

export function validateMarkdownBuffer(relativePath, buffer) {
  const normalizedPath = normalizeRelative(relativePath);
  const bytes = buffer instanceof Uint8Array ? buffer : Uint8Array.from(buffer);

  if (bytes.length >= 2 && bytes[0] === 0xFF && bytes[1] === 0xFE) {
    return [formatViolation(normalizedPath, 1, 1, 'UTF-16 LE BOM is not allowed in authoritative Markdown.')];
  }
  if (bytes.length >= 2 && bytes[0] === 0xFE && bytes[1] === 0xFF) {
    return [formatViolation(normalizedPath, 1, 1, 'UTF-16 BE BOM is not allowed in authoritative Markdown.')];
  }

  let text;
  try {
    text = utf8Decoder.decode(bytes);
  } catch {
    const byteOffset = findFirstInvalidUtf8Byte(bytes);
    const position = byteOffsetToLineColumn(bytes, byteOffset);
    return [formatViolation(
      normalizedPath,
      position.line,
      position.column,
      `Invalid UTF-8 byte sequence at byte offset ${byteOffset + 1}.`)];
  }

  const violations = [];
  const replacementIndex = text.indexOf('\uFFFD');
  if (replacementIndex !== -1) {
    const position = indexToLineColumn(text, replacementIndex);
    violations.push(formatViolation(
      normalizedPath,
      position.line,
      position.column,
      'Replacement character U+FFFD is not allowed in authoritative Markdown.'));
  }

  const prose = stripProtectedRegions(text);
  const mojibake = /(?:\?){3,}/.exec(prose);
  if (mojibake) {
    const sourceIndex = mapProseIndexToSourceIndex(text, mojibake.index);
    const position = indexToLineColumn(text, sourceIndex);
    violations.push(formatViolation(
      normalizedPath,
      position.line,
      position.column,
      'Consecutive ASCII question marks (???) indicate mojibake in authoritative Chinese prose.'));
  }

  return violations;
}

export async function validateAuthoritativeMarkdown(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const files = await collectAuthoritativeMarkdownFiles(root);
  const violations = [];
  for (const relativePath of files) {
    const buffer = await readFile(path.join(root, relativePath));
    violations.push(...validateMarkdownBuffer(relativePath, buffer));
  }
  return violations;
}

async function collectMarkdownUnder(root, relativeDirectory, files) {
  const absoluteDirectory = path.join(root, relativeDirectory);
  const { readdir } = await import('node:fs/promises');
  for (const entry of await readdir(absoluteDirectory, { withFileTypes: true })) {
    const relativePath = normalizeRelative(path.join(relativeDirectory, entry.name));
    if (entry.isDirectory()) {
      await collectMarkdownUnder(root, relativePath, files);
      continue;
    }
    if (entry.isFile() && entry.name.endsWith('.md')) {
      files.add(relativePath);
    }
  }
}

function stripProtectedRegions(text) {
  let result = text;
  result = result.replace(/```[\s\S]*?```/g, match => ' '.repeat(match.length));
  result = result.replace(/`[^`\n]+`/g, match => ' '.repeat(match.length));
  result = result.replace(/https?:\/\/[^\s)>\]]+/gi, match => ' '.repeat(match.length));
  result = result.replace(/\[[^\]]*]\([^)]*\)/g, match => ' '.repeat(match.length));
  return result;
}

function mapProseIndexToSourceIndex(source, proseIndex) {
  let sourceIndex = 0;
  let proseCursor = 0;
  while (sourceIndex < source.length && proseCursor < proseIndex) {
    const slice = source.slice(sourceIndex);
    const protectedMatch = slice.match(/^```[\s\S]*?```|^`[^`\n]+`|https?:\/\/[^\s)>\]]+|\[[^\]]*]\([^)]*\)/);
    if (protectedMatch && protectedMatch.index === 0) {
      const token = protectedMatch[0];
      sourceIndex += token.length;
      proseCursor += token.length;
      continue;
    }

    sourceIndex += 1;
    proseCursor += 1;
  }

  return sourceIndex;
}

function findFirstInvalidUtf8Byte(bytes) {
  for (let end = 1; end <= bytes.length; end += 1) {
    try {
      utf8Decoder.decode(bytes.subarray(0, end));
    } catch {
      return end - 1;
    }
  }
  return 0;
}

function byteOffsetToLineColumn(bytes, byteOffset) {
  let line = 1;
  let column = 1;
  for (let index = 0; index < byteOffset; index += 1) {
    if (bytes[index] === 0x0A) {
      line += 1;
      column = 1;
      continue;
    }
    if (bytes[index] === 0x0D) {
      continue;
    }
    column += 1;
  }
  return { line, column };
}

function indexToLineColumn(text, index) {
  const prefix = text.slice(0, index);
  const lines = prefix.split(/\r?\n/);
  return {
    line: lines.length,
    column: (lines.at(-1)?.length ?? 0) + 1,
  };
}

function formatViolation(relativePath, line, column, message) {
  return `${normalizeRelative(relativePath)}:${line}:${column}: ${message}`;
}

function normalizeRelative(relativePath) {
  return relativePath.replace(/\\/g, '/');
}

async function main() {
  const violations = await validateAuthoritativeMarkdown();
  if (violations.length > 0) {
    console.error(violations.join('\n'));
    process.exitCode = 1;
    return;
  }
  const files = await collectAuthoritativeMarkdownFiles();
  console.log(`Authoritative Markdown integrity: ${files.length} files OK.`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  main().catch(error => {
    console.error(error);
    process.exitCode = 1;
  });
}