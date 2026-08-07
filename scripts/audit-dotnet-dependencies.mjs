import { spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const policyUrl = new URL('../security/dotnet-audit-policy.json', import.meta.url);
const solutionPath = path.join(repoRoot, 'Full.NET.slnx');

const severityRank = {
  low: 1,
  moderate: 2,
  medium: 2,
  high: 3,
  critical: 4
};

export function toProjectSlug(projectPath, rootDirectory = repoRoot) {
  if (typeof projectPath !== 'string' || projectPath.length === 0) {
    throw new Error('Project path must be a non-empty string.');
  }
  const normalizedRoot = path.resolve(rootDirectory);
  const normalizedProject = path.resolve(projectPath);
  const relative = path.relative(normalizedRoot, normalizedProject);
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error(`Project path is outside the repository root: ${projectPath}`);
  }
  return relative.replace(/\\/g, '/').replace(/\//g, '__').replace(/\.csproj$/u, '');
}

export function extractAdvisoryId(advisoryUrl) {
  if (typeof advisoryUrl !== 'string' || advisoryUrl.length === 0) {
    throw new Error('Advisory URL must be a non-empty string.');
  }
  const match = advisoryUrl.match(/GHSA-[a-z0-9-]+|CVE-\d{4}-\d+/iu);
  if (!match) {
    throw new Error(`Advisory URL does not expose a GHSA or CVE identifier: ${advisoryUrl}`);
  }
  return match[0];
}

function normalizeSeverity(severity) {
  if (typeof severity !== 'string') {
    throw new Error('Vulnerability severity must be a string.');
  }
  const normalized = severity.trim().toLowerCase();
  if (!Object.hasOwn(severityRank, normalized)) {
    throw new Error(`Unsupported vulnerability severity: ${severity}`);
  }
  return normalized;
}

function meetsMinimumSeverity(severity, minimumSeverity) {
  return severityRank[severity] >= severityRank[normalizeSeverity(minimumSeverity)];
}

function assertPolicyShape(policy) {
  if (!policy || policy.schemaVersion !== 1 || !Array.isArray(policy.exceptions)) {
    throw new Error('Dotnet audit policy is invalid or unsupported.');
  }
  if (typeof policy.minimumSeverity !== 'string') {
    throw new Error('Dotnet audit policy must declare minimumSeverity.');
  }
  normalizeSeverity(policy.minimumSeverity);
}

function getExceptionAdvisory(exception) {
  return exception.advisory ?? exception.ghsa;
}

function validateExceptionMetadata(exception, now) {
  const advisory = getExceptionAdvisory(exception);
  if (typeof advisory !== 'string' || advisory.length === 0) {
    throw new Error('Security exception must declare a non-empty advisory identifier.');
  }
  if (typeof exception.package !== 'string' || exception.package.length === 0) {
    throw new Error(`Security exception ${advisory} must declare an exact package id.`);
  }
  if (!Array.isArray(exception.allowedPaths) || exception.allowedPaths.length === 0) {
    throw new Error(`Security exception ${advisory} must declare non-empty allowedPaths.`);
  }
  if (exception.allowedPaths.some(entry => typeof entry !== 'string' || entry.length === 0 || /[*?]/u.test(entry))) {
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

function collectPackages(framework, bucketName) {
  const packages = framework?.[bucketName];
  return Array.isArray(packages) ? packages : [];
}

export function extractVulnerabilityFindings(report, rootDirectory = repoRoot) {
  if (!report || typeof report !== 'object' || report.version !== 1 || !Array.isArray(report.projects)) {
    throw new Error('Dotnet audit JSON is missing the expected version/projects envelope.');
  }
  if (!Array.isArray(report.sources) || report.sources.length === 0) {
    throw new Error('Dotnet audit JSON does not expose vulnerability sources.');
  }

  const findings = [];
  for (const project of report.projects) {
    if (!project || typeof project.path !== 'string') {
      throw new Error('Dotnet audit JSON contains a project without a path.');
    }
    const frameworks = Array.isArray(project.frameworks) ? project.frameworks : [];
    for (const framework of frameworks) {
      if (!framework || typeof framework.framework !== 'string') {
        throw new Error(`Dotnet audit JSON project ${project.path} exposes an invalid framework entry.`);
      }
      const projectSlug = toProjectSlug(project.path, rootDirectory);
      for (const bucketName of ['topLevelPackages', 'transitivePackages']) {
        for (const pkg of collectPackages(framework, bucketName)) {
          if (!pkg || typeof pkg.id !== 'string') {
            throw new Error(`Dotnet audit JSON project ${project.path} exposes an invalid package entry.`);
          }
          const vulnerabilities = Array.isArray(pkg.vulnerabilities) ? pkg.vulnerabilities : [];
          for (const vulnerability of vulnerabilities) {
            if (!vulnerability || typeof vulnerability.advisoryurl !== 'string') {
              throw new Error(`Dotnet audit JSON project ${project.path} exposes an invalid vulnerability entry.`);
            }
            const severity = normalizeSeverity(vulnerability.severity);
            const dependencyPath = `${projectSlug}>${pkg.id}`;
            findings.push({
              advisory: extractAdvisoryId(vulnerability.advisoryurl),
              package: pkg.id,
              severity,
              projectPath: project.path,
              framework: framework.framework,
              resolvedVersion: typeof pkg.resolvedVersion === 'string' ? pkg.resolvedVersion : undefined,
              paths: [dependencyPath],
              isTransitive: bucketName === 'transitivePackages'
            });
          }
        }
      }
    }
  }

  return findings;
}

export function evaluateDotnetVulnerabilityFindings(findings, policy, now = new Date()) {
  assertPolicyShape(policy);
  if (!Array.isArray(findings)) {
    throw new Error('Dotnet vulnerability findings must be an array.');
  }

  const acceptedExceptions = [];
  for (const finding of findings) {
    if (!finding || typeof finding !== 'object') {
      throw new Error('Dotnet vulnerability findings contain an invalid entry.');
    }
    const severity = normalizeSeverity(finding.severity);
    if (!meetsMinimumSeverity(severity, policy.minimumSeverity)) {
      continue;
    }
    if (typeof finding.advisory !== 'string' || typeof finding.package !== 'string') {
      throw new Error('Dotnet vulnerability finding must declare advisory and package.');
    }
    const paths = Array.isArray(finding.paths) ? finding.paths : [];
    if (paths.length === 0 || paths.some(entry => typeof entry !== 'string' || entry.length === 0)) {
      throw new Error(`Advisory ${finding.advisory} does not expose non-empty dependency paths.`);
    }

    if (severity === 'critical') {
      throw new Error(`Critical advisory is never allowed: ${finding.advisory}.`);
    }

    const exception = policy.exceptions.find(item => getExceptionAdvisory(item).toLowerCase() === finding.advisory.toLowerCase());
    if (!exception) {
      throw new Error(`Unreviewed high advisory: ${finding.advisory}.`);
    }
    validateExceptionMetadata(exception, now);
    if (finding.package !== exception.package) {
      throw new Error(`Advisory ${finding.advisory} does not match the reviewed package ${exception.package}.`);
    }
    if (paths.some(entry => !exception.allowedPaths.includes(entry))) {
      throw new Error(`Advisory ${finding.advisory} is outside the reviewed dependency paths.`);
    }

    acceptedExceptions.push({
      advisory: finding.advisory,
      package: finding.package,
      paths: [...new Set(paths)]
    });
  }

  return { acceptedExceptions };
}

export function evaluateDotnetListReport(report, policy, now = new Date(), rootDirectory = repoRoot) {
  const findings = extractVulnerabilityFindings(report, rootDirectory);
  return evaluateDotnetVulnerabilityFindings(findings, policy, now);
}

function runDotnetListAudit() {
  const restore = spawnSync('dotnet', ['restore', solutionPath], {
    cwd: repoRoot,
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024
  });
  if (restore.error) {
    throw restore.error;
  }
  if (restore.status !== 0) {
    throw new Error(`dotnet restore failed (exit ${restore.status}): ${restore.stderr.trim()}`);
  }

  const audit = spawnSync('dotnet', [
    'list',
    solutionPath,
    'package',
    '--vulnerable',
    '--include-transitive',
    '--format',
    'json'
  ], {
    cwd: repoRoot,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024
  });
  if (audit.error) {
    throw audit.error;
  }
  if (audit.status !== 0) {
    throw new Error(`dotnet list package audit failed (exit ${audit.status}): ${audit.stderr.trim()}`);
  }

  let report;
  try {
    report = JSON.parse(audit.stdout);
  } catch {
    throw new Error('dotnet list package audit did not return valid JSON.');
  }

  return report;
}

async function main() {
  const policy = JSON.parse(await readFile(policyUrl, 'utf8'));
  const report = runDotnetListAudit();
  const result = evaluateDotnetListReport(report, policy);
  for (const exception of result.acceptedExceptions) {
    console.log(`Accepted reviewed security exception ${exception.advisory} for ${exception.package}.`);
    console.log(`Paths: ${exception.paths.join(', ')}`);
  }
  console.log('Dotnet audit gate found no unreviewed critical or high advisories.');
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}