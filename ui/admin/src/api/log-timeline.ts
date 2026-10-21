import { auditingQueryLogTimeline, isLogTimelineResponse, type LogTimelineQuery } from '@fullnet/client-contracts';
import { http } from './http';

/** 有界日志时间线；响应已通过共享契约守卫。 */
export async function getLogTimeline(query: LogTimelineQuery, signal?: AbortSignal) {
  const result = await auditingQueryLogTimeline(http, query, signal);
  if (!isLogTimelineResponse(result)) throw new Error('client.invalid_log_timeline');
  return result;
}
