/** 服务端分页结果；items 仅包含当前请求页，total 是相同筛选条件下的总数。 */
export interface PagedResult<T> {
  readonly items: T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}
