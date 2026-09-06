import { describe, expect, it } from 'vitest';

import type { HttpClient, HttpRequestOptions } from '../src/api/http';
import { createInboxMessagesClient } from '../src/features/notifications/inbox-messages-client';

const inboxPage = {
  items: [{
    id: '018f0000-0000-7000-8000-000000000010',
    title: '审批提醒',
    content: '您有一条待办需要处理。',
    status: 'unread' as const,
    readAtUtc: null,
    createdAtUtc: '2026-09-07T00:00:00.000Z',
    createdByUserId: null
  }],
  page: 1,
  pageSize: 20,
  total: 1
};

function createHttp(responses: readonly unknown[]): {
  readonly http: HttpClient;
  readonly calls: HttpRequestOptions[];
} {
  const calls: HttpRequestOptions[] = [];
  let index = 0;
  return {
    calls,
    http: {
      async request<T>(options: HttpRequestOptions): Promise<T> {
        calls.push(options);
        return responses[index++] as T;
      }
    }
  };
}

describe('inbox messages client', () => {
  it('lists inbox messages with runtime guards', async () => {
    const { http, calls } = createHttp([inboxPage]);
    const client = createInboxMessagesClient(http);

    const page = await client.list(1, 20);

    expect(page.items).toHaveLength(1);
    expect(calls[0]?.path).toContain('/api/v1/notifications/my-inbox-messages');
  });

  it('marks a message as read', async () => {
    const readMessage = { ...inboxPage.items[0], status: 'read' as const, readAtUtc: '2026-09-07T01:00:00.000Z' };
    const { http } = createHttp([readMessage]);
    const client = createInboxMessagesClient(http);

    const message = await client.markRead(inboxPage.items[0].id);

    expect(message.status).toBe('read');
  });
});
