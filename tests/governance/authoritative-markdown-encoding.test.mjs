import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  collectAuthoritativeMarkdownFiles,
  validateAuthoritativeMarkdown,
  validateMarkdownBuffer,
} from '../../scripts/governance/validate-authoritative-markdown.mjs';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

test('validateMarkdownBuffer accepts valid UTF-8 Chinese prose', () => {
  const content = '# 标题\n\n这是权威中文文档正文。\n';
  const violations = validateMarkdownBuffer(
    'docs/roadmap/example.md',
    Buffer.from(content, 'utf8'));
  assert.deepEqual(violations, []);
});

test('validateMarkdownBuffer rejects truncated multibyte UTF-8', () => {
  const bytes = Buffer.from('中文', 'utf8').subarray(0, 4);
  const violations = validateMarkdownBuffer('rules/example.md', bytes);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /Invalid UTF-8 byte sequence/);
  assert.match(violations[0], /^rules\/example\.md:\d+:\d+:/);
});

test('validateMarkdownBuffer rejects UTF-16 BOM payloads', () => {
  const bytes = Buffer.from([0xFF, 0xFE, 0x41, 0x00]);
  const violations = validateMarkdownBuffer('AGENTS.md', bytes);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /UTF-16 LE BOM/);
});

test('validateMarkdownBuffer rejects explicit U+FFFD replacement characters', () => {
  const content = '# 标题\n\n损坏\uFFFD字符\n';
  const violations = validateMarkdownBuffer(
    'docs/architecture/example.md',
    Buffer.from(content, 'utf8'));
  assert.equal(violations.length, 1);
  assert.match(violations[0], /U\+FFFD/);
});

test('validateMarkdownBuffer rejects mojibake question-mark runs in prose', () => {
  const content = '# Full.NET ?????????\n\n???????????????\n';
  const violations = validateMarkdownBuffer(
    'docs/development/getting-started.md',
    Buffer.from(content, 'utf8'));
  assert.equal(violations.length, 1);
  assert.match(violations[0], /Consecutive ASCII question marks/);
});

test('validateMarkdownBuffer allows question marks inside fenced code', () => {
  const content = '# 标题\n\n```powershell\nif ($x -eq $null) { Write-Output "???" }\n```\n';
  const violations = validateMarkdownBuffer(
    'docs/operations/example.md',
    Buffer.from(content, 'utf8'));
  assert.deepEqual(violations, []);
});

test('validateMarkdownBuffer allows inline code and URL query strings', () => {
  const content = [
    '# 标题',
    '',
    '使用 `value?.length` 与链接 https://example.com/search?q=test???ok。',
    '',
    '中文问号？可以保留。',
    '',
  ].join('\n');
  const violations = validateMarkdownBuffer(
    'docs/superpowers/specs/example.md',
    Buffer.from(content, 'utf8'));
  assert.deepEqual(violations, []);
});

test('repository authoritative Markdown scan has zero encoding violations', async () => {
  const files = await collectAuthoritativeMarkdownFiles(repositoryRoot);
  assert.ok(files.length > 0, 'authoritative Markdown inventory must be non-empty');
  const violations = await validateAuthoritativeMarkdown(repositoryRoot);
  assert.deepEqual(
    violations,
    [],
    violations.length > 0
      ? `Authoritative Markdown violations:\n${violations.join('\n')}`
      : undefined);
});

async function seedAuthoritativeMarkdownRoots(tempRoot) {
  const roots = [
    '.agents/skills',
    'docs/architecture',
    'docs/roadmap',
    'docs/superpowers/specs',
    'docs/superpowers/plans',
    'docs/operations',
    'rules',
  ];
  for (const relative of roots) {
    await mkdir(path.join(tempRoot, relative), { recursive: true });
    await writeFile(path.join(tempRoot, relative, 'placeholder.md'), '# 占位\n', 'utf8');
  }
}

test('temporary fixture tree is scanned without path allowlists', async () => {
  const tempRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-md-integrity-'));
  await seedAuthoritativeMarkdownRoots(tempRoot);
  await writeFile(path.join(tempRoot, 'AGENTS.md'), '# 代理入口\n', 'utf8');
  await writeFile(path.join(tempRoot, 'rules', 'broken.md'), '# 损坏\n\n????\n', 'utf8');

  const violations = await validateAuthoritativeMarkdown(tempRoot);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /^rules\/broken\.md:/);
});