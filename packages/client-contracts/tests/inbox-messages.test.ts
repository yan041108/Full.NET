import { describe, expect, it } from 'vitest';
import {
  isInboxMessage,
  isInboxMessagePage,
  isInboxUnreadCount,
  isSendHostInboxMessageRequest
} from '../src/inbox-messages';

describe('inbox-messages contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    title: '系统通知',
    content: '您有一条新消息。',
    status: 'unread',
    readAtUtc: null,
    createdAtUtc: '2026-07-26T00:00:00Z',
    createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
  };

  it('accepts valid inbox payloads', () => {
    expect(isInboxMessage(sample)).toBe(true);
    expect(isInboxMessagePage({
      items: [sample],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isInboxUnreadCount({ unreadCount: 1 })).toBe(true);
    expect(isSendHostInboxMessageRequest({
      recipientUserId: sample.createdByUserId!,
      title: '标题',
      content: '正文'
    })).toBe(true);
  });
});
