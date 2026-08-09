#!/usr/bin/env node
/**
 * Helm 合同编排：lint、双库角色渲染、反例 fail、kubectl dry-run。
 * CI 只验证模板，不连接生产集群。
 */
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const chartDir = path.join(repositoryRoot, 'deploy/helm/fullnet');
const ciDir = path.join(chartDir, 'ci');
const outDir = path.join(repositoryRoot, 'artifacts/helm-contract');

function run(command, args, options = {}) {
  // Windows 上 shell:true + 参数数组会错误拼接，导致 helm 收到空输出。
  if (process.platform === 'win32') {
    const quoted = [command, ...args]
      .map((part) => (/\s/.test(part) ? `"${part}"` : part))
      .join(' ');
    return spawnSync(quoted, {
      encoding: 'utf8',
      shell: true,
      ...options,
    });
  }

  return spawnSync(command, args, {
    encoding: 'utf8',
    shell: false,
    ...options,
  });
}

function mustSucceed(label, result) {
  if (result.status !== 0) {
    process.stderr.write(
      `${label} failed\n${result.stdout ?? ''}\n${result.stderr ?? ''}\n`
    );
    process.exit(result.status ?? 1);
  }
}

function mustFail(label, result, needle) {
  if (result.status === 0) {
    process.stderr.write(`${label} unexpectedly succeeded\n${result.stdout}\n`);
    process.exit(1);
  }
  const text = `${result.stdout ?? ''}\n${result.stderr ?? ''}`;
  if (needle && !text.includes(needle)) {
    process.stderr.write(
      `${label} failed without expected message '${needle}'\n${text}\n`
    );
    process.exit(1);
  }
}

function requireTool(name) {
  const probe = run(name, ['version']);
  if (probe.error && probe.status === null) {
    process.stderr.write(`${name} is required on PATH for test:helm\n`);
    process.exit(1);
  }
}

function validateRenderedManifest(name, content) {
  const docs = content
    .split(/^---\s*$/m)
    .map((part) => part.trim())
    .filter((part) => {
      if (part.length === 0) {
        return false;
      }
      // Helm 会在文档前插入 `# Source:` 注释，不能因此丢弃整段。
      const withoutComments = part
        .split(/\r?\n/)
        .filter((line) => !line.trimStart().startsWith('#'))
        .join('\n')
        .trim();
      return withoutComments.length > 0;
    });
  if (docs.length === 0) {
    process.stderr.write(`${name}: rendered manifest is empty\n`);
    process.exit(1);
  }
  for (const doc of docs) {
    if (!/^\s*apiVersion:\s*\S+/m.test(doc) || !/^\s*kind:\s*\S+/m.test(doc)) {
      process.stderr.write(
        `${name}: each document must declare apiVersion and kind\n${doc.slice(0, 200)}\n`
      );
      process.exit(1);
    }
  }

  const kinds = docs
    .map((doc) => doc.match(/^\s*kind:\s*(\S+)/m)?.[1])
    .filter(Boolean);
  const requiredByName = {
    'api-sqlserver': ['Deployment', 'Service', 'Ingress', 'HorizontalPodAutoscaler', 'PodDisruptionBudget'],
    'api-mysql': ['Deployment', 'Service', 'Ingress'],
    'api-kafka-replay': ['Deployment', 'Service', 'Ingress'],
    'worker-sqlserver': ['Deployment', 'HorizontalPodAutoscaler', 'PodDisruptionBudget'],
    'worker-cdc-kafka': ['Deployment', 'HorizontalPodAutoscaler', 'PodDisruptionBudget'],
    'migrator-mysql': ['Job'],
  };
  for (const kind of requiredByName[name] ?? []) {
    if (!kinds.includes(kind)) {
      process.stderr.write(
        `${name}: missing kind ${kind}; found ${kinds.join(', ')}\n`
      );
      process.exit(1);
    }
  }
}

function tryKubectlDryRun(name, outFile) {
  const dryRun = run('kubectl', [
    'apply',
    '--dry-run=client',
    '--validate=false',
    '-f',
    outFile,
  ]);
  if (dryRun.status === 0) {
    return;
  }
  const text = `${dryRun.stdout ?? ''}\n${dryRun.stderr ?? ''}`;
  if (
    /connection refused|connectex|no such host|dial tcp|Unable to connect|refused/i.test(
      text
    )
  ) {
    process.stdout.write(
      `kubectl dry-run ${name}: skipped (no reachable cluster API); local kind checks used.\n`
    );
    return;
  }
  mustSucceed(`kubectl dry-run ${name}`, dryRun);
}

fs.mkdirSync(outDir, { recursive: true });
requireTool('helm');
// kubectl 可选：无集群时跳过 dry-run，仍做本地 kind 校验。
const hasKubectl = !run('kubectl', ['version', '--client']).error;

mustSucceed(
  'helm lint',
  run('helm', ['lint', chartDir, '-f', path.join(ciDir, 'values-role-api.yaml')])
);

const renderMatrix = [
  ['api-sqlserver', ['values-role-api.yaml', 'values-provider-sqlserver.yaml']],
  ['api-mysql', ['values-role-api.yaml', 'values-provider-mysql.yaml']],
  [
    'api-kafka-replay',
    [
      'values-role-api.yaml',
      'values-provider-sqlserver.yaml',
      'values-api-kafka-replay.yaml',
    ],
  ],
  ['worker-sqlserver', ['values-role-worker.yaml', 'values-provider-sqlserver.yaml']],
  [
    'worker-cdc-kafka',
    [
      'values-role-worker.yaml',
      'values-provider-sqlserver.yaml',
      'values-messaging-cdc-kafka.yaml',
    ],
  ],
  ['migrator-mysql', ['values-role-migrator.yaml', 'values-provider-mysql.yaml']],
];

for (const [name, files] of renderMatrix) {
  const outFile = path.join(outDir, `${name}.yaml`);
  const args = ['template', `fullnet-${name}`, chartDir];
  for (const file of files) {
    args.push('-f', path.join(ciDir, file));
  }
  const result = run('helm', args);
  mustSucceed(`helm template ${name}`, result);
  fs.writeFileSync(outFile, result.stdout, 'utf8');
  validateRenderedManifest(name, result.stdout);
  if (name === 'worker-cdc-kafka') {
    const requiredKafkaFragments = [
      'name: Messaging__Kafka__ConsumerInstanceId',
      'fieldPath: metadata.name',
      'name: Messaging__Kafka__ConsumerGroupProtocol',
      'name: Messaging__Kafka__ClassicPartitionAssignment',
      'name: Messaging__Kafka__CooperativeStickyMigrationCompleted',
      'name: Messaging__Kafka__ProducerBatchSizeBytes',
      'name: Messaging__Kafka__ProducerQueueMaxKbytes',
      'name: Messaging__Kafka__ProducerMaxInFlightRequests',
      'name: Messaging__Kafka__ConsumerBufferHighWatermark',
      'name: Messaging__Kafka__PartitionBufferHighWatermark',
      'name: Messaging__Kafka__PartitionKeyConcurrencySlots',
      'name: Messaging__Kafka__OffsetCommitMode',
    ];
    for (const fragment of requiredKafkaFragments) {
      if (!result.stdout.includes(fragment)) {
        process.stderr.write(`${name}: missing rendered Kafka fragment ${fragment}\n`);
        process.exit(1);
      }
    }
  }
  if (name === 'api-kafka-replay') {
    const requiredReplayFragments = [
      'name: Messaging__KafkaReplay__Enabled',
      'name: Messaging__KafkaReplay__MaximumSynchronousMessages',
      'name: Messaging__KafkaReplay__ExecutionTimeoutSeconds',
      'name: Messaging__Kafka__BootstrapServers',
      'name: Messaging__Kafka__ClientId',
      'name: Messaging__Kafka__SecurityProtocol',
    ];
    for (const fragment of requiredReplayFragments) {
      if (!result.stdout.includes(fragment)) {
        process.stderr.write(`${name}: missing rendered replay fragment ${fragment}\n`);
        process.exit(1);
      }
    }
  }
  if (hasKubectl) {
    tryKubectlDryRun(name, outFile);
  }
}

const counterexamples = [
  [
    'api-kafka-replay-missing-secret',
    [
      'values-role-api.yaml',
      'values-provider-sqlserver.yaml',
      'values-api-kafka-replay-missing-secret.yaml',
    ],
    'api.kafkaReplay.bootstrapSecretName is required',
  ],
  [
    'api-kafka-replay-sasl-invalid',
    [
      'values-role-api.yaml',
      'values-provider-sqlserver.yaml',
      'values-api-kafka-replay-sasl-invalid.yaml',
    ],
    'api.kafkaReplay SaslSsl requires',
  ],
  [
    'budget-overrun',
    ['values-role-api.yaml', 'values-budget-overrun.yaml'],
    'database connection budget exceeded',
  ],
  [
    'missing-edge',
    ['values-role-api.yaml', 'values-missing-edge.yaml'],
    'edgeProtection.declared=true',
  ],
  [
    'affinity-invalid',
    ['values-role-api.yaml', 'values-affinity-invalid.yaml'],
    'requireSessionAffinity may be false only',
  ],
  [
    'custom-hpa-unverified',
    ['values-role-api.yaml', 'values-custom-hpa-unverified.yaml'],
    'adapterInstalledAndVerified=true',
  ],
  [
    'rate-amplification',
    ['values-rate-amplification.yaml'],
    'exceeds edge global rate budget',
  ],
  [
    'messaging-cdc-missing-kafka',
    ['values-role-worker.yaml', 'values-messaging-cdc-missing-kafka.yaml'],
    'worker.messaging.kafka.bootstrapSecretName is required when worker.messaging.mode is CdcKafka',
  ],
  [
    'messaging-consumer-old-broker',
    [
      'values-role-worker.yaml',
      'values-messaging-consumer-old-broker.yaml',
    ],
    'worker.messaging.kafka.brokerMajorVersion must be at least 4 when consumerGroupProtocol is Consumer',
  ],
  [
    'messaging-cooperative-unmigrated',
    [
      'values-role-worker.yaml',
      'values-messaging-cooperative-unmigrated.yaml',
    ],
    'worker.messaging.kafka.cooperativeStickyMigrationCompleted=true is required before enabling CooperativeSticky',
  ],
  [
    'messaging-producer-queue-too-small',
    [
      'values-role-worker.yaml',
      'values-messaging-producer-queue-too-small.yaml',
    ],
    'producerQueueMaxKbytes',
  ],
  [
    'messaging-buffer-invalid',
    ['values-role-worker.yaml', 'values-messaging-buffer-invalid.yaml'],
    'consumerBufferLowWatermark must be less than consumerBufferHighWatermark',
  ],
  [
    'messaging-periodic-unverified',
    ['values-role-worker.yaml', 'values-messaging-periodic-unverified.yaml'],
    'periodicOffsetCommitVerified=true is required before PeriodicWatermark in production',
  ],
];

for (const [name, files, needle] of counterexamples) {
  const args = ['template', `fullnet-bad-${name}`, chartDir];
  for (const file of files) {
    args.push('-f', path.join(ciDir, file));
  }
  mustFail(`helm template counterexample ${name}`, run('helm', args), needle);
}

process.stdout.write('Helm contract orchestration passed.\n');
