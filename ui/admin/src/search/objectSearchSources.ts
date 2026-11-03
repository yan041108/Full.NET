import type { ObjectSearchSource } from './objectSearch';

/** 应用装配来源；管理壳不依赖样例 API，也不因超管通配权限推断可选模块存在。 */
export function createObjectSearchSources(options: {
  tenantId?: string | null;
  permissions: readonly string[];
  routes: ReadonlySet<string>;
  documentLabel: string;
  requestLabel: string;
}): ObjectSearchSource[] {
  const sources: ObjectSearchSource[] = [];
  if (!options.tenantId && options.permissions.includes('document.host_documents.read') && options.routes.has('host-document-items'))
    sources.push({ key: 'document', label: options.documentLabel, routeName: 'host-document-items', queryKey: 'documentId',
      load: async (query, page, signal) => (await import('../api/object-search')).searchHostDocuments(query, page, signal) });
  if (options.tenantId && options.permissions.includes('enterprise_request.enterprise_requests.read') && options.routes.has('enterprise-requests'))
    sources.push({ key: 'enterprise-request', label: options.requestLabel, routeName: 'enterprise-requests', queryKey: 'requestId',
      load: async (query, page, signal) => (await import('../api/object-search')).searchEnterpriseRequests(query, page, signal) });
  return sources;
}
