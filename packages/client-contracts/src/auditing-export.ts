import type { HttpClient, RequestOptions } from './http.js';

export interface AuditLogExportRequest {
  fromUtc: string;
  toUtc: string;
  httpMethod?: string | null;
  statusCode?: number | null;
  succeeded?: boolean | null;
  pathContains?: string | null;
  exceptionTypeContains?: string | null;
}

const workbookAccept =
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

function postExport(
  http: HttpClient,
  path: string,
  request: AuditLogExportRequest,
  options?: RequestOptions
): Promise<Blob> {
  return http.requestBlob(
    path,
    {
      method: 'POST',
      headers: {
        accept: workbookAccept,
        'content-type': 'application/json'
      },
      body: JSON.stringify(request)
    },
    options?.signal,
    options
  );
}

export function exportAuditingAccessLogs(
  http: HttpClient,
  request: AuditLogExportRequest,
  options?: RequestOptions
): Promise<Blob> {
  return postExport(http, '/api/v1/auditing/access-logs/exports', request, options);
}

export function exportAuditingOperationLogs(
  http: HttpClient,
  request: AuditLogExportRequest,
  options?: RequestOptions
): Promise<Blob> {
  return postExport(http, '/api/v1/auditing/operation-logs/exports', request, options);
}

export function exportAuditingExceptionLogs(
  http: HttpClient,
  request: AuditLogExportRequest,
  options?: RequestOptions
): Promise<Blob> {
  return postExport(http, '/api/v1/auditing/exception-logs/exports', request, options);
}
