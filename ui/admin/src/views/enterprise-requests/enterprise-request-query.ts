import type { HttpClient, SavedQueryConfiguration } from '@fullnet/client-contracts';
import { enterpriseRequestQueryPath } from '../../api/enterprise-request-aggregate';

export const enterpriseRequestQueryColumns = ['requestNumber', 'title', 'status', 'totalAmount', 'applicantUserId'] as const;
export const defaultEnterpriseRequestQuery = (): SavedQueryConfiguration => ({ filters: [], sortBy: 'id', descending: false,
  columns: enterpriseRequestQueryColumns.map((columnKey, order) => ({ columnKey, order, visible: true, width: null, fixed: null })) });

/** 只装饰本业务列表的读取，复用生成页面；写操作和详情完全按原契约派发。 */
export function enterpriseRequestQueryHttp(http: HttpClient, current: () => SavedQueryConfiguration): HttpClient {
  return { ...http, request<T>(path: string, init?: RequestInit, signal?: AbortSignal, options?: Parameters<HttpClient['request']>[3]) {
    return http.request<T>(enterpriseRequestQueryPath(path, init?.method ?? 'GET', current), init, signal, options);
  } };
}
