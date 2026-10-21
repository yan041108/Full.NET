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

const productionPolicy = JSON.parse(await readFile('security/client-audit-policy.json', 'utf8'));
// 历史例外只作为拒绝边界的测试夹具，不回写生产策略。
const policy = { ...productionPolicy, exceptions: [
  {
    "advisory": "GHSA-fx2h-pf6j-xcff",
    "package": "vite",
    "allowedPaths": [
      "clients__uniapp>vite",
      "clients__uniapp>@dcloudio/uni-h5>@dcloudio/uni-h5-vite>@vitejs/plugin-basic-ssl>vite"
    ],
    "rationale": "DCloud 3.0.0-5010520260709002 declares an exact Vite 5.2.8 peer while the patched advisory requires Vite 6.4.3 or later; Full.NET has verified Vite 5.4.21 without claiming upstream support.",
    "upstreamPeerEvidence": {
      "package": "@dcloudio/vite-plugin-uni",
      "version": "3.0.0-5010520260709002",
      "vite": "5.2.8"
    },
    "mitigations": [
      "The uni-app development server binds only to 127.0.0.1.",
      "Client directories must not contain secrets, .env files, certificates, or private keys.",
      "Production artifacts do not include the Vite development server or its Node runtime package; only generated build helper identifiers may remain."
    ],
    "owner": "client-platform",
    "reviewBy": "2026-07-18",
    "expiresOn": "2026-10-18"
  },
  {
    "advisory": "GHSA-mh99-v99m-4gvg",
    "package": "brace-expansion",
    "allowedPaths": [
      "clients__uniapp>vue-tsc>@vue/language-core>minimatch>brace-expansion"
    ],
    "rationale": "The patched brace-expansion 5.x API is incompatible with minimatch 9.x, which declares brace-expansion ^2.0.1. The vulnerable package is confined to the pinned vue-tsc development toolchain, so a cross-major override would break fresh installs.",
    "upstreamPeerEvidence": {
      "package": "@vue/language-core",
      "version": "2.2.12",
      "minimatch": "9.0.9"
    },
    "mitigations": [
      "The dependency executes only during repository-controlled type checking and is not shipped in client production artifacts.",
      "Type-check inputs and glob patterns come from version-controlled workspace configuration rather than remote user input.",
      "The exact dependency path is pinned and must be re-reviewed when vue-tsc or @vue/language-core is upgraded."
    ],
    "owner": "client-platform",
    "reviewBy": "2026-07-18",
    "expiresOn": "2026-09-26"
  },
  {
    "advisory": "GHSA-vfj7-8cjw-p6xm",
    "package": "braces",
    "allowedPaths": [
      "clients__uniapp>@dcloudio/uni-cli-shared>chokidar>braces"
    ],
    "rationale": "The reviewed upstream advisory has no patched release as of 2026-10-03. This dependency is confined to the pinned uni-app development build watcher; repository-controlled build paths are the only glob inputs in the accepted path.",
    "mitigations": [
      "Do not pass user-supplied glob patterns or remote filenames into the uni-app build watcher.",
      "The dependency is used during local and CI builds and is not included in deployed client artifacts.",
      "Recheck the upstream advisory and remove this exception once a patched braces release is available."
    ],
    "owner": "client-platform",
    "reviewBy": "2026-10-10",
    "expiresOn": "2026-10-18"
  }
] };
const auditModule = await import('../scripts/audit-client-dependencies.mjs');
assert.equal(
  typeof auditModule.evaluateAuditReport,
  'function',
  'client audit runner must expose its pure policy evaluator'
);

const evaluateAuditReport = auditModule.evaluateAuditReport;
const collectAuditReport = auditModule.collectAuditReport;
// 包名与路径边界使用受审日期，避免真实日期先触发过期拒绝并遮蔽目标断言。
const reviewedViteTime = new Date('2026-07-18T00:00:00Z');

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
      vulnerabilities: advisories.reduce((counts, advisory) => {
        counts[advisory.severity] = (counts[advisory.severity] ?? 0) + advisory.findings.length;
        return counts;
      }, { info: 0, low: 0, moderate: 0, high: 0, critical: 0 })
    }
  };
}

if (typeof evaluateAuditReport === 'function') {
  test('production policy has no vulnerability exceptions and still rejects High/Critical findings', () => {
    assert.equal(productionPolicy.version, 1);
    assert.equal(productionPolicy.registry, 'https://registry.npmjs.org');
    assert.equal(productionPolicy.minimumSeverity, 'low');
    assert.deepEqual(productionPolicy.exceptions, []);
    assert.throws(() => evaluateAuditReport(createReport([createAdvisory()]), productionPolicy), /Unreviewed high advisory/u);
    assert.throws(() => evaluateAuditReport(createReport([createAdvisory({ severity: 'critical' })]), productionPolicy), /Critical advisory is never allowed/u);
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
      () => evaluateAuditReport(createReport([createAdvisory({ ghsa: 'GHSA-unreviewed' })]), policy, reviewedViteTime),
      /Unreviewed high advisory/u
    );
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory({ packageName: 'other-package' })]), policy, reviewedViteTime),
      /does not match the reviewed package/u
    );
  });

  test('rejects advisory paths outside the exact uni-app toolchain boundary', () => {
    assert.throws(
      () => evaluateAuditReport(createReport([createAdvisory({ paths: ['ui__admin>vite'] })]), policy, reviewedViteTime),
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
      () => evaluateAuditReport(createReport([advisory]), policy, reviewedViteTime),
      /does not expose non-empty findings paths/u
    );
  });

  test('accepts the time-limited braces finding only on the reviewed uni-app watcher path', () => {
    const braces = createAdvisory({
      ghsa: 'GHSA-vfj7-8cjw-p6xm',
      packageName: 'braces',
      paths: policy.exceptions[2].allowedPaths
    });
    assert.deepEqual(
      evaluateAuditReport(createReport([braces]), policy, new Date('2026-10-03T00:00:00Z')).acceptedExceptions,
      [{ advisory: braces.github_advisory_id, package: 'braces', paths: policy.exceptions[2].allowedPaths }]
    );
    assert.throws(() => evaluateAuditReport(createReport([createAdvisory({
      ghsa: braces.github_advisory_id,
      packageName: 'braces',
      paths: ['ui__admin>chokidar>braces']
    })]), policy, new Date('2026-10-03T00:00:00Z')), /outside the reviewed uni-app toolchain/u);
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

test('Low policy rejects Low and Moderate findings without exceptions', () => {
  for (const severity of ['low', 'moderate']) {
    assert.throws(() => evaluateAuditReport(createReport([createAdvisory({ severity })]),
      { ...productionPolicy, minimumSeverity: 'low' }), new RegExp(`Unreviewed ${severity} advisory`, 'u'));
  }
});

test('metadata counts every affected version group rather than advisory identifiers', () => {
  const advisory = createAdvisory();
  advisory.findings.push({ version: '5.4.20', paths: ['clients__uniapp>vite'] });
  assert.equal(createReport([advisory]).metadata.vulnerabilities.high, 2);
  assert.equal(evaluateAuditReport(createReport([advisory]), policy, reviewedViteTime).acceptedExceptions.length, 1);
});

test('metadata cannot conceal unreported findings at any severity', () => {
  for (const severity of ['info', 'low', 'moderate', 'high', 'critical']) {
    const report = createReport([]);
    report.metadata.vulnerabilities[severity] = 1;
    assert.throws(() => evaluateAuditReport(report, productionPolicy), /counts do not match metadata/u);
  }
});

test('unknown severity and invalid policy thresholds fail closed', () => {
  assert.throws(() => evaluateAuditReport(createReport([createAdvisory({ severity: 'medium' })]), productionPolicy), /invalid severity/u);
  for (const minimumSeverity of ['medium', undefined]) {
    assert.throws(() => evaluateAuditReport(createReport([]), { ...productionPolicy, minimumSeverity }), /policy is invalid/u);
  }
});

test('unknown metadata severity fails closed even without advisory entries', () => {
  for (const count of [0, 1]) {
    const report = createReport([]);
    report.metadata.vulnerabilities.medium = count;
    assert.throws(() => evaluateAuditReport(report, productionPolicy), /unknown severity metadata/u);
  }
});

test('advisories must be an object map rather than an array', () => {
  assert.throws(() => evaluateAuditReport({ ...createReport([]), advisories: [] }, productionPolicy), /advisories object/u);
});
