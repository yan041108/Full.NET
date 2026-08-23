import {

  auditingListHostExceptionLogs,

  isAuditingExceptionLogPage,

  type AuditingExceptionLog,

  type AuditingExceptionLogPage

} from '@fullnet/client-contracts';

import { http } from './http';



export async function listAuditingExceptionLogs(

  page = 1,

  pageSize = 20,

  signal?: AbortSignal

): Promise<AuditingExceptionLogPage> {

  const value = await auditingListHostExceptionLogs(

    http,

    { page, pageSize },

    signal

  );

  if (!isAuditingExceptionLogPage(value)) {

    throw new Error('client.invalid_auditing_exception_log_page');

  }



  return value;

}



export type { AuditingExceptionLog, AuditingExceptionLogPage };

