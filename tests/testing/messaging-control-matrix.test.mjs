import assert from 'node:assert/strict';
import test from 'node:test';
import { loadTestMatrix } from '../../scripts/testing/run-dotnet-test-suite.mjs';

test('消息控制面单元选择锁定直接边界与最低发现数', () => {
  const selection = loadTestMatrix().dotnetSuites.unit.selections['messaging-control'];
  assert.equal(selection.minimum, 60);
  for (const name of ['KafkaDeliveryControlTests', 'KafkaCutoverShadowProbeTests', 'CdcDeliveryPositionTests', 'KafkaConnectAdminClientTests', 'KafkaConsumerLagObserverTests'])
    assert.ok(selection.filter.includes(`FullyQualifiedName~${name}`));
});

test('291/292 双库恢复映射与重型分片保持一致', () => {
  const integration = loadTestMatrix().integration;
  for (const number of ['291', '292']) {
    assert.match(integration.migrationSelections[number].filter, /MessagingDeliveryControlTests\.MySql_/);
    assert.match(integration.migrationSelections[number].filter, /MessagingDeliveryControlTests\.SqlServer_/);
  }
  assert.ok(integration.shards['messaging-heavy'].filter.includes('FullyQualifiedName~MessagingDeliveryControlTests'));
  assert.ok(integration.shards.infrastructure.filter.includes('FullyQualifiedName!~MessagingDeliveryControlTests'));
  assert.equal(integration.shards['messaging-control'].minimumPassedOnTestcontainers, 2);
});

test('真实受控影子 CDC 独立选择且只归属消息重型分片', () => {
  const shards = loadTestMatrix().integration.shards;
  const name = 'MessagingShadowCaptureDebeziumTests';
  assert.equal(shards['messaging-real-cdc'].filter, `FullyQualifiedName~${name}`);
  assert.equal(shards['messaging-real-cdc'].minimum, 3);
  assert.equal(shards['messaging-real-cdc'].minimumPassedOnTestcontainers, 3);
  assert.ok(shards['messaging-heavy'].filter.includes(`FullyQualifiedName~${name}`));
  assert.ok(shards.infrastructure.filter.includes(`FullyQualifiedName!~${name}`));
});

test('完整真实切流回退与配置/屏障回归有独立聚焦选择', () => {
  const matrix = loadTestMatrix();
  const unit = matrix.dotnetSuites.unit.selections['messaging-real-control-contracts'];
  assert.equal(unit.minimum, 10);
  assert.match(unit.filter, /KafkaConnectorDeploymentContractTests/);
  assert.match(unit.filter, /MessagingProducerFenceHttpTests/);
  const shards = matrix.integration.shards;
  const name = 'MessagingCutoverRollbackDebeziumTests';
  assert.equal(shards['messaging-cutover-rollback-real'].filter, `FullyQualifiedName~${name}`);
  assert.equal(shards['messaging-cutover-rollback-real'].minimumPassedOnTestcontainers, 2);
  assert.ok(shards['messaging-heavy'].filter.includes(`FullyQualifiedName~${name}`));
  assert.ok(shards.infrastructure.filter.includes(`FullyQualifiedName!~${name}`));
});
