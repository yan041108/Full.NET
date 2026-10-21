import { spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const policyUrl = new URL('../security/client-audit-policy.json', import.meta.url);
const severityLevels = ['info', 'low', 'moderate', 'high', 'critical'];
const retryableAuditErrorCodes = new Set([
  'ERR_SOCKET_TIMEOUT',
  'ERR_PNPM_AUDIT_BAD_RESPONSE'
]);

function getExceptionAdvisory(exception) {
  return exception.advisory ?? exception.ghsa;
}

function validateExceptionMetadata(exception, now) {
  const advisory = getExceptionAdvisory(exception);
  if (typeof advisory !== 'string' || advisory.length === 0) {
    throw new Error('Security exception must declare a non-empty advisory identifier.');
  }
  if (typeof exception.package !== 'string' || exception.package.length === 0) {
    throw new Error(`Security exception ${advisory} must declare an exact package name.`);
  }
  if (!Array.isArray(exception.allowedPaths) || exception.allowedPaths.length === 0) {
    throw new Error(`Security exception ${advisory} must declare non-empty allowedPaths.`);
  }
  if (exception.allowedPaths.some(path => typeof path !== 'string' || path.length === 0 || /[*?]/u.test(path))) {
    throw new Error(`Security exception ${advisory} must not use wildcards in allowedPaths.`);
  }
  if (typeof exception.rationale !== 'string' || exception.rationale.trim().length === 0) {
    throw new Error(`Security exception ${advisory} must declare a non-empty rationale.`);
  }
  if (!Array.isArray(exception.mitigations) || exception.mitigations.length === 0 || exception.mitigations.some(item => typeof item !== 'string' || item.trim().length === 0)) {
    throw new Error(`Security exception ${advisory} must declare non-empty mitigations.`);
  }
  if (typeof exception.owner !== 'string' || exception.owner.trim().length === 0) {
    throw new Error(`Security exception ${advisory} must declare an owner.`);
  }
  if (typeof exception.reviewBy !== 'string' || exception.reviewBy.length === 0) {
    throw new Error(`Security exception ${advisory} must declare reviewBy.`);
  }
  if (typeof exception.expiresOn !== 'string' || exception.expiresOn.length === 0) {
    throw new Error(`Security exception ${advisory} must declare expiresOn.`);
  }
  if (now.toISOString().slice(0, 10) > exception.expiresOn) {
    throw new Error(`Security exception ${advisory} expired on ${exception.expiresOn}.`);
  }
}

/** 按版本化严重级别门槛校验官方 npm 报告；未知结构和不一致计数必须失败关闭。 */
export function evaluateAuditReport(report, policy, now = new Date()) {
  if (!report || typeof report !== 'object' || !report.advisories || typeof report.advisories !== 'object'
    || Array.isArray(report.advisories)) {
    throw new Error('Audit JSON does not expose an advisories object.');
  }
  if (!policy || policy.version !== 1 || !Array.isArray(policy.exceptions)
    || !severityLevels.includes(policy.minimumSeverity)) {
    throw new Error('Client audit policy is invalid or unsupported.');
  }

  const acceptedExceptions = [];
  const findingCounts = Object.fromEntries(severityLevels.map(severity => [severity, 0]));
  for (const advisory of Object.values(report.advisories)) {
    if (!advisory || typeof advisory !== 'object') {
      throw new Error('Audit JSON contains an invalid advisory entry.');
    }
    if (!severityLevels.includes(advisory.severity)) {
      throw new Error('Audit JSON contains an invalid severity.');
    }
    if (advisory.severity === 'critical') {
      throw new Error(`Critical advisory is never allowed: ${advisory.github_advisory_id ?? 'unknown'}.`);
    }
    const findings = Array.isArray(advisory.findings) ? advisory.findings : [];
    const paths = findings.flatMap(finding => Array.isArray(finding?.paths) ? finding.paths : []);
    if (findings.length === 0 || findings.some(finding => !Array.isArray(finding?.paths) || finding.paths.length === 0)
      || paths.some(path => typeof path !== 'string' || path.length === 0)) {
      throw new Error(`Advisory ${advisory.github_advisory_id ?? 'unknown'} does not expose non-empty findings paths.`);
    }
    // npm 元数据按受影响版本组计数；一条公告可以包含多个版本，不能按公告条数核对。
    findingCounts[advisory.severity] += findings.length;
    if (severityLevels.indexOf(advisory.severity) < severityLevels.indexOf(policy.minimumSeverity)) {
      continue;
    }

    const exception = policy.exceptions.find(item => getExceptionAdvisory(item) === advisory.github_advisory_id);
    if (!exception) {
      throw new Error(`Unreviewed ${advisory.severity} advisory: ${advisory.github_advisory_id ?? 'unknown'}.`);
    }
    validateExceptionMetadata(exception, now);
    if (advisory.module_name !== exception.package) {
      throw new Error(`Advisory ${getExceptionAdvisory(exception)} does not match the reviewed package ${exception.package}.`);
    }

    if (paths.some(path => !exception.allowedPaths.includes(path))) {
      throw new Error(`Advisory ${getExceptionAdvisory(exception)} is outside the reviewed uni-app toolchain.`);
    }

    acceptedExceptions.push({
      advisory: getExceptionAdvisory(exception),
      package: exception.package,
      paths: [...new Set(paths)]
    });
  }

  const counts = report.metadata?.vulnerabilities;
  if (!counts || severityLevels.some(severity => !Number.isSafeInteger(counts[severity]) || counts[severity] < 0)) {
    throw new Error('Audit JSON does not expose reliable severity metadata counts.');
  }
  if (Object.keys(counts).some(severity => !severityLevels.includes(severity))) {
    throw new Error('Audit JSON contains unknown severity metadata.');
  }
  if (severityLevels.some(severity => counts[severity] !== findingCounts[severity])) {
    throw new Error('Audit JSON finding counts do not match metadata.');
  }

  return { acceptedExceptions };
}

/**
 * 从 npm 审计命令收集可信报告，并仅对明确的上游传输错误执行有限重试。
 *
 * @param {() => { error?: Error, status: number | null, stdout: string, stderr: string }} runAudit 执行一次审计命令的回调。
 * @param {{ maxAttempts?: number, waitBeforeRetry?: () => Promise<void>, onRetry?: (message: string) => void }} options 重试次数、等待与诊断回调。
 * @returns {Promise<object>} 可交给安全策略评估器的审计报告。
 */
export async function collectAuditReport(runAudit, options = {}) {
  const maxAttempts = options.maxAttempts ?? 3;
  const waitBeforeRetry = options.waitBeforeRetry ?? (() => new Promise(resolve => setTimeout(resolve, 5_000)));
  const onRetry = options.onRetry ?? (() => {});

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const audit = runAudit();
    if (audit.error) {
      throw audit.error;
    }

    let report;
    try {
      report = JSON.parse(audit.stdout);
    } catch {
      throw new Error('pnpm audit did not return valid JSON from the official npm registry.');
    }

    const auditErrorCode = report?.error?.code;
    if (retryableAuditErrorCodes.has(auditErrorCode)) {
      const detail = report.error.message ?? auditErrorCode;
      if (attempt < maxAttempts) {
        onRetry(`pnpm audit transport failed (${detail}); retrying attempt ${attempt + 1} of ${maxAttempts}.`);
        await waitBeforeRetry();
        continue;
      }
      throw new Error(`pnpm audit transport failed after ${maxAttempts} attempts: ${detail}`);
    }

    if (audit.status !== 0 && audit.status !== 1) {
      throw new Error(`pnpm audit could not produce a report (exit ${audit.status}): ${audit.stderr.trim()}`);
    }
    return report;
  }

  throw new Error('pnpm audit retry loop ended without a report.');
}

async function main() {
  const policy = JSON.parse(await readFile(policyUrl, 'utf8'));
  const pnpmCli = process.env.npm_execpath;
  if (!pnpmCli) {
    throw new Error('pnpm audit runner requires npm_execpath from a pnpm script.');
  }
  const report = await collectAuditReport(
    () => spawnSync(process.execPath, [
      pnpmCli,
      'audit',
      '--json',
      `--registry=${policy.registry}`
    ], {
      cwd: fileURLToPath(new URL('..', import.meta.url)),
      encoding: 'utf8',
      maxBuffer: 4 * 1024 * 1024
    }),
    { onRetry: message => console.warn(message) }
  );
  const result = evaluateAuditReport(report, policy);
  for (const exception of result.acceptedExceptions) {
    console.log(`Accepted reviewed security exception ${exception.advisory} for ${exception.package}.`);
    console.log(`Paths: ${exception.paths.join(', ')}`);
  }
  console.log(`Client audit gate found no unreviewed ${policy.minimumSeverity} or higher advisories.`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
