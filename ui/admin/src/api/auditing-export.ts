import {
  exportAuditingAccessLogs,
  exportAuditingExceptionLogs,
  exportAuditingOperationLogs,
  type AuditLogExportRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export type AuditLogExportKind = 'access' | 'operation' | 'exception';

export async function exportAuditLogs(
  kind: AuditLogExportKind,
  request: AuditLogExportRequest,
  signal?: AbortSignal
): Promise<Blob> {
  const options = signal ? { signal } : undefined;
  switch (kind) {
    case 'access':
      return exportAuditingAccessLogs(http, request, options);
    case 'operation':
      return exportAuditingOperationLogs(http, request, options);
    case 'exception':
      return exportAuditingExceptionLogs(http, request, options);
    default:
      throw new Error('client.invalid_audit_log_export_kind');
  }
}

export function createDefaultExportRequest(now = new Date()): AuditLogExportRequest {
  return {
    fromUtc: new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString(),
    toUtc: now.toISOString()
  };
}

export type { AuditLogExportRequest };
