import { describe, expect, it, vi } from 'vitest';
import { createNotificationIntent } from './notification-intents';
import * as httpModule from './http';

describe('notification-intents api', () => {
  it('posts intent payload with attachment file ids', async () => {
    const request = vi.spyOn(httpModule, 'request').mockResolvedValue({
      id: '01912345-6789-7abc-8def-0123456789ab',
      producerKey: 'admin.manual',
      sceneKey: 'email.test',
      idempotencyKey: 'idem-1',
      templateVersionId: '01912345-6789-7abc-8def-0123456789ac',
      bindingVersionId: null,
      policyCategoryKey: 'transactional',
      dispatchModeKey: 'single',
      statusKey: 'accepted',
      routeSnapshotJson: '[]',
      parameterSnapshotJson: '{}',
      recipients: [],
      attachments: [{ fileId: '01912345-6789-7abc-8def-0123456789ad', sortOrder: 0 }],
      createdAtUtc: '2026-09-06T00:00:00Z'
    });

    const result = await createNotificationIntent({
      producerKey: 'admin.manual',
      sceneKey: 'email.test',
      templateKey: 'email.test',
      recipients: [{ recipientTypeKey: 'user', recipientKey: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }],
      parameters: {},
      idempotencyKey: 'idem-1',
      attachmentFileIds: ['01912345-6789-7abc-8def-0123456789ad']
    });

    expect(result.attachments).toHaveLength(1);
    expect(request).toHaveBeenCalledWith(
      '/api/v1/notifications/intents',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });
});
