export type WorkflowBusinessDetailRoute = {
  routeName: string;
  idQueryKey: string;
};

/** 可信业务类型到后台路由的白名单映射；禁止接受任意 URL。 */
const workflowBusinessDetailByType = new Map<string, WorkflowBusinessDetailRoute & { permissions: readonly string[] }>([
  ['data_approval.serial_rule.update', {
    routeName: 'data-approval-requests',
    idQueryKey: 'requestId',
    permissions: ['data_approvals.requests.read']
  }],
  ['demo.enterprise_request', {
    routeName: 'enterprise-requests',
    idQueryKey: 'requestId',
    permissions: ['enterprise_request.enterprise_requests.read', 'enterprise_request.enterprise_request_lines.read']
  }]
]);

/** 解析业务类型对应的可信详情导航。 */
export function findWorkflowBusinessDetailRoute(
  businessType: string | null | undefined,
  hasPermission: (permission: string) => boolean
): WorkflowBusinessDetailRoute | undefined {
  const normalized = businessType?.trim();
  if (normalized === undefined || normalized.length === 0) {
    return undefined;
  }
  const route = workflowBusinessDetailByType.get(normalized);
  return route && route.permissions.every(hasPermission)
    ? { routeName: route.routeName, idQueryKey: route.idQueryKey } : undefined;
}

/** 列表与详情优先展示启动时写入的业务标题快照。 */
export function formatWorkflowBusinessLabel(
  businessTitle: string | null | undefined,
  businessType: string,
  businessId: string
): string {
  const title = businessTitle?.trim();
  if (title !== undefined && title.length > 0) {
    return title;
  }
  return `${businessType} · ${businessId}`;
}
