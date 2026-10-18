import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import path from 'node:path';

assert.equal(
  existsSync('security/dotnet-audit-policy.json'),
  true,
  'dotnet audit policy must be versioned'
);
assert.equal(
  existsSync('scripts/audit-dotnet-dependencies.mjs'),
  true,
  'dotnet audit runner must be versioned'
);

const policy = JSON.parse(await readFile('security/dotnet-audit-policy.json', 'utf8'));
const auditModule = await import('../scripts/audit-dotnet-dependencies.mjs');

for (const exportName of [
  'extractAdvisoryId',
  'extractVulnerabilityFindings',
  'evaluateDotnetVulnerabilityFindings',
  'evaluateDotnetListReport',
  'toProjectSlug'
]) {
  assert.equal(
    typeof auditModule[exportName],
    'function',
    `dotnet audit runner must export ${exportName}`
  );
}

const {
  extractAdvisoryId,
  extractVulnerabilityFindings,
  evaluateDotnetVulnerabilityFindings,
  evaluateDotnetListReport,
  toProjectSlug
} = auditModule;

const reviewedException = {
  advisory: 'GHSA-sample-high',
  package: 'Sample.Package',
  allowedPaths: ['src__Sample__Sample>Sample.Package'],
  rationale: 'Pinned by upstream SDK until the next platform upgrade window.',
  mitigations: ['Package is only used in isolated integration tests.'],
  owner: 'platform-security',
  reviewBy: '2026-08-08',
  expiresOn: '2026-12-31'
};

const policyWithException = {
  schemaVersion: 1,
  minimumSeverity: 'high',
  exceptions: [reviewedException]
};

function createVulnerablePackage({
  id = 'Sample.Package',
  severity = 'High',
  advisoryUrl = 'https://github.com/advisories/GHSA-sample-high',
  bucket = 'topLevelPackages'
} = {}) {
  return {
    id,
    requestedVersion: '1.0.0',
    resolvedVersion: '1.0.0',
    vulnerabilities: [{ severity, advisoryurl: advisoryUrl }]
  };
}

function createReport({
  projectPath = 'G:/repo/src/Sample/Sample.csproj',
  framework = 'net10.0',
  bucket = 'topLevelPackages',
  pkg = createVulnerablePackage()
} = {}) {
  return {
    version: 1,
    parameters: '--vulnerable --include-transitive',
    sources: ['https://api.nuget.org/v3/index.json'],
    projects: [{
      path: projectPath,
      frameworks: [{
        framework,
        [bucket]: [pkg]
      }]
    }]
  };
}

test('policy records schema version and minimum severity', () => {
  assert.equal(policy.schemaVersion, 1);
  assert.equal(policy.minimumSeverity, 'high');
  assert.deepEqual(policy.exceptions, []);
});

test('extractAdvisoryId accepts GHSA and CVE URLs', () => {
  assert.equal(
    extractAdvisoryId('https://github.com/advisories/GHSA-abc1-def2-ghi3'),
    'GHSA-abc1-def2-ghi3'
  );
  assert.equal(
    extractAdvisoryId('https://nvd.nist.gov/vuln/detail/CVE-2024-12345'),
    'CVE-2024-12345'
  );
});

test('extractVulnerabilityFindings returns zero findings for a clean report', () => {
  const findings = extractVulnerabilityFindings({
    version: 1,
    parameters: '--vulnerable --include-transitive',
    sources: ['https://api.nuget.org/v3/index.json'],
    projects: [{
      path: 'G:/repo/src/Sample/Sample.csproj',
      frameworks: [{
        framework: 'net10.0',
        topLevelPackages: [{
          id: 'Clean.Package',
          resolvedVersion: '1.0.0'
        }]
      }]
    }]
  }, 'G:/repo');

  assert.deepEqual(findings, []);
});

test('evaluateDotnetListReport accepts a clean solution report', () => {
  const result = evaluateDotnetListReport({
    version: 1,
    parameters: '--vulnerable --include-transitive',
    sources: ['https://api.nuget.org/v3/index.json'],
    projects: [{
      path: 'G:/repo/src/Sample/Sample.csproj'
    }]
  }, policyWithException, new Date('2026-08-08T00:00:00Z'), 'G:/repo');

  assert.deepEqual(result.acceptedExceptions, []);
});

test('rejects every critical advisory even when its identifier is configured', () => {
  assert.throws(
    () => evaluateDotnetVulnerabilityFindings([{
      advisory: 'GHSA-critical',
      package: 'Sample.Package',
      severity: 'critical',
      paths: reviewedException.allowedPaths
    }], policyWithException),
    /Critical advisory is never allowed/u
  );
});

test('rejects unreviewed high advisories and package mismatches', () => {
  assert.throws(
    () => evaluateDotnetVulnerabilityFindings([{
      advisory: 'GHSA-unreviewed',
      package: 'Sample.Package',
      severity: 'high',
      paths: reviewedException.allowedPaths
    }], policyWithException),
    /Unreviewed high advisory/u
  );
  assert.throws(
    () => evaluateDotnetVulnerabilityFindings([{
      advisory: reviewedException.advisory,
      package: 'Other.Package',
      severity: 'high',
      paths: reviewedException.allowedPaths
    }], policyWithException),
    /does not match the reviewed package/u
  );
});

test('accepts only the reviewed advisory on exact dependency paths', () => {
  const report = createReport();
  const findings = extractVulnerabilityFindings(report, 'G:/repo');
  const result = evaluateDotnetVulnerabilityFindings(findings, policyWithException, new Date('2026-08-08T00:00:00Z'));

  assert.deepEqual(result.acceptedExceptions, [{
    advisory: reviewedException.advisory,
    package: reviewedException.package,
    paths: reviewedException.allowedPaths
  }]);
});

test('rejects advisory paths outside the reviewed dependency boundary', () => {
  const findings = extractVulnerabilityFindings(createReport({
    projectPath: 'G:/repo/tests/Other/Other.csproj'
  }), 'G:/repo');

  assert.throws(
    () => evaluateDotnetVulnerabilityFindings(findings, policyWithException),
    /outside the reviewed dependency paths/u
  );
});

test('rejects expired exceptions automatically', () => {
  const findings = extractVulnerabilityFindings(createReport(), 'G:/repo');
  assert.throws(
    () => evaluateDotnetVulnerabilityFindings(findings, policyWithException, new Date('2027-01-01T00:00:00Z')),
    /expired on 2026-12-31/u
  );
});

test('stops when dotnet audit JSON is malformed or incomplete', () => {
  assert.throws(
    () => extractVulnerabilityFindings({ version: 1, projects: [] }),
    /does not expose vulnerability sources/u
  );
  assert.throws(
    () => extractVulnerabilityFindings({
      version: 1,
      sources: ['https://api.nuget.org/v3/index.json'],
      projects: [{
        path: 'G:/repo/src/Sample/Sample.csproj',
        frameworks: [{
          framework: 'net10.0',
          topLevelPackages: [{
            id: 'Sample.Package',
            vulnerabilities: [{ severity: 'High' }]
          }]
        }]
      }]
    }, 'G:/repo'),
    /invalid vulnerability entry/u
  );
  assert.throws(
    () => evaluateDotnetListReport({ version: 2, sources: [], projects: [] }, policyWithException),
    /missing the expected version\/projects envelope/u
  );
});

test('toProjectSlug normalizes repository-relative project paths', () => {
  assert.equal(
    toProjectSlug('G:/repo/src/Sample/Sample.csproj', 'G:/repo'),
    'src__Sample__Sample'
  );
});

// 在命令边界模拟真实 JSON，不允许干净主解决方案掩盖独立实验的漏洞或缺失结果。
function runWithProbeReport(probeReport) {
  return auditModule.runDotnetListAudit((_command, args) => {
    if (args[0] === 'restore') return { status: 0, stdout: '', stderr: '' };
    const project = args[1].endsWith('.csproj') ? args[1] : path.resolve('src/Sample/Sample.csproj');
    const report = args[1].endsWith('.csproj') ? probeReport(project)
      : { version: 1, sources: ['https://api.nuget.org/v3/index.json'], projects: [{ path: project, frameworks: [] }] };
    return { status: 0, stdout: JSON.stringify(report), stderr: '' };
  });
}

test('干净主解决方案不能掩盖独立实验中的高危传递依赖', () => {
  assert.throws(() => evaluateDotnetListReport(runWithProbeReport(project => createReport({
    projectPath: project, bucket: 'transitivePackages', pkg: createVulnerablePackage({ id: 'System.Security.Cryptography.Pkcs' })
  })), policy), /Unreviewed high advisory/u);
});

test('独立实验缺少漏洞源时不得与主解决方案合并为干净报告', () => {
  assert.throws(() => runWithProbeReport(project => ({ version: 1, projects: [{ path: project, frameworks: [] }] })), /vulnerability sources/u);
});

test('独立实验零项目报告不能作为审计通过', () => {
  assert.throws(() => runWithProbeReport(() => ({ version: 1, sources: ['https://api.nuget.org/v3/index.json'], projects: [] })), /no project/u);
});

test('独立实验报告必须包含实际请求的项目', () => {
  assert.throws(() => runWithProbeReport(() => ({ version: 1, sources: ['https://api.nuget.org/v3/index.json'], projects: [{ path: path.resolve('src/Other/Other.csproj'), frameworks: [] }] })), /requested project/u);
});

test('还原失败保留标准输出中的 NuGet 根因和标准错误', () => {
  assert.throws(() => auditModule.runDotnetListAudit(() => ({ status: 1, stdout: 'NU1109 dependency downgrade', stderr: 'restore stderr' })), error => {
    assert.match(error.message, /NU1109 dependency downgrade/u);
    assert.match(error.message, /restore stderr/u);
    return true;
  });
});
