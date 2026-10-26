import { describe, expect, it } from 'vitest';
import { isMessagingShadowCapture, isMessagingShadowCapturePolicy, isMessagingOffset, isMessagingReplayOperation, isMessagingDeliveryOverview, isMessagingCutoverEvidence, isMessagingRollbackOperation } from '../src/messaging-operations.js';

const id = '01929d50-8640-7000-8000-000000000001';
const operation = { operationId: id, state: 'result_unknown', topicCode: 'messaging.test.v1', replayConsumerName: 'fullnet.messaging.test',
  eventType: 'fullnet.test', schemaVersion: 1, maxMessages: 100, confirmedMessages: 1, processedMessages: 0, alreadyProcessedMessages: 1,
  rejectedAttempts: 0, attemptCount: 2, hasUnconfirmedMessage: true, lastReasonCode: 'execution_interrupted',
  createdAtUtc: '2026-10-24T00:00:00Z', updatedAtUtc: '2026-10-24T00:00:01Z',
  partitions: [{ partition: 0, startOffset: '9007199254740993', endOffsetExclusive: '9007199254740996', nextOffset: '9007199254740994' }] };

describe('Messaging durable operation contracts', () => {
  it('preserves Int64 offsets as strings beyond Number precision', () => {
    expect(isMessagingReplayOperation(operation)).toBe(true);
    expect(isMessagingOffset('9223372036854775807')).toBe(true);
    for (const value of [9007199254740993, '-1', '1e3', '9223372036854775808', ' 10', '']) expect(isMessagingOffset(value)).toBe(false);
  });
  it('rejects contradictory counts, unknown state, duplicated partitions and backward progress', () => {
    for (const delta of [{ confirmedMessages: 2 }, { processedMessages: Number.MAX_SAFE_INTEGER + 1 }, { state: 'healthy' },
      { partitions: [...operation.partitions, ...operation.partitions] }, { partitions: [{ ...operation.partitions[0], nextOffset: '1' }] }])
      expect(isMessagingReplayOperation({ ...operation, ...delta })).toBe(false);
  });
  it('accepts unknown observations only as explicit nulls and never defaults missing data to zero', () => {
    const overview = { backlog: null, streams: [], observationState: 'partial', hasMoreStreams: false, unattributedKafkaDeadLetters: null,
      cutoverEnabled: false, replayEnabled: false, replayMaximumSynchronousMessages: 100, replayExecutionTimeoutSeconds: 30, controlAdapterConfigured: false, shadowCapturePolicy: { enabled: false, eventType: null, schemaVersion: null, maximumEvents: null, maximumPayloadBytes: null, maximumWindowSeconds: null }, targetRuntimeState: 'not_observed' };
    expect(isMessagingDeliveryOverview(overview)).toBe(true);
    expect(isMessagingDeliveryOverview({ ...overview, backlog: {} })).toBe(false);
    expect(isMessagingDeliveryOverview({ ...overview, targetRuntimeState: 'ready' })).toBe(false);
    expect(isMessagingDeliveryOverview({ ...overview, unattributedKafkaDeadLetters: undefined })).toBe(false);
  });
  it('checks real evidence and closed rollback phases', () => {
    const evidence = { id, eventType: 'fullnet.test', schemaVersion: 1, sampleCount: 1, verifiedAtUtc: operation.createdAtUtc, expiresAtUtc: operation.updatedAtUtc };
    expect(isMessagingCutoverEvidence(evidence)).toBe(true);
    expect(isMessagingCutoverEvidence({ ...evidence, sampleCount: 0 })).toBe(false);
    const rollback = { generation: id, eventType: 'fullnet.test', schemaVersion: 1, phase: 5, createdAtUtc: operation.createdAtUtc, updatedAtUtc: operation.updatedAtUtc };
    expect(isMessagingRollbackOperation(rollback)).toBe(true);
    expect(isMessagingRollbackOperation({ ...rollback, phase: '5' })).toBe(false);
    expect(isMessagingRollbackOperation({ ...rollback, phase: 6 })).toBe(false);
  });
});

describe('Shadow capture bounded metadata', () => {
  it('rejects stale disabled policy and excessive active budgets', () => {
    const policy = { enabled: false, eventType: null, schemaVersion: null, maximumEvents: null, maximumPayloadBytes: null, maximumWindowSeconds: null };
    expect(isMessagingShadowCapturePolicy(policy)).toBe(true);
    expect(isMessagingShadowCapturePolicy({ ...policy, eventType: 'fullnet.stale' })).toBe(false);
    expect(isMessagingShadowCapturePolicy({ ...policy, enabled: true })).toBe(false);
  });
  it('rejects counters beyond the window and incomplete terminal metadata', () => {
    const capture = { generation: id, eventType: 'fullnet.test', schemaVersion: 1, state: 'closed', maxEvents: 10, maxPayloadBytes: 100,
      capturedCount: 1, capturedBytes: 2, missingMetadataCount: 0, startedAtUtc: operation.createdAtUtc, expiresAtUtc: operation.updatedAtUtc,
      closedAtUtc: operation.updatedAtUtc, endReason: 'operator_closed' };
    expect(isMessagingShadowCapture(capture)).toBe(true);
    for (const delta of [{ capturedCount: 11 }, { capturedBytes: 101 }, { closedAtUtc: null }, { state: 'blocked' }, { expiresAtUtc: operation.createdAtUtc }])
      expect(isMessagingShadowCapture({ ...capture, ...delta })).toBe(false);
  });
});
