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
  'timestamp',
  'level',
  'message',
  'application',
  'instance',
  'trace_id',
  'span_id',
  'log.class',
  'log.stream',
  'reliability.class',
  'data.classification',
  'DiagnosticGroup',
  'EventName',
];

const requiredAlerts = [
  'FullNetErrorCriticalStorm',
  'FullNetBestEffortLogDrop',
  'FullNetPriorityLogDrop',
  'FullNetFluentBitSpoolHigh',
  'FullNetFluentBitDiskFull',
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
  for (const field of requiredLogFields) {
    assert.match(text, new RegExp(field.replace('.', '\\.')));
  }
  assert.match(text, /Remove\s+DiagnosticGroup/);
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
