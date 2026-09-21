/**
 * 仅跑 L3：admin-real-stack 全部 59 个 spec，并合并进 host-admin-qa-audit.json / 报告。
 * 附着本机栈：先 `node scripts/resolve-attach-stack-env.mjs` 配置 Host.Api/Worker 环境并启动，再本脚本。
 * 用法：node scripts/host-admin-l3-full.mjs [--skip-preflight]
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { buildQaReportMarkdown } from './support/qa-report-builder.mjs';
import { runL3FullSuite, writeL3Artifact } from './support/qa-l3-runner.mjs';

const rootDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const skipPreflight = process.argv.includes('--skip-preflight');
const auditPath = join(rootDir, '.artifacts/host-admin-qa-audit.json');
const reportPath = join(rootDir, '.artifacts/host-admin-qa-report.md');

if (!skipPreflight) {
  console.log('Preflight local stack...');
  const pre = spawnSync('node', ['scripts/preflight-local-stack.mjs'], {
    cwd: rootDir,
    stdio: 'inherit',
    env: process.env
  });
  if (pre.status !== 0) {
    process.exit(pre.status ?? 1);
  }
}

console.log('Running full L3 Playwright suite (59 spec files)...');
const l3 = runL3FullSuite(rootDir);
const artifactPath = writeL3Artifact(rootDir, l3);
console.log(l3.summary);
console.log(`L3 artifact: ${artifactPath}`);

if (existsSync(auditPath)) {
  const audit = JSON.parse(readFileSync(auditPath, 'utf8'));
  audit.l3Playwright = l3;
  audit.l3PlaywrightUpdatedAt = new Date().toISOString();
  writeFileSync(auditPath, JSON.stringify(audit, null, 2), 'utf8');
  writeFileSync(reportPath, buildQaReportMarkdown(audit), 'utf8');
  console.log(`Updated: ${reportPath}`);
} else {
  console.warn('No host-admin-qa-audit.json; only L3 artifact written.');
}

if (l3.exitCode !== 0) {
  process.exitCode = 1;
}
