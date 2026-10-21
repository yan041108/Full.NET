import { describe, expect, it } from 'vitest';
import { isHostDocumentPreviewTaskPage, isHostDocumentPreviewTaskResponse } from '../src/document-preview-tasks';

describe('document preview requester contracts', () => {
  const id = '01912345-6789-7abc-8def-0123456789ab';
  const task = {
    id, documentItemId: id, documentTitle: 'Preview', versionId: null,
    sourceFileId: id, outputFileId: null, statusKey: 'pending', providerKey: 'disabled',
    errorCode: null, requestedByUserId: null, createdAtUtc: '2026-10-20T00:00:00Z',
    startedAtUtc: null, completedAtUtc: null, version: 1,
  };

  it('accepts anonymous and authenticated tasks in the same management page', () => {
    expect(isHostDocumentPreviewTaskResponse(task)).toBe(true);
    expect(isHostDocumentPreviewTaskPage({
      items: [task, { ...task, requestedByUserId: id }], page: 1, pageSize: 20, total: 2,
    })).toBe(true);
  });

  it('rejects a malformed or missing requester instead of treating it as anonymous', () => {
    expect(isHostDocumentPreviewTaskResponse({ ...task, requestedByUserId: 'invalid' })).toBe(false);
    expect(isHostDocumentPreviewTaskResponse({ ...task, requestedByUserId: undefined })).toBe(false);
  });
});
