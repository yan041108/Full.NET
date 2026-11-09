import type { HostDocumentPreviewTaskResponse, ImportExportTaskDetailResponse } from '@fullnet/client-contracts';
import { outputId } from './data-output-fixtures';

export const importTask: ImportExportTaskDetailResponse = {
  sourceRetention: { expiresAtUtc: '2027-01-01T00:00:00Z', stateKey: 'available', releasedAtUtc: null },
  id: outputId, tenantId: outputId, schemaKey: 'organization.tenant_positions', schemaDisplayName: '租户职位',
  worksheetKey: 'positions', sourceFileId: outputId, sourceFileName: 'positions.xlsx', statusKey: 'preview_succeeded',
  totalRows: 1, validRowCount: 1, invalidRowCount: 0, errorCode: null, requestedByUserId: outputId,
  createdAtUtc: '2026-10-07T00:00:00Z', previewCompletedAtUtc: null, processedRowCount: 0, succeededRowCount: 0,
  executionFailedRowCount: 0, nextLineNumber: 0, executionStartedAtUtc: null, executionCompletedAtUtc: null,
  hasErrorReceipt: false, version: 1, previewRows: []
};
export const documentPreviewTask: HostDocumentPreviewTaskResponse = {
  id: outputId, documentItemId: outputId, documentTitle: '敏感文档', versionId: null, sourceFileId: outputId,
  outputFileId: outputId, statusKey: 'succeeded', providerKey: 'fixture', errorCode: null, requestedByUserId: outputId,
  createdAtUtc: importTask.createdAtUtc, startedAtUtc: null, completedAtUtc: importTask.createdAtUtc, version: 1
};
export const publicDocumentPreviewTask: HostDocumentPreviewTaskResponse = {
  ...documentPreviewTask, versionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297', requestedByUserId: null,
  previewGrant: 'fixture-grant', previewPollUntilUtc: '2099-01-01T00:00:00Z'
};
export const publicShareCapabilities = {
  enabled: true,
  allowedMimeTypes: ['application/msword', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'],
  maxInputBytes: 52428800, maxAttempts: 3, providerKey: 'external_http'
};
