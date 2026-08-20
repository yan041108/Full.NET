import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function readWorkflow() {
  return readFile(
    path.join(repositoryRoot, '.github/workflows/kafka-capacity.yml'),
    'utf8'
  );
}

function readSecondLevelKeys(yaml, topLevelKey) {
  const lines = yaml.split(/\r?\n/);
  const start = lines.findIndex((line) => line === `${topLevelKey}:`);
  assert.notEqual(start, -1, `missing top-level key ${topLevelKey}`);

  const keys = [];
  for (const line of lines.slice(start + 1)) {
    if (line.trim() !== '' && !line.startsWith(' ')) {
      break;
    }

    const match = /^  ([A-Za-z0-9_-]+):/.exec(line);
    if (match) {
      keys.push(match[1]);
    }
  }

  return keys;
}

function readNamedStep(workflow, name) {
  const marker = `      - name: ${name}`;
  const start = workflow.indexOf(marker);
  assert.notEqual(start, -1, `missing workflow step ${name}`);
  const next = workflow.indexOf('\n      - ', start + marker.length);
  return workflow.slice(start, next === -1 ? workflow.length : next);
}

function readProfileArguments(executionStep, profile) {
  const arm = new RegExp(
    `^ {12}${profile}\\)\\r?\\n([\\s\\S]*?)^ {14};;$`,
    'm'
  ).exec(executionStep);
  assert.ok(arm, `missing ${profile} case arm`);
  return [...arm[1].matchAll(/^\s+--([a-z-]+)\s+([^\r\n]+)$/gm)].map(
    ([, key, value]) => [key, value]
  );
}

function countCsv(value) {
  return value.split(',').length;
}

function profileSampleCount(argumentsList) {
  const values = Object.fromEntries(argumentsList);
  return (
    (countCsv(values['low-rates']) + countCsv(values['throughput-rates'])) *
    countCsv(values['payload-sizes']) *
    countCsv(values['producer-concurrency']) *
    Number(values.repetitions)
  );
}

function profilePlannedMessages(argumentsList) {
  const values = Object.fromEntries(argumentsList);
  const rates = `${values['low-rates']},${values['throughput-rates']}`
    .split(',')
    .map(Number);
  const variantsPerRate =
    countCsv(values['payload-sizes']) *
    countCsv(values['producer-concurrency']) *
    Number(values.repetitions);
  const durationSeconds = Number(values['duration-seconds']);
  const maximumMessagesPerSample = Number(values['max-messages-per-sample']);
  return rates.reduce(
    (total, rate) =>
      total +
      Math.min(maximumMessagesPerSample, rate * durationSeconds) *
        variantsPerRate,
    0
  );
}

test('Kafka capacity workflow is manual and protected', async () => {
  const workflow = await readWorkflow();

  assert.deepEqual(readSecondLevelKeys(workflow, 'on'), ['workflow_dispatch']);
  assert.match(workflow, /permissions:\s*\n\s+contents: read/);
  assert.match(workflow, /environment: kafka-capacity/);
  assert.match(workflow, /runs-on: \[self-hosted, linux, x64, kafka-capacity\]/);
  assert.match(workflow, /if: github\.ref == 'refs\/heads\/main'/);
  assert.match(workflow, /group: kafka-capacity-dedicated-cluster/);
  assert.doesNotMatch(workflow, /group: kafka-capacity-\$\{\{/);
  assert.match(workflow, /cancel-in-progress: false/);
  assert.match(workflow, /timeout-minutes: 240/);
  assert.match(workflow, /persist-credentials: false/);

  const actionReferences = [...workflow.matchAll(/uses: (actions\/[^@\s]+)@([^\s]+)/g)];
  assert.deepEqual(
    actionReferences.map(([, action, revision]) => `${action}@${revision}`),
    [
      'actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683',
      'actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9',
      'actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02',
    ]
  );
  assert.ok(actionReferences.length > 0, 'official action references missing');
  for (const [, action, revision] of actionReferences) {
    assert.match(revision, /^[0-9a-f]{40}$/, `${action} must use an immutable commit SHA`);
  }
});

test('Kafka capacity workflow keeps secrets out of command arguments', async () => {
  const workflow = await readWorkflow();
  const executionStep = readNamedStep(workflow, 'Run bounded Kafka capacity profile');
  const smokeBudgetStep = readNamedStep(workflow, 'Prepare smoke performance budget');
  const matrixBudgetStep = readNamedStep(workflow, 'Prepare matrix performance budget');

  for (const key of [
    'KafkaCapacity__ExpectedClusterId',
    'KafkaCapacity__Kafka__BootstrapServers',
    'KafkaCapacity__Kafka__SaslUsername',
    'KafkaCapacity__Kafka__SaslPassword',
  ]) {
    assert.ok(workflow.includes(key), `missing protected environment key ${key}`);
    assert.ok(
      executionStep.includes(key),
      `${key} must only be exposed to the capacity execution step`
    );
  }

  assert.doesNotMatch(workflow, /--bootstrap|--username|--password/i);
  assert.match(workflow, /CAPACITY_APPROVAL_ID: \$\{\{ inputs\.approval_id \}\}/);
  assert.match(workflow, /CAPACITY_REASON: \$\{\{ inputs\.reason \}\}/);
  assert.match(workflow, /--approval-id "\$CAPACITY_APPROVAL_ID"/);
  assert.match(workflow, /--reason "\$CAPACITY_REASON"/);
  assert.match(workflow, /umask 077/);
  assert.match(workflow, /rm -f "\$budget"/);
  assert.match(workflow, /trap 'rm -f "\$budget"' EXIT/);
  assert.match(workflow, /- name: Prepare smoke performance budget/);
  assert.match(workflow, /- name: Prepare matrix performance budget/);
  assert.doesNotMatch(
    executionStep,
    /KAFKA_CAPACITY_(SMOKE|MATRIX)_BUDGET_JSON/,
    'unselected profile budgets must not be exposed to the Kafka execution step'
  );
  assert.match(smokeBudgetStep, /secrets\.KAFKA_CAPACITY_SMOKE_BUDGET_JSON/);
  assert.doesNotMatch(smokeBudgetStep, /secrets\.KAFKA_CAPACITY_MATRIX_BUDGET_JSON/);
  assert.match(matrixBudgetStep, /secrets\.KAFKA_CAPACITY_MATRIX_BUDGET_JSON/);
  assert.doesNotMatch(matrixBudgetStep, /secrets\.KAFKA_CAPACITY_SMOKE_BUDGET_JSON/);

  const stepsAllowedToReadSecrets = [smokeBudgetStep, matrixBudgetStep, executionStep];
  const workflowWithoutSecretSteps = stepsAllowedToReadSecrets.reduce(
    (remaining, step) => remaining.replace(step, ''),
    workflow
  );
  assert.doesNotMatch(workflowWithoutSecretSteps, /\$\{\{\s*secrets\./);
  assert.doesNotMatch(workflow, /set -x/);
});

test('Kafka capacity workflow has bounded smoke and explicit matrix profiles', async () => {
  const workflow = await readWorkflow();
  const executionStep = readNamedStep(workflow, 'Run bounded Kafka capacity profile');
  const smokeArguments = readProfileArguments(executionStep, 'smoke');
  const matrixArguments = readProfileArguments(executionStep, 'matrix');

  assert.match(
    workflow,
    /options:\s*\n\s+- smoke\s*\n\s+- matrix\s*\n\s+- scope_b_smoke\s*\n\s+- scope_c_smoke/
  );
  assert.match(workflow, /--scope kafka_transport/);
  assert.match(workflow, /--execute true/);
  assert.match(workflow, /--delete-topic false/);
  assert.deepEqual(smokeArguments, [
    ['scope', 'kafka_transport'],
    ['scenarios', 'low-rate,throughput'],
    ['low-rates', '20'],
    ['throughput-rates', '200'],
    ['payload-sizes', '128'],
    ['producer-concurrency', '2'],
    ['partitions', '2'],
    ['replication-factor', '1'],
    ['repetitions', '1'],
    ['warmup-seconds', '1'],
    ['duration-seconds', '2'],
    ['drain-seconds', '15'],
    ['max-messages-per-sample', '1000'],
  ]);
  assert.deepEqual(matrixArguments, [
    ['scope', 'kafka_transport'],
    ['scenarios', 'low-rate,throughput'],
    ['low-rates', '10,100'],
    ['throughput-rates', '1000,5000,10000'],
    ['payload-sizes', '256,4096'],
    ['producer-concurrency', '1,4'],
    ['partitions', '12'],
    ['replication-factor', '3'],
    ['repetitions', '3'],
    ['warmup-seconds', '10'],
    ['duration-seconds', '60'],
    ['drain-seconds', '120'],
    ['max-messages-per-sample', '5000000'],
  ]);
  assert.equal(profileSampleCount(smokeArguments), 2);
  assert.equal(profileSampleCount(matrixArguments), 60);
  const matrixValues = Object.fromEntries(matrixArguments);
  assert.equal(profilePlannedMessages(smokeArguments), 440);
  assert.equal(profilePlannedMessages(matrixArguments), 11_599_200);
  assert.ok(
    profilePlannedMessages(matrixArguments) <= 100_000_000,
    'matrix must fit inside the Runner planned-message limit'
  );
  const matrixWorstCaseMinutes =
    (profileSampleCount(matrixArguments) *
      (Number(matrixValues['warmup-seconds']) +
        Number(matrixValues['duration-seconds']) +
        Number(matrixValues['drain-seconds']))) /
    60;
  assert.equal(matrixWorstCaseMinutes, 190);
  assert.ok(matrixWorstCaseMinutes < 240, 'matrix must fit inside the job timeout');
  assert.match(executionStep, /\*\)\s+echo "Unsupported capacity profile\." >&2\s+exit 2/);
  assert.match(workflow, /CapacityStatus=Capacity-not-verified/);
  assert.doesNotMatch(workflow, /CapacityStatus=(Verified|Certified)/);
});

test('Kafka capacity workflow bounds Scope C smoke with worker parity and secret skip', async () => {
  const workflow = await readWorkflow();
  const executionStep = readNamedStep(workflow, 'Run bounded Kafka capacity profile');
  const scopeCArguments = readProfileArguments(executionStep, 'scope_c_smoke');
  const scopeCArm = new RegExp(
    '^ {12}scope_c_smoke\\)\\r?\\n([\\s\\S]*?)^ {14};;$',
    'm'
  ).exec(executionStep);
  assert.ok(scopeCArm, 'missing scope_c_smoke case arm');
  const scopeCBody = scopeCArm[1];

  assert.deepEqual(scopeCArguments, [
    ['scope', 'transaction_outbox_cdc'],
    ['host-parity-mode', 'worker'],
    ['scenarios', 'low-rate'],
    ['low-rates', '20'],
    ['payload-sizes', '128'],
    ['producer-concurrency', '2'],
    ['partitions', '2'],
    ['replication-factor', '1'],
    ['repetitions', '1'],
    ['warmup-seconds', '0'],
    ['duration-seconds', '2'],
    ['drain-seconds', '45'],
    ['max-messages-per-sample', '100'],
  ]);
  assert.match(
    scopeCBody,
    /KAFKA_CAPACITY_MYSQL_CONNECTION_STRING not configured; skipping Scope C smoke\./
  );
  assert.match(
    scopeCBody,
    /KAFKA_CAPACITY_CONNECT_BASE_URI not configured; skipping Scope C smoke\./
  );
  assert.match(scopeCBody, /KafkaCapacity__Connect__BaseUri="\$KAFKA_CAPACITY_CONNECT_BASE_URI"/);
  assert.match(
    executionStep,
    /KAFKA_CAPACITY_CONNECT_BASE_URI: \$\{\{ secrets\.KAFKA_CAPACITY_CONNECT_BASE_URI \}\}/
  );
  assert.doesNotMatch(scopeCBody, /echo "\$KAFKA_CAPACITY_/);
  assert.doesNotMatch(scopeCBody, /echo .*ConnectionString/i);
  assert.doesNotMatch(scopeCBody, /set -x/);
  assert.match(scopeCBody, /--scope transaction_outbox_cdc/);
  assert.match(scopeCBody, /--host-parity-mode worker/);
  assert.doesNotMatch(scopeCBody, /--host-parity-mode fast/);
});

test('Kafka capacity workflow always preserves bounded evidence', async () => {
  const workflow = await readWorkflow();
  const prepareStep = workflow.indexOf('- name: Prepare evidence directory');
  const setupStep = workflow.indexOf('actions/setup-dotnet@');
  const restoreStep = workflow.indexOf('- name: Restore benchmark');

  assert.ok(prepareStep > 0, 'evidence preparation step missing');
  assert.ok(prepareStep < setupStep, 'evidence must exist before toolchain setup');
  assert.ok(prepareStep < restoreStep, 'evidence must exist before restore/build');
  assert.match(workflow, /- name: Finalize workflow metadata\s+if: always\(\)/);
  assert.match(workflow, /- name: Cleanup temporary budget\s+if: always\(\)/);
  assert.match(workflow, /- name: Upload Kafka capacity evidence\s+if: always\(\)/);
  assert.match(workflow, /retention-days: 30/);
  assert.match(workflow, /if-no-files-found: error/);
  assert.match(
    workflow,
    /path: BenchmarkDotNet\.Artifacts\/kafka-capacity\/\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}\/\*\*/
  );
  assert.doesNotMatch(workflow, /path: BenchmarkDotNet\.Artifacts\/kafka-capacity\/\*\*/);
});
