import {
  documentHostGetVersionRetentionSettings,
  documentHostUpdateVersionRetentionSettings,
  type HostDocumentVersionRetentionSettingsResponse,
  type UpdateHostDocumentVersionRetentionRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export type HostDocumentVersionRetentionSettings = HostDocumentVersionRetentionSettingsResponse;

/** 与服务端保留策略范围一致，空值、非整数及超限值不能触发清理策略写入。 */
export function isDocumentVersionRetentionInput(value: unknown): value is UpdateHostDocumentVersionRetentionRequest {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const input = value as Record<string, unknown>;
  const inRange = (key: string, minimum: number, maximum: number) =>
    typeof input[key] === 'number' && Number.isInteger(input[key]) && input[key] >= minimum && input[key] <= maximum;
  return inRange('minimumRetainedVersionsPerItem', 1, 1000)
    && inRange('maximumRetainedHistoryVersions', 0, 10000)
    && inRange('pollSeconds', 60, 86400) && inRange('batchSize', 1, 1000);
}

export async function getDocumentVersionRetentionSettings(
  signal?: AbortSignal
): Promise<HostDocumentVersionRetentionSettings> {
  const settings = await documentHostGetVersionRetentionSettings(http, {}, signal);
  if (!isDocumentVersionRetentionInput(settings)) throw new Error('client.invalid_document_version_retention_settings');
  return settings;
}

export async function updateDocumentVersionRetentionSettings(
  body: UpdateHostDocumentVersionRetentionRequest,
  signal?: AbortSignal
): Promise<HostDocumentVersionRetentionSettings> {
  if (!isDocumentVersionRetentionInput(body)) throw new Error('client.invalid_document_version_retention_input');
  // 发送与响应核对共享同一份副本，调用方后续编辑不得迁移本次写入的含义。
  const snapshot = { ...body };
  const settings = await documentHostUpdateVersionRetentionSettings(http, { body: snapshot }, signal);
  if (!isDocumentVersionRetentionInput(settings)
    || (Object.keys(snapshot) as (keyof UpdateHostDocumentVersionRetentionRequest)[]).some(key => settings[key] !== snapshot[key])) {
    throw new Error('client.invalid_document_version_retention_settings');
  }
  return settings;
}
