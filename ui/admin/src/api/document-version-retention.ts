import {
  documentHostGetVersionRetentionSettings,
  documentHostUpdateVersionRetentionSettings,
  type HostDocumentVersionRetentionSettingsResponse,
  type UpdateHostDocumentVersionRetentionRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export type HostDocumentVersionRetentionSettings = HostDocumentVersionRetentionSettingsResponse;

export async function getDocumentVersionRetentionSettings(
  signal?: AbortSignal
): Promise<HostDocumentVersionRetentionSettings> {
  return documentHostGetVersionRetentionSettings(http, {}, signal);
}

export async function updateDocumentVersionRetentionSettings(
  body: UpdateHostDocumentVersionRetentionRequest,
  signal?: AbortSignal
): Promise<HostDocumentVersionRetentionSettings> {
  return documentHostUpdateVersionRetentionSettings(http, { body }, signal);
}
