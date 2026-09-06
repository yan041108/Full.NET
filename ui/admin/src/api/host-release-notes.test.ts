import { describe, expect, it, vi } from 'vitest';
import {
  createHostReleaseNote,
  listHostReleaseNotes,
  publishHostReleaseNote
} from './host-release-notes';
import { request } from './http';

vi.mock('./http', () => ({
  request: vi.fn()
}));

const requestMock = vi.mocked(request);

describe('host-release-notes api', () => {
  it('lists release notes with filters', async () => {
    requestMock.mockResolvedValueOnce({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        versionLabel: '1.0.0',
        versionSortKey: 1000000,
        title: 'Initial',
        content: 'Body',
        status: 'draft',
        publishedAtUtc: null,
        publishedByUserId: null,
        retractedAtUtc: null,
        retractedByUserId: null,
        createdAtUtc: '2026-09-06T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostReleaseNotes({
      page: 1,
      pageSize: 20,
      status: 'draft',
      versionLabel: '1.0'
    })).resolves.toMatchObject({ total: 1 });

    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/platform/host-release-notes?page=1&pageSize=20&status=draft&versionLabel=1.0',
      { method: 'GET' },
      undefined
    );
  });

  it('creates and publishes release notes', async () => {
    const note = {
      id: '01912345-6789-7abc-8def-0123456789ab',
      versionLabel: '1.0.0',
      versionSortKey: 1000000,
      title: 'Initial',
      content: 'Body',
      status: 'draft',
      publishedAtUtc: null,
      publishedByUserId: null,
      retractedAtUtc: null,
      retractedByUserId: null,
      createdAtUtc: '2026-09-06T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    requestMock
      .mockResolvedValueOnce(note)
      .mockResolvedValueOnce({ ...note, status: 'published', version: 2 });

    await expect(createHostReleaseNote({
      versionLabel: '1.0.0',
      title: 'Initial',
      content: 'Body'
    })).resolves.toMatchObject({ status: 'draft' });

    await expect(publishHostReleaseNote(note.id, 1)).resolves.toMatchObject({ status: 'published' });
  });
});
