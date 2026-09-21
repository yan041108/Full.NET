import { execSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { classifyL3Failures } from '../classify-l3-failures.mjs';
import { freeLocalE2ePorts } from '../free-local-e2e-ports.mjs';
import { listAllL3SpecFiles } from './qa-l3-mapping.mjs';

function parsePlaywrightSummary(output) {
  const passed = output.match(/(\d+)\s+passed(?:\s*\(|$)/m);
  const failed = output.match(/(\d+)\s+failed(?:\s*\(|$)/m);
  const skipped = output.match(/(\d+)\s+skipped(?:\s*\(|$)/m);
  return {
    passed: passed ? Number(passed[1]) : null,
    failed: failed ? Number(failed[1]) : 0,
    skipped: skipped ? Number(skipped[1]) : 0
  };
}

/**
 * 运行全部 admin-real-stack spec（vue-admin + vue-admin-oidc-center 项目）。
 * @param {string} rootDir - admin-real-stack 根目录
 */
export function runL3FullSuite(rootDir) {
  const specFiles = listAllL3SpecFiles();
  const artifactsDir = join(rootDir, '.artifacts');
  mkdirSync(artifactsDir, { recursive: true });
  const jsonReportPath = join(artifactsDir, 'playwright-l3-report.json');
  const command = 'pnpm exec playwright test';
  const reuseServer = process.env.FULLNET_E2E_REUSE_SERVER ?? '0';
  if (reuseServer !== '1') {
    freeLocalE2ePorts();
  }
  const env = {
    ...process.env,
    FULLNET_E2E_L3_JSON_REPORT: jsonReportPath,
    FULLNET_E2E_SKIP_BOOTSTRAP: process.env.FULLNET_E2E_SKIP_BOOTSTRAP ?? '1',
    FULLNET_E2E_API_URL: process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149',
    FULLNET_E2E_REUSE_SERVER: reuseServer,
    FULLNET_E2E_ADMIN_URL:
      process.env.FULLNET_E2E_ADMIN_URL ?? process.env.FULLNET_E2E_ADMIN_ORIGIN ?? 'http://localhost:25173',
    FULLNET_E2E_ADMIN_ORIGIN:
      process.env.FULLNET_E2E_ADMIN_ORIGIN ?? process.env.FULLNET_E2E_ADMIN_URL ?? 'http://localhost:25173'
  };
  let stdout = '';
  let stderr = '';
  try {
    stdout = execSync(command, {
      cwd: rootDir,
      encoding: 'utf8',
      timeout: 7_200_000,
      env,
      stdio: 'pipe',
      maxBuffer: 20 * 1024 * 1024
    });
    const stats = parsePlaywrightSummary(stdout);
    const classification = classifyL3Failures(jsonReportPath);
    return {
      specFileCount: specFiles.length,
      specFiles,
      command,
      exitCode: 0,
      jsonReportPath,
      failureClassificationPath: classification.outPath,
      failureCount: classification.failures.length,
      ...stats,
      summary: `全量 ${specFiles.length} 个 spec 文件：${stats.passed ?? '?'} 用例通过，${stats.failed} 失败，${stats.skipped} 跳过`,
      stdoutTail: stdout.slice(-4000)
    };
  } catch (error) {
    stdout = error?.stdout?.toString?.() ?? '';
    stderr = error?.stderr?.toString?.() ?? '';
    const combined = `${stdout}\n${stderr}`;
    const stats = parsePlaywrightSummary(combined);
    const classification = classifyL3Failures(jsonReportPath);
    return {
      specFileCount: specFiles.length,
      specFiles,
      command,
      exitCode: error.status ?? 1,
      jsonReportPath,
      failureClassificationPath: classification.outPath,
      failureCount: classification.failures.length,
      ...stats,
      summary: `全量 ${specFiles.length} 个 spec 文件：${stats.passed ?? '?'} 用例通过，${stats.failed ?? '?'} 失败，${stats.skipped} 跳过`,
      stdoutTail: combined.slice(-4000),
      stderrTail: stderr.slice(-2000)
    };
  }
}

export function writeL3Artifact(rootDir, l3Result) {
  const path = join(rootDir, '.artifacts/host-admin-l3-full.json');
  writeFileSync(path, JSON.stringify({ ranAt: new Date().toISOString(), ...l3Result }, null, 2), 'utf8');
  return path;
}
