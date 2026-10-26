import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { listOperationalDeadLetters, getOperationalDeadLetter, recoverOperationalDeadLetter } from './messaging-operations';

vi.mock('./http', () => ({ request: vi.fn() }));

export const deadLetter = {
  id: '01929d50-8640-7000-8000-000000000001', source: 'legacy', consumerName: null,
  messageId: '01929d50-8640-7000-8000-000000000001', messageType: 'fullnet.test.failed',
  schemaVersion: 1, tenantId: null, attempts: 3, errorCode: 'outbox.max_attempts_exceeded',
  firstFailedAtUtc: '2026-10-24T00:00:00Z', lastFailedAtUtc: '2026-10-24T00:00:00Z',
  sourceTopic: null, sourcePartition: null, sourceOffset: null, dlqTopic: null,
  dlqPartition: null, dlqOffset: null, canReplay: true, replayedAtUtc: null
};

describe('messaging dead-letter operations', () => {
  beforeEach(() => vi.resetAllMocks());

  it('encodes exact filters and passes cancellation to the selected source', async () => {
    vi.mocked(request).mockResolvedValue({ items: [deadLetter], page: 1, pageSize: 20, total: 1 });
    const controller = new AbortController();
    await listOperationalDeadLetters('legacy', { page: 1, pageSize: 20, messageType: 'fullnet.test&failed' }, controller.signal);
    expect(request).toHaveBeenCalledWith('/api/v1/messaging/outbox/dead-letters?page=1&pageSize=20&messageType=fullnet.test%26failed', { signal: controller.signal });
  });

  it('rejects malformed responses instead of treating them as an empty queue', async () => {
    vi.mocked(request).mockResolvedValue({ items: [{ ...deadLetter, sourceOffset: 9007199254740992 }], page: 1, pageSize: 20, total: 1 });
    await expect(listOperationalDeadLetters('kafka', { page: 1, pageSize: 20 })).rejects.toThrow('client.invalid_operational_dead_letter_page');
  });

  it('uses persisted record identity and submits only an explicit reason', async () => {
    vi.mocked(request).mockResolvedValue({ id: deadLetter.id, outcome: 'processed' });
    await expect(recoverOperationalDeadLetter('kafka', deadLetter.id, ' fixed root cause ')).resolves.toBe('processed');
    expect(request).toHaveBeenCalledWith(`/api/v1/messaging/kafka/dead-letters/${deadLetter.id}/replay`, {
      method: 'POST', body: JSON.stringify({ reason: 'fixed root cause' })
    });
  });

  it('validates detail metadata and keeps large offsets as exact strings', async () => {
    vi.mocked(request).mockResolvedValue({ ...deadLetter, source: 'kafka', sourceOffset: '9007199254740993' });
    expect((await getOperationalDeadLetter('kafka', deadLetter.id)).sourceOffset).toBe('9007199254740993');
  });
});
