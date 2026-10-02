import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

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

const requiredLogFields = [
  '@t',
  '@mt',
  'LogEventId',
  'Instance',
  'log.class',
];

const optionalLogFields = [
  '@l',
  'Application',
  'TraceId',
  'SpanId',
  'log.stream',
  'reliability.class',
  'data.classification',
  'EventName',
];

const requiredAlerts = [
  'FullNetErrorCriticalStorm',
  'FullNetBestEffortLogDrop',
  'FullNetPriorityLogDrop',
  'FullNetFluentBitForwardQueueCapacityLow',
  'FullNetNodeDiskPressure',
  'FullNetFluentBitTargetDown',
  'FullNetFluentBitDaemonSetUnavailable',
  'FullNetFluentBitPriorityOutputDrop',
  'FullNetFluentBitPriorityOutputRetryWithoutProgress',
  'FullNetFluentBitBestEffortOutputDrop',
  'FullNetAuditB1QueuePressure',
  'FullNetAuditB1WaitOrFailure',
  'FullNetCacheInvalidationStaleWindow',
  'FullNetRedisReconnectOrEviction',
  'FullNetDatabaseConnectionWait',
  'FullNetDatabaseConnectionAcquireTimeout',
  'FullNetDatabaseConnectionAdmissionRejected',
  'FullNetOutboxJobsBacklogAge',
  'FullNetMessagingKafkaConsumeFailures',
  'FullNetMessagingSqlServerCdcCaptureJobStopped',
  'FullNetMessagingMySqlBinlogRetentionLow',
  'FullNetMessagingConnectorOffsetUnrecoverable',
  'FullNetMessagingKafkaLagNearRetention',
  'FullNetEdgeGlobalRateRejected',
  'FullNetWafOrExternalLimiterDown',
  'FullNetHpaAtMaxReplicas',
  'FullNetPdbUnsatisfied',
];

test('observability deploy files exist and parse as text/JSON', async () => {
  const files = [
    'deploy/observability/fluent-bit-values.yaml',
    'deploy/observability/otel-collector-values.yaml',
    'deploy/observability/prometheus-rules.yaml',
    'deploy/observability/grafana-dashboard.json',
    'tests/deployment/fluent-bit-output-alerts.promtest.yaml',
    'tests/deployment/fluent-bit-prometheus-scrape.yml',
    'eng/testing/fluent-bit-prometheus-alert-smoke.mjs',
    'eng/testing/fluent-bit-forward-tls-smoke.mjs',
    'eng/testing/fluent-bit-enospc-characterization.mjs',
    'tests/deployment/fluent-bit-enospc-probe.conf',
    'eng/testing/fluent-bit-queue-overflow-characterization.mjs',
    'tests/deployment/fluent-bit-queue-overflow-probe.conf',
    'deploy/observability/README.md',
    'docs/runbooks/high-concurrency-multi-instance-production.md',
    'docs/runbooks/data-protection-key-recovery.md',
    'docs/runbooks/cache-redis-recovery.md',
    'docs/runbooks/audit-log-backpressure.md',
    'docs/runbooks/cdc-kafka-cutover-rollback.md',
  ];
  for (const file of files) {
    assert.equal(await exists(file), true, `${file} missing`);
    const text = await read(file);
    assert.ok(text.trim().length > 0, `${file} empty`);
  }
  JSON.parse(await read('deploy/observability/grafana-dashboard.json'));
});

test('Fluent Bit contract: buffers, TLS, split streams, no durable audit duplication', async () => {
  const text = await read('deploy/observability/fluent-bit-values.yaml');
  assert.match(text, /storage\.path/);
  assert.match(text, /Mem_Buf_Limit/);
  assert.match(text, /storage\.type\s+filesystem/);
  assert.match(text, /Retry_Limit|retry_limit/);
  assert.match(text, /tls\s+On/);
  assert.match(text, /fullnet\.b2\./);
  assert.match(text, /fullnet\.priority\./);
  assert.match(text, /Name\s+s3/);
  assert.match(text, /durableAuditViaFluentBit:\s*false/);
  assert.match(text, /recursiveSinkWriteback:\s*false/);
  for (const alias of ['fullnet_priority_forward', 'fullnet_b2_forward', 'fullnet_b2_archive']) {
    assert.match(text, new RegExp(`Alias\\s+${alias}`));
  }
  assert.match(text, /Alias\s+fullnet_priority_forward[\s\S]*?Retry_Limit\s+False[\s\S]*?Alias\s+fullnet_b2_forward/);
  assert.match(text, /Alias\s+fullnet_b2_forward[\s\S]*?Retry_Limit\s+3[\s\S]*?Alias\s+fullnet_b2_archive/);
  for (const alias of ['fullnet_priority_forward', 'fullnet_b2_forward']) {
    const output = text.match(new RegExp(`Alias\\s+${alias}([\\s\\S]*?)(?=\\n\\s*\\[OUTPUT\\]|\\n\\s*customParsers:)`))?.[1];
    assert.ok(output, `${alias} output missing`);
    assert.match(output, /Require_ack_response\s+On/);
    assert.match(output, /net\.io_timeout\s+5s/);
    assert.match(output, /tls\s+On/);
    assert.match(output, /tls\.verify\s+On/);
    assert.match(output, /tls\.verify_hostname\s+On/);
    assert.match(output, /tls\.ca_file\s+\/fluent-bit\/tls\/ca\.crt/);
  }
  assert.match(text, /- name: forward-ca\s+secret:\s+secretName: fullnet-forward-ca\s+items:\s+- key: ca\.crt\s+path: ca\.crt/);
  assert.match(text, /- name: forward-ca\s+mountPath: \/fluent-bit\/tls\s+readOnly: true/);
  assert.match(text, /corruptChunkIsolation:\s*false/);
  const required = text.match(/requiredLogFields:\s*\n((?:\s+- [^\n]+\n)+)/)?.[1] ?? '';
  const optional = text.match(/optionalLogFields:\s*\n((?:\s+- [^\n]+\n)+)/)?.[1] ?? '';
  for (const field of requiredLogFields) assert.match(required, new RegExp(field.replace('.', '\\.')));
  for (const field of optionalLogFields) assert.match(optional, new RegExp(field.replace('.', '\\.')));
  assert.doesNotMatch(required, /DiagnosticGroup|timestamp|trace_id|level/);
  assert.match(text, /Remove\s+DiagnosticGroup/);
});

test('Fluent Bit output loss alerts use stable output aliases', async () => {
  const rules = await read('deploy/observability/prometheus-rules.yaml');
  const workflow = await read('.github/workflows/fluent-bit-collector-route.yml');
  assert.match(rules, /fluentbit_output_dropped_records_total\{name="fullnet_priority_forward"\}/);
  assert.match(rules, /fluentbit_output_dropped_records_total\{name=~"fullnet_b2_forward\|fullnet_b2_archive"\}/);
  assert.match(rules, /fluentbit_output_retries_failed_total/);
  assert.match(rules, /fluentbit_output_retries_total\{name="fullnet_priority_forward"\}/);
  assert.match(rules, /fluentbit_output_proc_records_total\{name="fullnet_priority_forward"\}/);
  assert.match(rules, /fluentbit_output_chunk_available_capacity_percent\{name=~"fullnet_priority_forward\|fullnet_b2_forward"\}/);
  assert.match(rules, /alert: FullNetFluentBitForwardQueueCapacityLow/);
  assert.doesNotMatch(rules, /alert: FullNetFluentBitSpoolHigh/);
  assert.doesNotMatch(rules, /fluentbit_storage_chunks_up\s*\/\s*fluentbit_storage_chunks_total/);
  assert.doesNotMatch(rules, /kubelet_volume_stats_available_bytes\{persistentvolumeclaim=~"\.\*fluent-bit\.\*"\}/);
  assert.match(rules, /kube_node_status_condition\{condition="DiskPressure",status="true"\}/);
  assert.match(await read('tests/deployment/fluent-bit-prometheus-scrape.yml'), /metrics_path:\s*\/api\/v2\/metrics\/prometheus/);
  assert.match(workflow, /prometheus-log-alerts-test\.mjs/);
  assert.match(workflow, /fluent-bit-output-alerts\.promtest\.yaml/);
  assert.match(workflow, /run: node eng\/testing\/fluent-bit-prometheus-alert-smoke\.mjs/);
  assert.match(workflow, /run: node eng\/testing\/fluent-bit-forward-ack-smoke\.mjs/);
  assert.match(workflow, /'eng\/testing\/fluent-bit-forward-ack-smoke\.mjs'/);
  assert.match(workflow, /run: node eng\/testing\/fluent-bit-forward-tls-smoke\.mjs/);
  assert.match(workflow, /'eng\/testing\/fluent-bit-forward-tls-smoke\.mjs'/);
  assert.match(workflow, /run: node eng\/testing\/fluent-bit-enospc-characterization\.mjs/);
  assert.match(workflow, /'tests\/deployment\/fluent-bit-enospc-probe\.conf'/);
  assert.match(workflow, /run: node eng\/testing\/fluent-bit-queue-overflow-characterization\.mjs/);
  assert.match(workflow, /'tests\/deployment\/fluent-bit-queue-overflow-probe\.conf'/);
});

test('Fluent Bit Collector candidate declares one Tail and a Pod label gate', async () => {
  const source = await read('deploy/observability/fluent-bit-values.yaml');
  assert.match(source, /Parsers_File\s+\/fluent-bit\/etc\/conf\/custom_parsers\.conf/);
  const inputs = source.match(/  inputs: \|([\s\S]*?)\n  filters: \|/)?.[1];
  const filters = source.match(/  filters: \|([\s\S]*?)\n  outputs: \|/)?.[1];
  assert.ok(inputs);
  assert.ok(filters);
  assert.equal((inputs.match(/^    \[INPUT\]/gm) ?? []).length, 1);
  assert.match(inputs, /Multiline\.Parser\s+docker,\s*cri/i);
  assert.match(filters, /Name\s+kubernetes[\s\S]*Labels\s+On/i);
  assert.match(filters, /Name\s+kubernetes[\s\S]*Merge_Log\s+Off/i);
  assert.match(filters, /Rule\s+\$kubernetes\['labels'\]\['fullnet\.io\/log-ingress'\]\s+\^collector\$/);
  assert.ok(filters.indexOf('Name                parser') > filters.indexOf("$kubernetes['labels']['fullnet.io/log-ingress']"));
  assert.match(filters, /Name\s+rewrite_tag/);
  assert.match(filters, /Name\s+grep[\s\S]*Match\s+fullnet\.raw[\s\S]*Regex\s+LogEventId/);
});

test('Fluent Bit rejects records missing the declared Compact JSON envelope fields', async () => {
  const source = await read('deploy/observability/fluent-bit-values.yaml');
  const filters = source.match(/  filters: \|([\s\S]*?)\n  outputs: \|/)?.[1];
  assert.ok(filters);
  const grep = source.match(/\[FILTER\]\s+Name\s+grep\s+Match\s+fullnet\.raw([\s\S]*?)(?=\[FILTER\])/i)?.[1];
  assert.ok(grep);
  assert.match(grep, /Logical_Op\s+AND/i);
  const requiredAliases = new Map([
    ['@t', '_fullnet_required_timestamp'],
    ['@mt', '_fullnet_required_message'],
    ['log.class', '_fullnet_required_class'],
  ]);
  for (const field of requiredLogFields) {
    const key = requiredAliases.get(field) ?? field;
    const escaped = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    assert.match(grep, new RegExp(`Regex\\s+${escaped}\\s+`));
    if (requiredAliases.has(field)) {
      assert.match(filters, new RegExp(`Copy\\s+${field.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s+${escaped}`));
      assert.equal((filters.match(new RegExp(`Remove\\s+${escaped}`, 'g')) ?? []).length, 2);
    }
  }
});

test('OTel Collector contract: memory_limiter, batch, retry, file_storage', async () => {
  const text = await read('deploy/observability/otel-collector-values.yaml');
  assert.match(text, /memory_limiter:/);
  assert.match(text, /batch:/);
  assert.match(text, /retry_on_failure:/);
  assert.match(text, /file_storage:/);
  assert.match(text, /storage:\s*file_storage/);
  assert.match(text, /recursiveSinkWriteback:\s*false/);
  assert.match(text, /durableAuditDuplication:\s*false/);
  assert.match(text, /key:\s*DiagnosticGroup[\s\S]*action:\s*delete/);
});

test('Logstash PQ restart probe keeps manual Kafka commits and per-event checkpoints', async () => {
  const settings = await read('tests/deployment/logstash-pq-probe.yml');
  const blocked = await read('tests/deployment/logstash-pq-blocked.conf');
  const script = await read('eng/testing/logstash-pq-restart-smoke.mjs');
  const workflow = await read('.github/workflows/logstash-pq-restart.yml');
  const packageJson = JSON.parse(await read('package.json'));
  assert.match(settings, /queue\.type:\s*persisted/);
  assert.match(settings, /queue\.checkpoint\.writes:\s*1/);
  assert.match(blocked, /enable_auto_commit\s*=>\s*false/);
  assert.match(script, /committedOffset\(\) === 1/);
  assert.match(script, /docker\(\['kill', blocked\]\)/);
  assert.match(script, /docker\(\['stop', '-t', '1', broker\]\)/);
  assert.match(workflow, /node eng\/testing\/logstash-pq-restart-smoke\.mjs/);
  assert.match(packageJson.scripts['test:observability-logstash-pq:live'], /logstash-pq-restart-smoke\.mjs/);
});

test('Prometheus rules cover required high-concurrency alerts', async () => {
  const text = await read('deploy/observability/prometheus-rules.yaml');
  for (const alert of requiredAlerts) {
    assert.match(text, new RegExp(`alert:\\s*${alert}`));
  }
  assert.match(text, /fullnet_messaging_kafka_consume_results_total/);
  assert.match(text, /fullnet_outbox_backlog_oldest_age_seconds/);
  assert.match(text, /fullnet_jobs_backlog_oldest_age_seconds/);
  assert.match(text, /fullnet_messaging_cdc_sqlserver_capture_job_running/);
  assert.match(text, /fullnet_messaging_cdc_mysql_binlog_retention_hours/);
  assert.match(text, /fullnet_messaging_connector_offset_unrecoverable/);
  assert.match(text, /fullnet_messaging_kafka_lag_retention_ratio/);
  assert.match(text, /fullnet_db_connection_wait_seconds_bucket/);
  assert.match(text, /by\s*\(le,\s*provider,\s*host_role\)/);
  assert.match(text, /fullnet_db_connection_acquire_total\{outcome="timeout"\}/);
  assert.match(text, /fullnet_db_connection_acquire_total\{outcome="rejected"\}/);
  assert.doesNotMatch(text, /message_id|tenant_id|MessageId|TenantId/);
  assert.doesNotMatch(text, /fullnet_outbox_oldest_message_age_seconds/);
});

test('Runbooks cover SLO, DP keys, Redis split, audit fail-open/closed, Expand/Contract', async () => {
  const production = await read(
    'docs/runbooks/high-concurrency-multi-instance-production.md'
  );
  assert.match(production, /99\.9%/);
  assert.match(production, /Expand\/Contract/);
  assert.match(production, /Capacity-not-verified/);
  assert.match(production, /RPO\/RTO/);

  const dp = await read('docs/runbooks/data-protection-key-recovery.md');
  assert.match(dp, /Key Ring/);
  assert.match(dp, /X\.509|certificate/i);

  const redis = await read('docs/runbooks/cache-redis-recovery.md');
  assert.match(redis, /Cache:RedisConnectionString/);
  assert.match(redis, /Realtime:RedisBackplaneConnectionString/);

  const audit = await read('docs/runbooks/audit-log-backpressure.md');
  assert.match(audit, /fail-open/);
  assert.match(audit, /B0|B1|B2/);

  const cdc = await read('docs/runbooks/cdc-kafka-cutover-rollback.md');
  assert.match(cdc, /DeliveryCutover/);
  assert.match(cdc, /Capacity-not-verified/);
  assert.match(cdc, /rollback/i);
  assert.match(cdc, /DLQ|dead.?letter/i);
  assert.match(cdc, /reconcile/i);
});
