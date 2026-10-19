import { queryLogTimeline, type LogTimelineQuery } from '@fullnet/client-contracts';
import { http } from './http';

/** 有界日志时间线；响应已通过共享契约守卫。 */
export const getLogTimeline = (query: LogTimelineQuery, signal?: AbortSignal) => queryLogTimeline(http, query, signal);
