import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

assert.equal(
  existsSync('security/client-audit-policy.json'),
  true,
  'client audit policy must be versioned'
);
assert.equal(
  existsSync('scripts/audit-client-dependencies.mjs'),
  true,
  'client audit runner must be versioned'
);

const policy = JSON.parse(await readFile('security/client-audit-policy.json', 'utf8'));
const auditModule = await import('../scripts/audit-client-dependencies.mjs');
assert.equal(
  typeof auditModule.evaluateAuditReport,
  'function',
  'client audit runner must expose its pure policy evaluator'
);

const evaluateAuditReport = auditModule.evaluateAuditReport;
const collectAuditReport = auditModule.collectAuditReport;

function createAdvisory({
  ghsa = 'GHSA-fx2h-pf6j-xcff',
  packageName = 'vite',
  severity = 'high',
  paths = ['clients__uniapp>vite']
} = {}) {
  return {
    github_advisory_id: ghsa,
    module_name: packageName,
    severity,
    findings: [{ version: '5.4.21', paths }]
  };
}

function createReport(advisories) {
  return {
    advisories: Object.fromEntries(advisories.map((advisory, index) => [String(index + 1), advisory])),
    metadata: {
      vulnerabilities: { info: 0, low: 0, moderate: 0, high: advisories.length, critical: 0 }
    }
  };
}

if (typeof evaluateAuditReport === 'function') {
  test('policy records the exact reviewed exception and its expiry controls', () => {
    assert.equal(policy.version, 1);
    assert.equal(policy.registry, 'https://registry.npmjs.org');
    assert.equal(policy.minimumSeverity, 'high');
    assert.deepEqual(
      policy.exceptions.map(exception => exception.advisory),
      ['GHSA-fx2h-pf6j-xcff', 'GHSA-mh99-v99m-4gvg']
    );
    assert.equal(policy.exceptions[0].package, 'vite');
    assert.equal(policy.exceptions[0].upstreamPeerEvidence.vite, '5.2.8');
    assert.equal(policy.exceptions[0].reviewBy, '2026-07-18');
    assert.equal(policy.exceptions[0].expiresOn, '2026-10-18');
    assert.equal(policy.exceptions[0].owner, 'client-platform');
    assert.equal(policy.exceptions[0].mitigations.length, 3);
    assert.equal(policy.exceptions[1].package, 'brace-expansion');
    assert.deepEqual(
      policy.exceptions[1].allowedPaths,
      ['clients__uniapp>vue-tsc>@vue/language-core>minimatch>brace-expansion']
    );
    assert.equal(policy.exceptions[1].reviewBy, '2026-07-18');
    assert.equal(policy.exceptions[1].expiresOn, '2026-09-26');
    assert.equal(policy.exceptions[1].owner, 'client-platform');
    assert.equal(policy.exceptions[1].mitigations.length, 3);
  });

  test('accepts only the reviewed Vite advisory on exact uni-app toolchain paths', () => {
    const result = evaluateAuditReport(createReport([createAdvisory({
      paths: policy.exceptions[0].allowedPaths
    })]), policy, new Date('2026-07-18T00:00:00Z'));

    assert.deepEqual(result.acceptedExceptions, [{
      advisory: 'GHSA-fx2h-pf6j-xcff',
      package: 'vite',
      paths: policy.exceptions[0].allowedPaths
    }]);
  });

  test('rejects every critical advisory even when its identifier is configured', () => {
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory({ severity: 'critical' })]), policy),
      /Critical advisory is never allowed/u
    );
  });

  test('rejects unreviewed high advisories and package mismatches', () => {
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory({ ghsa: 'GHSA-unreviewed' })]), policy),
      /Unreviewed high advisory/u
    );
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory({ packageName: 'other-package' })]), policy),
      /does not match the reviewed package/u
    );
  });

  test('rejects advisory paths outside the exact uni-app toolchain boundary', () => {
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory({ paths: ['ui__admin>vite'] })]), policy),
      /outside the reviewed uni-app toolchain/u
    );
  });

  test('rejects expired exceptions automatically', () => {
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory()]), policy, new Date('2026-10-19T00:00:00Z')),
      /expired on 2026-10-18/u
    );
  });

  test('stops when the audit JSON does not expose reliable finding paths', () => {
    const advisory = createAdvisory();
    advisory.findings = [{ version: '5.4.21' }];
    assert.throws(
      () => evaluateAuditReport(createReport([advisory]), policy),
      /does not expose non-empty findings paths/u
    );
  });

  test('retries two npm audit transport timeouts and still fails closed after the limit', async () => {
    const timeoutResult = {
      status: 1,
      stdout: JSON.stringify({
        error: {
          code: 'ERR_SOCKET_TIMEOUT',
          message: 'registry socket timeout'
        }
      }),
      stderr: ''
    };
    const validResult = {
      status: 0,
      stdout: JSON.stringify(createReport([])),
      stderr: ''
    };
    let attempts = 0;
    const report = await collectAuditReport(
      () => (++attempts < 3 ? timeoutResult : validResult),
      { waitBeforeRetry: async () => {} }
    );
    assert.equal(attempts, 3);
    assert.deepEqual(report, createReport([]));

    attempts = 0;
    await assert.rejects(
      collectAuditReport(
        () => {
          attempts += 1;
          return timeoutResult;
        },
        { waitBeforeRetry: async () => {} }
      ),
      /transport failed after 3 attempts/u
    );
    assert.equal(attempts, 3);
  });
}
