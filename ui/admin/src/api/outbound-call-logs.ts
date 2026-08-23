import {

  auditingListHostOutboundCallLogs,

  isAuditingOutboundCallLogPage,

  type AuditingOutboundCallLog,

  type AuditingOutboundCallLogPage

} from '@fullnet/client-contracts';

import { http } from './http';



export async function listAuditingOutboundCallLogs(

  page = 1,

  pageSize = 20,

  signal?: AbortSignal

): Promise<AuditingOutboundCallLogPage> {

  const value = await auditingListHostOutboundCallLogs(

    http,

    { page, pageSize },

    signal

  );

  if (!isAuditingOutboundCallLogPage(value)) {

    throw new Error('client.invalid_auditing_outbound_call_log_page');

  }



  return value;

}



export type { AuditingOutboundCallLog, AuditingOutboundCallLogPage };

