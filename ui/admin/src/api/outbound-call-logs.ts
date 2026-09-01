import {

  auditingListHostOutboundCallLogs,

  isAuditingOutboundCallLogPage,

  type AuditingOutboundCallLog,

  type AuditingOutboundCallLogPage

} from '@fullnet/client-contracts';

import { http } from './http';



/** 分页查询外呼日志列表，并对响应页做失败关闭校验。 */
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



/** 导出外呼日志明细与分页模型，供链路审计列表和失败详情共用同一契约。 */
export type { AuditingOutboundCallLog, AuditingOutboundCallLogPage };

