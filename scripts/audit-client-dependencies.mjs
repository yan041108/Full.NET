import { spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const policyUrl = new URL('../security/client-audit-policy.json', import.meta.url);

/** 根据版本化策略检查 npm audit JSON，只返回经过审查的高危例外。 */
export function evaluateAuditReport(report, policy, now = new Date()) {
  if (!report || typeof report !== 'object' || !report.advisories || typeof report.advisories !== 'object') {
    throw new Error('Audit JSON does not expose an advisories object.');
  }
  if (!policy || policy.version !== 1 || !Array.isArray(policy.exceptions)) {
    throw new Error('Client audit policy is invalid or unsupported.');
  }

  const acceptedExceptions = [];
  for (const advisory of Object.values(report.advisories)) {
    if (!advisory || typeof advisory !== 'object') {
      throw new Error('Audit JSON contains an invalid advisory entry.');
    }
    if (advisory.severity === 'critical') {
      throw new Error(`Critical advisory is never allowed: ${advisory.github_advisory_id ?? 'unknown'}.`);
    }
    if (advisory.severity !== 'high') {
      continue;
    }

    const exception = policy.exceptions.find(item => item.ghsa === advisory.github_advisory_id);
    if (!exception) {
      throw new Error(`Unreviewed high advisory: ${advisory.github_advisory_id ?? 'unknown'}.`);
    }
    if (advisory.module_name !== exception.package) {
      throw new Error(`Advisory ${exception.ghsa} does not match the reviewed package ${exception.package}.`);
    }
    if (now.toISOString().slice(0, 10) > exception.reviewBy) {
      throw new Error(`Security exception ${exception.ghsa} expired on ${exception.reviewBy}.`);
    }

    const findings = Array.isArray(advisory.findings) ? advisory.findings : [];
    const paths = findings.flatMap(finding => Array.isArray(finding?.paths) ? finding.paths : []);
    if (paths.length === 0 || paths.some(path => typeof path !== 'string' || path.length === 0)) {
      throw new Error(`Advisory ${exception.ghsa} does not expose non-empty findings paths.`);
    }
    if (paths.some(path => !exception.allowedPaths.includes(path))) {
      throw new Error(`Advisory ${exception.ghsa} is outside the reviewed uni-app toolchain.`);
    }

    acceptedExceptions.push({
      ghsa: exception.ghsa,
      package: exception.package,
      paths: [...new Set(paths)]
    });
  }

  const counts = report.metadata?.vulnerabilities;
  if (!counts || typeof counts.high !== 'number' || typeof counts.critical !== 'number') {
    throw new Error('Audit JSON does not expose high and critical metadata counts.');
  }
  const advisoryCounts = Object.values(report.advisories).reduce((result, advisory) => {
    if (advisory.severity === 'high' || advisory.severity === 'critical') {
      result[advisory.severity] += 1;
    }
    return result;
  }, { high: 0, critical: 0 });
  if (counts.high !== advisoryCounts.high || counts.critical !== advisoryCounts.critical) {
    throw new Error('Audit JSON advisory counts do not match metadata.');
  }

  return { acceptedExceptions };
}

async function main() {
  const policy = JSON.parse(await readFile(policyUrl, 'utf8'));
  const pnpmCli = process.env.npm_execpath;
  if (!pnpmCli) {
    throw new Error('pnpm audit runner requires npm_execpath from a pnpm script.');
  }
  const audit = spawnSync(process.execPath, [
    pnpmCli,
    'audit',
    '--json',
    `--registry=${policy.registry}`
  ], {
    cwd: fileURLToPath(new URL('..', import.meta.url)),
    encoding: 'utf8',
    maxBuffer: 4 * 1024 * 1024
  });
  if (audit.error) {
    throw audit.error;
  }
  if (audit.status !== 0 && audit.status !== 1) {
    throw new Error(`pnpm audit could not produce a report (exit ${audit.status}): ${audit.stderr.trim()}`);
  }

  let report;
  try {
    report = JSON.parse(audit.stdout);
  } catch {
    throw new Error('pnpm audit did not return valid JSON from the official npm registry.');
  }
  const result = evaluateAuditReport(report, policy);
  for (const exception of result.acceptedExceptions) {
    console.log(`Accepted reviewed security exception ${exception.ghsa} for ${exception.package}.`);
    console.log(`Paths: ${exception.paths.join(', ')}`);
  }
  console.log('Client audit gate found no unreviewed critical or high advisories.');
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
