import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const chartDir = path.join(repositoryRoot, 'deploy/helm/fullnet');

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

async function exists(relativePath) {
  try {
    await access(path.join(repositoryRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

test('Helm chart files required by Task 12 exist', async () => {
  const required = [
    'deploy/helm/fullnet/Chart.yaml',
    'deploy/helm/fullnet/values.yaml',
    'deploy/helm/fullnet/values.schema.json',
    'deploy/helm/fullnet/templates/_helpers.tpl',
    'deploy/helm/fullnet/templates/api-deployment.yaml',
    'deploy/helm/fullnet/templates/api-service.yaml',
    'deploy/helm/fullnet/templates/api-ingress.yaml',
    'deploy/helm/fullnet/templates/api-hpa.yaml',
    'deploy/helm/fullnet/templates/api-pdb.yaml',
    'deploy/helm/fullnet/templates/worker-deployment.yaml',
    'deploy/helm/fullnet/templates/worker-hpa.yaml',
    'deploy/helm/fullnet/templates/worker-pdb.yaml',
    'deploy/helm/fullnet/templates/migrator-job.yaml',
    'deploy/helm/fullnet/templates/data-protection-pvc.yaml',
    'deploy/helm/fullnet/templates/codegeneration-workspace-pvc.yaml',
    'deploy/helm/fullnet/templates/configmap.yaml',
    'deploy/helm/fullnet/templates/serviceaccount.yaml',
    'deploy/helm/fullnet/templates/networkpolicy.yaml',
    'deploy/helm/fullnet/templates/NOTES.txt',
  ];
  for (const file of required) {
    assert.equal(await exists(file), true, `${file} must exist`);
  }
});

test('values encode production replica, HPA, MaxConcurrency and budget keys', async () => {
  const values = await read('deploy/helm/fullnet/values.yaml');
  assert.match(values, /replicaCount:\s*3/);
  assert.match(values, /minReplicas:\s*3/);
  assert.match(values, /maxReplicas:\s*12/);
  assert.match(values, /replicaCount:\s*2/);
  assert.match(values, /minReplicas:\s*2/);
  assert.match(values, /maxReplicas:\s*8/);
  assert.match(values, /maxConcurrency:\s*1/);
  assert.match(values, /messaging:/);
  assert.match(values, /mode:\s*LegacyPolling/);
  assert.match(values, /databaseConnectionBudget:/);
  assert.match(values, /apiMaxPoolSize:/);
  assert.match(values, /workerMaxPoolSize:/);
  assert.match(values, /migrationReserve:/);
  assert.match(values, /edgeProtection:/);
  assert.match(values, /codeGeneration:/);
  assert.match(values, /enabledWhenProduction:/);
  assert.doesNotMatch(values, /\bredis:\s*$/m);
  assert.doesNotMatch(values, /bitnami/i);
});

test('Chart.yaml declares no DB/Redis/S3/observability dependencies', async () => {
  const chart = await read('deploy/helm/fullnet/Chart.yaml');
  assert.doesNotMatch(chart, /dependencies:/);
  assert.match(chart, /does NOT install|不安装/i);
});

test('API deployment uses zero-downtime rolling and hardened security context', async () => {
  const deployment = await read(
    'deploy/helm/fullnet/templates/api-deployment.yaml'
  );
  assert.match(deployment, /maxUnavailable:\s*0/);
  assert.match(deployment, /maxSurge:\s*1/);
  assert.match(deployment, /readOnlyRootFilesystem:\s*true|containerSecurityContext/);
  assert.match(deployment, /\/health\/startup/);
  assert.match(deployment, /\/health\/ready/);
  assert.match(deployment, /\/health\/live/);
  assert.match(deployment, /preStop/);
  assert.match(deployment, /secretKeyRef/);
  assert.doesNotMatch(deployment, /X-Forwarded-For/);
});

test('Migrator is a Helm hook Job', async () => {
  const job = await read('deploy/helm/fullnet/templates/migrator-job.yaml');
  assert.match(job, /kind:\s*Job/);
  assert.match(job, /helm\.sh\/hook/);
  assert.match(job, /hook-weight/);
});

test('Ingress defaults to cookie affinity and never trusts raw client XFF alone', async () => {
  const ingress = await read('deploy/helm/fullnet/templates/api-ingress.yaml');
  assert.match(ingress, /session-cookie-name/);
  assert.match(ingress, /use-forwarded-headers/);
  assert.match(ingress, /禁止信任任意客户端 X-Forwarded-For/);
});

test('helm contract orchestration passes lint, renders, and counterexamples', () => {
  const script = path.join(
    repositoryRoot,
    'scripts/testing/run-helm-contracts.mjs'
  );
  const result = spawnSync(process.execPath, [script], {
    encoding: 'utf8',
    cwd: repositoryRoot,
    env: process.env,
    shell: false,
  });
  assert.equal(
    result.status,
    0,
    `run-helm-contracts failed:\n${result.stdout}\n${result.stderr}`
  );
  assert.match(result.stdout, /Helm contract orchestration passed/);
});

test('rendered API manifest keeps Capacity-not-verified marker', () => {
  const rendered = (() => {
    if (process.platform === 'win32') {
      const quoted = [
        'helm',
        'template',
        'fullnet-api-check',
        chartDir,
        '-f',
        path.join(chartDir, 'ci/values-role-api.yaml'),
        '-f',
        path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      ]
        .map((part) => (/\s/.test(part) ? `"${part}"` : part))
        .join(' ');
      return spawnSync(quoted, {
        encoding: 'utf8',
        shell: true,
        cwd: repositoryRoot,
      });
    }
    return spawnSync(
      'helm',
      [
        'template',
        'fullnet-api-check',
        chartDir,
        '-f',
        path.join(chartDir, 'ci/values-role-api.yaml'),
        '-f',
        path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      ],
      { encoding: 'utf8', cwd: repositoryRoot }
    );
  })();
  assert.equal(rendered.status, 0, rendered.stderr);
  assert.match(rendered.stdout, /Capacity-not-verified/);
  assert.match(rendered.stdout, /kind:\s*Deployment/);
  assert.match(rendered.stdout, /component:\s*api/);
  assert.doesNotMatch(rendered.stdout, /kind:\s*StatefulSet/);
});
