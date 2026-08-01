import {
  isAuditingOutboundCallLogPage,
  type AuditingOutboundCallLog,
  type AuditingOutboundCallLogPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAuditingOutboundCallLogs(
  page = 1,
  pageSize = 20
): Promise<AuditingOutboundCallLogPage> {
  const value = await request<unknown>(
    `/api/v1/auditing/outbound-call-logs?page=${page}&pageSize=${pageSize}`
  );
  if (!isAuditingOutboundCallLogPage(value)) {
    throw new Error('client.invalid_auditing_outbound_call_log_page');
  }
  return value;
}

export type { AuditingOutboundCallLog, AuditingOutboundCallLogPage };
