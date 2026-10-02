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
  assert.match(values, /consumerBufferHighWatermark:\s*256/);
  assert.match(values, /partitionKeyConcurrencySlots:\s*1/);
  assert.match(values, /offsetCommitMode:\s*PerMessage/);
  assert.match(values, /maximumSynchronousMessages:\s*1000/);
  assert.match(values, /executionTimeoutSeconds:\s*45/);
  assert.match(values, /databaseConnectionBudget:/);
  assert.match(values, /apiMaxPoolSize:/);
  assert.match(values, /workerMaxPoolSize:/);
  assert.match(values, /migrationReserve:/);
  assert.match(values, /healthReserve:\s*2/);
  assert.match(values, /workerCriticalReserve:\s*1/);
  assert.match(values, /apiPermitLimit:\s*38/);
  assert.match(values, /apiQueueLimit:\s*0/);
  assert.match(values, /workerPermitLimit:\s*7/);
  assert.match(values, /workerQueueLimit:\s*1/);
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

test('config map wires static database capacity budget and role-specific admission', async () => {
  const configMap = await read('deploy/helm/fullnet/templates/configmap.yaml');
  for (const key of [
    'DatabaseCapacity__Enabled',
    'DatabaseCapacity__HostRole',
    'DatabaseCapacity__PermitLimit',
    'DatabaseCapacity__QueueLimit',
    'DatabaseCapacity__AcquireTimeoutMilliseconds',
    'DatabaseCapacity__ExpectedMaxPoolSize',
    'DatabaseCapacity__HealthReserve',
    'DatabaseCapacity__CriticalWorkerReserve',
    'DatabaseCapacity__ApiMaxReplicas',
    'DatabaseCapacity__ApiMaxPoolSize',
    'DatabaseCapacity__WorkerMaxReplicas',
    'DatabaseCapacity__WorkerMaxPoolSize',
    'DatabaseCapacity__MigrationReserve',
    'DatabaseCapacity__TotalBudget',
  ]) {
    assert.match(configMap, new RegExp(key));
  }
  assert.match(configMap, /\.Values\.roles\.api/);
  assert.match(configMap, /\.Values\.roles\.worker/);
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
  assert.match(rendered.stdout, /DatabaseCapacity__Enabled:\s*"true"/);
  assert.match(rendered.stdout, /DatabaseCapacity__HostRole:\s*"Api"/);
  assert.match(rendered.stdout, /DatabaseCapacity__PermitLimit:\s*"38"/);
  assert.match(rendered.stdout, /DatabaseCapacity__QueueLimit:\s*"0"/);
  assert.match(rendered.stdout, /fullnet\.io\/log-ingress:\s*legacy/);
  assert.match(rendered.stdout, /DOTNET_ENVIRONMENT:\s*"Production"/);
  assert.match(rendered.stdout, /ASPNETCORE_ENVIRONMENT:\s*"Production"/);
  assert.doesNotMatch(rendered.stdout, /- name: FullNet__Logging__DeliveryMode/);
  assert.doesNotMatch(rendered.stdout, /- name: FullNet__Logging__ExpectedDeliveryMode/);
  assert.match(rendered.stdout, /kind:\s*Deployment/);
  assert.match(rendered.stdout, /component:\s*api/);
  assert.doesNotMatch(rendered.stdout, /kind:\s*StatefulSet/);
});

test('Collector ingress renders matching Pod route and application expectation', () => {
  for (const role of ['api', 'worker']) {
    const args = [
      'template',
      `fullnet-log-${role}-collector-check`,
      chartDir,
      '-f', path.join(chartDir, `ci/values-role-${role}.yaml`),
      '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      '--set', 'logging.ingress=Collector',
      '--set', 'production=false',
      '--set', 'dotnetEnvironment=Staging',
    ];
    const rendered = process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
        encoding: 'utf8', shell: true, cwd: repositoryRoot,
      })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
    assert.equal(rendered.status, 0, rendered.stderr);
    assert.match(rendered.stdout, /fullnet\.io\/log-ingress:\s*collector/);
    assert.match(rendered.stdout, /- name: FullNet__Logging__DeliveryMode\s+value: "Collector"/);
    assert.match(rendered.stdout, /- name: FullNet__Logging__ExpectedDeliveryMode\s+value: "Collector"/);
    assert.match(rendered.stdout, /DOTNET_ENVIRONMENT:\s*"Staging"/);
    assert.match(rendered.stdout, /ASPNETCORE_ENVIRONMENT:\s*"Staging"/);
  }
});

test('logging index route metadata is opt-in and requires a paired retention policy', () => {
  const base = [
    'template', 'fullnet-log-index-route-check', chartDir,
    '-f', path.join(chartDir, 'ci/values-role-api.yaml'),
    '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
    '--set', 'production=false',
    '--set', 'dotnetEnvironment=Staging',
    '--set', 'logging.ingress=Collector',
  ];
  const render = (extra) => {
    const args = [...base, ...extra];
    return process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
          encoding: 'utf8', shell: true, cwd: repositoryRoot,
        })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
  };

  const disabled = render([]);
  assert.equal(disabled.status, 0, disabled.stderr);
  assert.doesNotMatch(disabled.stdout, /FullNet__Logging__IndexRouteVersion/);

  const enabled = render(['--set', 'logging.indexRouteVersion=2', '--set', 'logging.indexRetentionDays=30']);
  assert.equal(enabled.status, 0, enabled.stderr);
  assert.match(enabled.stdout, /- name: FullNet__Logging__IndexRouteVersion\s+value: "2"/);
  assert.match(enabled.stdout, /- name: FullNet__Logging__IndexRetentionDays\s+value: "30"/);

  const unpaired = render(['--set', 'logging.indexRouteVersion=2']);
  assert.notEqual(unpaired.status, 0);
  assert.match(unpaired.stderr, /must be configured together/);
});

test('production Collector renders matching route and keeps capacity unverified', () => {
  for (const role of ['api', 'worker']) {
    const args = [
      'template', `fullnet-log-collector-${role}-production-check`, chartDir,
      '-f', path.join(chartDir, `ci/values-role-${role}.yaml`),
      '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      '--set', 'logging.ingress=Collector',
    ];
    const rendered = process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
        encoding: 'utf8', shell: true, cwd: repositoryRoot,
      })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
    assert.equal(rendered.status, 0, rendered.stderr);
    assert.match(rendered.stdout, /fullnet\.io\/log-ingress: collector/);
    assert.match(rendered.stdout, /- name: FullNet__Logging__DeliveryMode\s+value: "Collector"/);
    assert.match(rendered.stdout, /- name: FullNet__Logging__ExpectedDeliveryMode\s+value: "Collector"/);
    assert.match(rendered.stdout, /DOTNET_ENVIRONMENT: "Production"/);
    assert.match(rendered.stdout, /Capacity-not-verified/);
    assert.doesNotMatch(rendered.stdout, /FullNet__Logging__Kafka__BootstrapServers/);
  }
});

test('non-production Local ingress renders matching Pod route and application expectation', () => {
  for (const role of ['api', 'worker']) {
    const args = [
      'template', `fullnet-log-${role}-local-check`, chartDir,
      '-f', path.join(chartDir, `ci/values-role-${role}.yaml`),
      '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      '--set', 'logging.ingress=Local',
      '--set', 'logging.kafka.caSecretName=log-broker-ca',
      '--set', 'production=false',
      '--set', 'dotnetEnvironment=Staging',
    ];
    const rendered = process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
        encoding: 'utf8', shell: true, cwd: repositoryRoot,
      })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
    assert.equal(rendered.status, 0, rendered.stderr);
    assert.match(rendered.stdout, /fullnet\.io\/log-ingress:\s*local/);
    assert.match(rendered.stdout, /- name: FullNet__Logging__DeliveryMode\s+value: "Local"/);
    assert.match(rendered.stdout, /- name: FullNet__Logging__ExpectedDeliveryMode\s+value: "Local"/);
    assert.match(rendered.stdout, /DOTNET_ENVIRONMENT:\s*"Staging"/);
    assert.match(rendered.stdout, /ASPNETCORE_ENVIRONMENT:\s*"Staging"/);
    assert.doesNotMatch(rendered.stdout, /FullNet__Logging__Kafka__/);
    assert.doesNotMatch(rendered.stdout, /kafka-log-ca|log-broker-ca/);
  }
});

test('explicit logging ingress requires a non-production .NET environment in preview', () => {
  for (const ingress of ['Local', 'Collector']) {
    const args = [
      'template', `fullnet-log-${ingress.toLowerCase()}-environment-check`, chartDir,
      '-f', path.join(chartDir, 'ci/values-role-api.yaml'),
      '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      '--set', `logging.ingress=${ingress}`,
      '--set', 'production=false',
    ];
    const rendered = process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
        encoding: 'utf8', shell: true, cwd: repositoryRoot,
      })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
    assert.notEqual(rendered.status, 0);
    assert.match(rendered.stderr, /dotnetEnvironment must be Staging or Development for non-production logging ingress/);
  }
});

test('production chart cannot run with a non-production .NET environment', () => {
  for (const environment of ['Staging', 'Development']) {
    const args = [
      'template', `fullnet-${environment.toLowerCase()}-production-check`, chartDir,
      '-f', path.join(chartDir, 'ci/values-role-api.yaml'),
      '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      '--set', `dotnetEnvironment=${environment}`,
    ];
    const rendered = process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
        encoding: 'utf8', shell: true, cwd: repositoryRoot,
      })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
    assert.notEqual(rendered.status, 0);
    assert.match(rendered.stderr, /dotnetEnvironment must be Production when production=true/);
  }
});

test('ApplicationKafka requires dedicated Secrets and frozen routes in Staging and Production', () => {
  for (const environment of ['Staging', 'Production']) {
    for (const role of ['api', 'worker']) {
      const args = [
        'template', `fullnet-log-${role}-kafka-check`, chartDir,
        '-f', path.join(chartDir, `ci/values-role-${role}.yaml`),
        '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
        '--set', `production=${environment === 'Production'}`,
        '--set', `dotnetEnvironment=${environment}`,
        '--set', 'logging.ingress=ApplicationKafka',
      ];
      const render = (extra = []) => process.platform === 'win32'
        ? spawnSync(['helm', ...args, ...extra].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
          encoding: 'utf8', shell: true, cwd: repositoryRoot,
        })
        : spawnSync('helm', [...args, ...extra], { encoding: 'utf8', cwd: repositoryRoot });
      const missing = render();
      assert.notEqual(missing.status, 0);
      assert.match(missing.stderr, /logging\.kafka\.configurationSecretName/);

      const missingRoute = render(['--set', 'logging.kafka.configurationSecretName=log-producer-config']);
      assert.notEqual(missingRoute.status, 0);
      assert.match(missingRoute.stderr, /requires logging.indexRouteVersion/);

      const rendered = render([
        '--set', 'logging.kafka.configurationSecretName=log-producer-config',
        '--set', 'logging.indexRouteVersion=2',
        '--set', 'logging.indexRetentionDays=30',
      ]);
      assert.equal(rendered.status, 0, rendered.stderr);
      assert.match(rendered.stdout, /fullnet\.io\/log-ingress:\s*applicationkafka/);
      assert.match(rendered.stdout, new RegExp(`DOTNET_ENVIRONMENT:\\s*"${environment}"`));
      assert.match(rendered.stdout, /Capacity-not-verified/);
      assert.match(rendered.stdout, /- name: FullNet__Logging__DeliveryMode\s+value: "ApplicationKafka"/);
      const configMapDocument = rendered.stdout.split(/^---\s*$/m)
        .find((document) => /kind: ConfigMap/.test(document));
      const deploymentDocument = rendered.stdout.split(/^---\s*$/m)
        .find((document) => /kind: Deployment/.test(document));
      assert.ok(configMapDocument);
      assert.ok(deploymentDocument);
      assert.doesNotMatch(configMapDocument, /FullNet__Logging__(?:Expected)?DeliveryMode/);
      assert.match(deploymentDocument, /- name: FullNet__Logging__DeliveryMode\s+value: "ApplicationKafka"/);
      assert.match(deploymentDocument, /- name: FullNet__Logging__ExpectedDeliveryMode\s+value: "ApplicationKafka"/);
      assert.match(rendered.stdout, /- name: FullNet__Logging__Kafka__BootstrapServers\s+valueFrom:\s+secretKeyRef:\s+name: "log-producer-config"\s+key: bootstrapServers/);
      assert.match(rendered.stdout, /- name: FullNet__Logging__Kafka__GeneralTopic\s+valueFrom:\s+secretKeyRef:\s+name: "log-producer-config"\s+key: generalTopic/);
      assert.doesNotMatch(rendered.stdout, /- secretRef:\s*\n\s*name: "log-producer-config"/);
      assert.doesNotMatch(deploymentDocument, /FullNet__Logging__Kafka__SslCaLocation|kafka-log-ca/);

      const withPrivateCa = render([
        '--set', 'logging.kafka.configurationSecretName=log-producer-config',
        '--set', 'logging.kafka.caSecretName=log-broker-ca',
        '--set', 'logging.indexRouteVersion=2',
        '--set', 'logging.indexRetentionDays=30',
      ]);
      assert.equal(withPrivateCa.status, 0, withPrivateCa.stderr);
      assert.match(withPrivateCa.stdout, /- name: FullNet__Logging__Kafka__SslCaLocation\s+value: "\/var\/run\/fullnet\/logging\/kafka-ca\/ca\.crt"/);
      assert.match(withPrivateCa.stdout, /- name: kafka-log-ca\s+mountPath: \/var\/run\/fullnet\/logging\/kafka-ca\s+readOnly: true/);
      assert.match(withPrivateCa.stdout, /- name: kafka-log-ca\s+secret:\s+secretName: "log-broker-ca"\s+items:\s+- key: ca\.crt\s+path: ca\.crt/);
    }
  }
});

test('production Local ingress remains rejected by Helm', () => {
  for (const ingress of ['Local']) {
    const args = [
      'template', `fullnet-log-${ingress.toLowerCase()}-check`, chartDir,
      '-f', path.join(chartDir, 'ci/values-role-api.yaml'),
      '-f', path.join(chartDir, 'ci/values-provider-sqlserver.yaml'),
      '--set', `logging.ingress=${ingress}`,
    ];
    const rendered = process.platform === 'win32'
      ? spawnSync(['helm', ...args].map((part) => (/\s/.test(part) ? `"${part}"` : part)).join(' '), {
        encoding: 'utf8', shell: true, cwd: repositoryRoot,
      })
      : spawnSync('helm', args, { encoding: 'utf8', cwd: repositoryRoot });
    assert.notEqual(rendered.status, 0);
    assert.match(rendered.stderr, /logging\.ingress=Local is disabled in production until collector exclusion is verified/);
  }
});
