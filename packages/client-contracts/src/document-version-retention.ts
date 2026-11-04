export interface HostDocumentVersionRetentionSettingsResponse {
  minimumRetainedVersionsPerItem: number;
  maximumRetainedHistoryVersions: number;
  pollSeconds: number;
  batchSize: number;
  version: number;
}

export interface UpdateHostDocumentVersionRetentionRequest {
  minimumRetainedVersionsPerItem: number;
  maximumRetainedHistoryVersions: number;
  pollSeconds: number;
  batchSize: number;
  version: number;
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
    && Number.isInteger(value.batchSize)
    && typeof value.version === 'number' && Number.isSafeInteger(value.version) && value.version >= 0;
}

export function isUpdateHostDocumentVersionRetentionRequest(
  value: unknown
): value is UpdateHostDocumentVersionRetentionRequest {
  return isHostDocumentVersionRetentionSettingsResponse(value) && value.version < Number.MAX_SAFE_INTEGER;
}
