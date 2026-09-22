export interface HostDocumentVersionRetentionSettingsResponse {
  minimumRetainedVersionsPerItem: number;
  maximumRetainedHistoryVersions: number;
  pollSeconds: number;
  batchSize: number;
}

export interface UpdateHostDocumentVersionRetentionRequest {
  minimumRetainedVersionsPerItem: number;
  maximumRetainedHistoryVersions: number;
  pollSeconds: number;
  batchSize: number;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isHostDocumentVersionRetentionSettingsResponse(
  value: unknown
): value is HostDocumentVersionRetentionSettingsResponse {
  return isRecord(value)
    && Number.isInteger(value.minimumRetainedVersionsPerItem)
    && Number.isInteger(value.maximumRetainedHistoryVersions)
    && Number.isInteger(value.pollSeconds)
    && Number.isInteger(value.batchSize);
}

export function isUpdateHostDocumentVersionRetentionRequest(
  value: unknown
): value is UpdateHostDocumentVersionRetentionRequest {
  return isHostDocumentVersionRetentionSettingsResponse(value);
}
