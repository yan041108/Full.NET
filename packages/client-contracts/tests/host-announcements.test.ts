import { describe, expect, it } from 'vitest';
import {
  isCreateHostAnnouncementRequest,
  isHostAnnouncement,
  isHostAnnouncementPage,
  isPublishHostAnnouncementRequest,
  isUpdateHostAnnouncementRequest
} from '../src/host-announcements';

describe('host-announcements contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    title: '系统维护通知',
    content: '将于今晚进行维护。',
    status: 'draft',
    publishedAtUtc: null,
    createdAtUtc: '2026-07-26T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  it('accepts valid host announcement payloads', () => {
    expect(isHostAnnouncement(sample)).toBe(true);
    expect(isHostAnnouncementPage({
      items: [sample],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isCreateHostAnnouncementRequest({
      title: '标题',
      content: '正文'
    })).toBe(true);
    expect(isUpdateHostAnnouncementRequest({
      title: '标题',
      content: '正文',
      version: 1
    })).toBe(true);
    expect(isPublishHostAnnouncementRequest({ version: 1 })).toBe(true);
  });

  it('rejects invalid ids', () => {
    expect(isHostAnnouncement({ ...sample, id: 'not-a-guid' })).toBe(false);
  });
});
