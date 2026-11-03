import type { HttpClient, SavedQueryConfiguration } from '@fullnet/client-contracts';

export const enterpriseRequestQueryColumns = ['requestNumber', 'title', 'status', 'totalAmount', 'applicantUserId'] as const;
export const defaultEnterpriseRequestQuery = (): SavedQueryConfiguration => ({ filters: [], sortBy: 'id', descending: false,
  columns: enterpriseRequestQueryColumns.map((columnKey, order) => ({ columnKey, order, visible: true, width: null, fixed: null })) });

/** 只装饰本业务列表的读取，复用生成页面；写操作和详情完全按原契约派发。 */
export function enterpriseRequestQueryHttp(http: HttpClient, current: () => SavedQueryConfiguration): HttpClient {
  return { ...http, request<T>(path: string, init?: RequestInit, signal?: AbortSignal, options?: Parameters<HttpClient['request']>[3]) {
    if ((init?.method ?? 'GET').toUpperCase() === 'GET' && path.split('?')[0]!.replace(/\/$/, '') === '/api/v1/enterprise_request/enterprise-requests') {
      const configuration = current(); const parameters = new URLSearchParams(path.split('?')[1] ?? '');
      if (!['id', 'requestNumber', 'title', 'status'].includes(configuration.sortBy)) throw new TypeError('client.invalid_query_scheme');
      parameters.set('sortBy', configuration.sortBy); parameters.set('descending', String(configuration.descending));
      const fields = new Map([['requestNumber', 'filterRequestNumber'], ['title', 'filterTitle'], ['status', 'filterStatus']]);
      for (const filter of configuration.filters) {
        const parameter = fields.get(filter.fieldKey); if (!parameter || filter.operatorKey !== 'contains') throw new TypeError('client.invalid_query_scheme');
        parameters.set(parameter, filter.value);
      }
      path = '/api/v1/enterprise_request/enterprise-requests?' + parameters;
    }
    return http.request<T>(path, init, signal, options);
  } };
}
