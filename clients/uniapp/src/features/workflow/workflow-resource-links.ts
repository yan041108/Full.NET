/** 登录回跳只携带封闭资源种类及标识；URL、租户与人员均不能成为客户端授权事实。 */
export type WorkflowResourceKind = 'workflow_todo' | 'workflow_instance' | 'inbox_message';
export interface WorkflowResourceTarget { readonly resourceTypeKey: WorkflowResourceKind; readonly resourceId: string }
const idPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const routes = { workflow_todo: '/pages/workflow/todo-detail', workflow_instance: '/pages/workflow/instance-detail',
  inbox_message: '/pages/notifications/inbox-detail' } as const;

export function readWorkflowResourceTarget(value: unknown): WorkflowResourceTarget | undefined {
  if (typeof value !== 'object' || value === null) return;
  const candidate = value as Record<string, unknown>;
  const kind = candidate.resourceTypeKey;
  if (kind !== 'workflow_todo' && kind !== 'workflow_instance' && kind !== 'inbox_message') return;
  if (typeof candidate.resourceId !== 'string' || !idPattern.test(candidate.resourceId)) return;
  return { resourceTypeKey: kind, resourceId: candidate.resourceId.toLowerCase() };
}

export function workflowResourceUrl(target: WorkflowResourceTarget): string {
  const safe = readWorkflowResourceTarget(target);
  if (!safe) throw new TypeError('workflow.resource.invalid');
  return `${routes[safe.resourceTypeKey]}?id=${safe.resourceId}`;
}

export function workflowResourceLoginUrl(target: WorkflowResourceTarget): string {
  const safe = readWorkflowResourceTarget(target);
  if (!safe) return '/pages/identity/login';
  return `/pages/identity/login?resourceTypeKey=${safe.resourceTypeKey}&resourceId=${safe.resourceId}`;
}

export function workflowResourcePermission(target: WorkflowResourceTarget): string {
  return { workflow_todo: 'workflow.todos.read', workflow_instance: 'workflow.instances.read',
    inbox_message: 'notifications.inbox.read' }[target.resourceTypeKey];
}

/** H5同一路由更换查询参数时仍须绑定新资源；重复标识或其他页面不借用旧参数。 */
export function workflowResourceIdFromHash(hash: string, kind: WorkflowResourceKind): string | undefined {
  const [path, search = ''] = hash.replace(/^#/, '').split('?', 2);
  if (path !== routes[kind]) return;
  const parameters = new URLSearchParams(search);
  if (parameters.getAll('id').length !== 1) return '';
  return readWorkflowResourceTarget({ resourceTypeKey: kind, resourceId: parameters.get('id') })?.resourceId ?? '';
}
