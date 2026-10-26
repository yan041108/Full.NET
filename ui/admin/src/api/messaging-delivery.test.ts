import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { getMessagingReplayOperation, executeMessagingReplayOperation, cancelMessagingReplayOperation, verifyMessagingCutoverEvidence,
  getMessagingShadowCapture, startMessagingShadowCapture, closeMessagingShadowCapture, recoverMessagingRollback, type MessagingDeliveryStream } from './messaging-delivery';
vi.mock('./http', () => ({ request: vi.fn() }));
const id = '01929d50-8640-7000-8000-000000000001';
const other = '01929d50-8640-7000-8000-000000000002';
const operation = { operationId: id, state: 'prepared', topicCode: 'messaging.test.v1', replayConsumerName: 'fullnet.messaging.test', eventType: 'fullnet.test',
  schemaVersion: 1, maxMessages: 1, confirmedMessages: 0, processedMessages: 0, alreadyProcessedMessages: 0, rejectedAttempts: 0, attemptCount: 0,
  hasUnconfirmedMessage: false, lastReasonCode: null, createdAtUtc: '2026-10-24T00:00:00Z', updatedAtUtc: '2026-10-24T00:00:00Z', partitions: [] };
const stream = { eventType: 'fullnet.test', schemaVersion: 1 } as MessagingDeliveryStream;
describe('Messaging delivery adapter', () => {
  beforeEach(() => vi.clearAllMocks());
  it('rejects a valid operation belonging to another requested resource', async () => {
    vi.mocked(request).mockResolvedValue(operation);
    await expect(getMessagingReplayOperation(other)).rejects.toThrow('client.invalid_messaging_operation_response');
    await expect(executeMessagingReplayOperation(other, 'reason')).rejects.toThrow();
    await expect(cancelMessagingReplayOperation(other, 'reason')).rejects.toThrow();
  });
  it('sends only trimmed reason on execution and preserves server state', async () => {
    vi.mocked(request).mockResolvedValue({ ...operation, state: 'result_unknown', hasUnconfirmedMessage: true });
    expect((await executeMessagingReplayOperation(id, '  verified reason  ')).state).toBe('result_unknown');
    expect(request).toHaveBeenCalledWith(`/api/v1/messaging/kafka/replay-operations/${id}/execute`, expect.objectContaining({ method: 'POST', body: JSON.stringify({ reason: 'verified reason' }) }));
  });
  it('does not accept evidence for a different stream', async () => {
    vi.mocked(request).mockResolvedValue({ id, eventType: 'fullnet.other', schemaVersion: 1, sampleCount: 1, verifiedAtUtc: operation.createdAtUtc, expiresAtUtc: operation.updatedAtUtc });
    await expect(verifyMessagingCutoverEvidence(stream, 'reason')).rejects.toThrow();
  });
  it('does not accept another rollback generation after recovery', async () => {
    vi.mocked(request).mockResolvedValue({ generation: other, eventType: 'fullnet.test', schemaVersion: 1, phase: 4, createdAtUtc: operation.createdAtUtc, updatedAtUtc: operation.updatedAtUtc });
    await expect(recoverMessagingRollback(id, 'reason')).rejects.toThrow();
  });
});

describe('Shadow capture resource guards', () => {
  const capture = { generation: id, eventType: stream.eventType, schemaVersion: 1, state: 'active', maxEvents: 10, maxPayloadBytes: 100,
    capturedCount: 0, capturedBytes: 0, missingMetadataCount: 0, startedAtUtc: '2026-10-25T00:00:00Z', expiresAtUtc: '2026-10-25T00:01:00Z', closedAtUtc: null, endReason: null };
  it('rejects another stream or generation instead of displaying it', async () => {
    vi.mocked(request).mockResolvedValue({ ...capture, eventType: 'fullnet.other' });
    await expect(getMessagingShadowCapture(stream)).rejects.toThrow();
    vi.mocked(request).mockResolvedValue(capture);
    await expect(closeMessagingShadowCapture(other, 'reason')).rejects.toThrow();
  });
  it('preserves the creation key and expected generation across retries', async () => {
    vi.mocked(request).mockResolvedValue(capture);
    const body = { requestId: other, expectedGeneration: null, eventType: stream.eventType, schemaVersion: 1, maxEvents: 10, windowSeconds: 60, reason: 'controlled capture' };
    expect((await startMessagingShadowCapture(body)).generation).toBe(id);
    expect(request).toHaveBeenLastCalledWith('/api/v1/messaging/delivery/shadow-captures/', { method: 'POST', body: JSON.stringify(body) });
  });
});
