#!/usr/bin/env node
/**
 * 按 rules/code-comments.md 扫描手写 C# 的注释缺口（不含 obj/bin/生成文件）。
 * 用法：
 *   node scripts/governance/audit-code-comments.mjs
 *   node scripts/governance/audit-code-comments.mjs --english-summary
 *   node scripts/governance/audit-code-comments.mjs --no-summary --limit 50
 *   node scripts/governance/audit-code-comments.mjs --roots=tests/governance/fixtures
 */
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..',
);

const args = new Set(process.argv.slice(2));
const flagEnglish = args.has('--english-summary');
const flagNoSummary = args.has('--no-summary');
const limitArg = process.argv.find((a) => a.startsWith('--limit='));
const limit = limitArg ? Number(limitArg.split('=')[1]) : Infinity;

const skipDir = new Set(['obj', 'bin', 'node_modules']);
const skipFile =
  /(AssemblyInfo\.cs|AssemblyAttributes\.cs|GlobalUsings\.cs|GlobalUsings\.g\.cs|\.resources\.cs|\.g\.cs|\.fragment\.cs)$/;

const publicType =
  /^\s*public\s+(partial\s+)?(sealed\s+)?(class|interface|struct|enum|record)\s+/;
/** 仅标记几乎全英文、且不含中文的 summary（术语与中文混排视为合规）。 */
function isEnglishOnlySummaryLine(line) {
  const match = line.match(/\/\/\/\s*<summary>\s*([^<]+)/);
  if (!match) return false;
  const text = match[1].trim();
  if (/[\u4e00-\u9fff]/.test(text)) return false;
  return text.length >= 12 && /[A-Za-z]/.test(text);
}

/** 代码生成器字符串模板内的 public 声明不是真实类型。 */
const codegenTemplateLine = /\{schema\.|\{\{/;

function isSkippableBeforePublic(line) {
  const t = line.trim();
  if (t === '') return true;
  if (t.startsWith('[')) return true;
  if (t.startsWith('#')) return true;
  return false;
}

function hasXmlDocBefore(lines, index) {
  let j = index - 1;
  while (j >= 0 && isSkippableBeforePublic(lines[j])) j--;
  if (j < 0 || !lines[j].trimStart().startsWith('///')) return false;
  let start = j;
  while (start >= 0 && lines[start].trimStart().startsWith('///')) start--;
  const block = lines.slice(start + 1, j + 1).join('\n');
  return block.includes('<summary>') || block.includes('inheritdoc');
}

async function walk(dir, out) {
  const entries = await readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (skipDir.has(entry.name)) continue;
      await walk(path.join(dir, entry.name), out);
      continue;
    }
    if (!entry.name.endsWith('.cs') || skipFile.test(entry.name)) continue;
    out.push(path.join(dir, entry.name));
  }
}

async function auditFile(filePath) {
  const rel = path.relative(repositoryRoot, filePath).replaceAll('\\', '/');
  const text = await readFile(filePath, 'utf8');
  const lines = text.split(/\r?\n/);
  const issues = [];

  if (flagNoSummary && !text.includes('/// <summary>')) {
    issues.push({ kind: 'file-no-summary', line: 1, detail: rel });
  }

  if (flagEnglish) {
    for (let i = 0; i < lines.length; i++) {
      if (isEnglishOnlySummaryLine(lines[i])) {
        issues.push({
          kind: 'english-summary',
          line: i + 1,
          detail: lines[i].trim(),
        });
      }
    }
  }

  for (let i = 0; i < lines.length; i++) {
    if (!publicType.test(lines[i])) continue;
    if (codegenTemplateLine.test(lines[i])) continue;
    // Roslyn/生成器测试的内嵌源码字符串里会出现深缩进的 public 声明。
    if (/^\s{5,}public\s/.test(lines[i])) continue;
    if (!hasXmlDocBefore(lines, i)) {
      issues.push({
        kind: 'public-type-no-summary',
        line: i + 1,
        detail: lines[i].trim().slice(0, 100),
      });
    }
  }

  return issues.map((issue) => ({ ...issue, file: rel }));
}

const rootsArg = process.argv.find((a) => a.startsWith('--roots='));
const rootNames = rootsArg
  ? rootsArg.slice('--roots='.length).split(',').filter(Boolean)
  : ['src', 'tests', 'scripts'];
const roots = rootNames.map((r) =>
  path.isAbsolute(r) ? r : path.join(repositoryRoot, r),
);
const files = [];
for (const root of roots) {
  try {
    await walk(root, files);
  } catch {
    // tests/scripts 可能不存在于裁剪工作区
  }
}

let all = [];
for (const file of files) {
  all.push(...(await auditFile(file)));
}

const kinds = flagNoSummary
  ? ['file-no-summary']
  : flagEnglish
    ? ['english-summary']
    : ['public-type-no-summary'];

all = all.filter((i) => kinds.includes(i.kind));
const shown = all.slice(0, limit);

const summary = {
  scannedFiles: files.length,
  issueCount: all.length,
  kinds,
  sample: shown,
};

console.log(JSON.stringify(summary, null, 2));
