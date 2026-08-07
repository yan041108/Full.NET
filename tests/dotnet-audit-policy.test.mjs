import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

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