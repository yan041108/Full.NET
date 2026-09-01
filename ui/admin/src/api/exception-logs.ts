import {

  auditingListHostExceptionLogs,

  isAuditingExceptionLogPage,

  type AuditingExceptionLog,

  type AuditingExceptionLogPage

} from '@fullnet/client-contracts';

import { http } from './http';



/** 分页查询异常日志列表，并对响应页做失败关闭校验。 */
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



/** 导出异常日志明细与分页模型，供异常列表与详情抽屉共享同一数据形状。 */
export type { AuditingExceptionLog, AuditingExceptionLogPage };

