import { expect, test } from '@playwright/test';

const user = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const id = '01912345-6789-7abc-8def-0123456789ab';
const todoId = '01912345-6789-7abc-8def-0123456789ae';
const form = '01912345-6789-7abc-8def-0123456789ad';
const at = '2026-10-16T00:00:00Z';
const permissions = ['workflow.instances.read', 'workflow.instances.list', 'workflow.instances.pause',
  'workflow.instances.resume', 'workflow.instances.cancel', 'workflow.instances.recover',
  'workflow.todos.read', 'workflow.todos.approve', 'workflow.todos.reject'];
const instance = { id, definitionVersionId: '01912345-6789-7abc-8def-0123456789ac', formVersionId: form,
  businessTitle: '冻结审批单', businessType: 'demo.enterprise_request',
  businessId: '01912345-6789-7abc-8def-0123456789af', statusKey: 'active', revision: 3,
  activeTodoId: todoId, startedAtUtc: at };
const row = { id, definitionVersionId: instance.definitionVersionId, definitionKey: 'request.approval',
  businessTitle: instance.businessTitle, businessType: instance.businessType, businessId: instance.businessId,
  statusKey: 'active', startedById: user, startedAtUtc: at, completedAtUtc: null };
const todoRow = { id: todoId, instanceId: id, stepId: '01912345-6789-7abc-8def-0123456789b1',
  statusKey: 'active', arrivedAtUtc: at, completedAtUtc: null, resultActionKey: null, revision: 3,
  businessTitle: instance.businessTitle, businessType: instance.businessType, businessId: instance.businessId,
  instanceStatusKey: 'active', definitionKey: row.definitionKey, nodeKey: 'manager' };
const detail = { id: todoId, instanceId: id, stepId: todoRow.stepId, assigneeUserId: user,
  statusKey: 'active', revision: 3, formVersionId: form, formSchemaHash: 'a'.repeat(64),
  formSchema: { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'request', fields: [
    { fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }] }] },
  submission: { summary: '申请' }, fieldPolicies: { summary: 'required' }, submissionRevision: 4,
  approvalModeKey: 'single', requiredApprovalCount: 1, approvedCount: 0, rejectedCount: 0, pendingCount: 1 };
const log = { id: '01912345-6789-7abc-8def-0123456789b2', instanceId: id, stepId: null,
  transitionKey: 'instance.started', fromStatusKey: null, toStatusKey: 'active', createdAtUtc: at };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
const unavailable = route => json(route, { type: 'about:blank', status: 503, code: 'fixture.unavailable', title: '测试服务暂不可用' }, 503);

// 只控制 HTTP 与身份快照；实际路由、状态门槛、表单、确认框及点击均使用真实 Vue。
async function setup(page, granted = permissions) {
  const state = { permissions: [...granted], instance: { ...instance }, writes: [], readFailures: 0,
    failReads: false, failLogOnce: false, lists: [], write: async route => unavailable(route) };
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'fixture-access', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
    if (path === '/api/v1/me') return json(route, { id: user, username: 'fixture', displayName: '夹具', tenantId: null,
      actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false,
      permissions: state.permissions, sessionId: user, preferredLocale: 'zh-CN', profileVersion: 1 });
    if (path === '/api/v1/navigation') return json(route, ['instances', 'todos'].map((kind, index) => ({
      id: 'workflow-' + kind, parentId: null, routeName: 'workflow-' + kind, path: '/workflow/' + kind,
      componentKey: 'workflow-' + kind, title: kind === 'instances' ? '工作流实例' : '我的待办',
      caption: '', icon: 'document', order: index, requiredPermission: 'workflow.' + kind + '.read', children: [] })));
    if (request.method() === 'POST' && path.startsWith('/api/v1/workflow/')) {
      state.writes.push({ path, body: request.postDataJSON() }); return state.write(route, path);
    }
    const catalog = ['/api/v1/workflow/instances/mine', '/api/v1/workflow/instances', '/api/v1/workflow/todos/mine'];
    if (catalog.includes(path)) {
      state.lists.push({ path, query: Object.fromEntries(url.searchParams) });
      if (state.failReads) { state.readFailures++; return unavailable(route); }
      const items = path.includes('/todos/') ? [todoRow] : [{ ...row, statusKey: state.instance.statusKey }];
      return json(route, { items, total: 1, page: Number(url.searchParams.get('page') ?? 1), pageSize: Number(url.searchParams.get('pageSize') ?? 20) });
    }
    if (path === `/api/v1/workflow/instances/${id}`) {
      if (state.failReads) { state.readFailures++; return unavailable(route); }
      return json(route, state.instance);
    }
    if (path === `/api/v1/workflow/instances/${id}/execution-logs`) {
      if (state.failReads || state.failLogOnce) { state.failLogOnce = false; state.readFailures++; return unavailable(route); }
      return json(route, [log]);
    }
    if (path === `/api/v1/workflow/todos/${todoId}/runtime`) return json(route, detail);
    return route.fulfill({ status: 404 });
  });
  return state;
}
async function openInstance(page) {
  await page.getByTestId('workflow-instance-list-item').first().click();
  await expect(page.getByTestId('workflow-instance-summary')).toBeVisible();
}
async function openTodo(page) {
  await page.getByTestId('workflow-todo-open').first().click();
  await expect(page.getByTestId('workflow-todo-approve')).toBeVisible();
}
async function confirm(page) {
  const box = page.locator('.el-message-box'); await expect(box).toBeVisible();
  await box.locator('.el-message-box__btns .el-button--primary').click(); await expect(box).toBeHidden();
}
async function pause(page) { await page.getByTestId('workflow-instance-pause').click(); await confirm(page); }
async function unlock(page, kind) {
  await page.getByTestId(`workflow-${kind}-unlock`).click(); await confirm(page);
}
async function refreshIdentity(page) {
  // 用生产 Store 的 HTTP 刷新入口模拟外部权限快照到达，不修改页面实例或恢复账本。
  await page.evaluate(async () => { const { useSessionStore } = await import('/src/auth/session.ts'); await useSessionStore().reloadContext(); });
}
function acknowledgeSecondWrite(state) {
  state.write = route => {
    if (state.writes.length === 1) return unavailable(route);
    state.instance = { ...instance, revision: 4, statusKey: state.writes.at(-1).path.endsWith('/pause') ? 'suspended' : 'active' };
    return json(route, state.instance);
  };
}

test('实例只读权限不能显示管理写入入口', async ({ page }) => {
  const state = await setup(page, ['workflow.instances.read']); await page.goto('/#/workflow/instances'); await openInstance(page);
  await expect(page.getByTestId('workflow-instance-pause')).toHaveCount(0);
  await expect(page.getByTestId('workflow-instance-reassign')).toHaveCount(0); expect(state.writes).toHaveLength(0);
});

test('实例未知写入跨同会话路由后仍须重读核实，手动重试复用原键', async ({ page }) => {
  const state = await setup(page); acknowledgeSecondWrite(state); await page.goto('/#/workflow/instances'); await openInstance(page); await pause(page);
  await expect(page.getByTestId('workflow-instance-pause')).toHaveCount(0);
  const previousLists = state.lists.length;
  await page.evaluate(() => { location.hash = '/workflow/instances?reopen=1'; });
  await expect.poll(() => state.lists.length).toBe(previousLists + 1); await openInstance(page);
  await expect(page.getByTestId('workflow-instance-pause')).toHaveCount(0); await expect(page.getByTestId('workflow-instance-unlock')).toBeEnabled();
  await unlock(page, 'instance'); expect(state.writes).toHaveLength(1);
  await pause(page); await expect.poll(() => state.writes.length).toBe(2);
  await expect(page.getByTestId('workflow-instance-receipt')).toContainText('操作已确认');
  expect(state.writes[1].body.idempotencyKey).toBe(state.writes[0].body.idempotencyKey);
});

test('实例确认成功后详情轨迹与目录失败仍保留成功回执', async ({ page }) => {
  const state = await setup(page); state.write = route => { state.failReads = true; return json(route, { ...instance, revision: 4, statusKey: 'suspended' }); };
  await page.goto('/#/workflow/instances'); await openInstance(page); await pause(page);
  await expect.poll(() => state.readFailures).toBe(3); await expect(page.getByTestId('workflow-instance-receipt')).toContainText('操作已确认');
  await expect(page.getByTestId('workflow-instance-pause')).toHaveCount(0); expect(state.writes).toHaveLength(1);
});

test('实例轨迹错误可独立重试且有效详情仍可使用', async ({ page }) => {
  const state = await setup(page); state.failLogOnce = true; await page.goto('/#/workflow/instances'); await openInstance(page);
  await expect(page.getByTestId('workflow-instance-pause')).toBeEnabled(); await page.getByTestId('workflow-instance-reload-logs').click();
  await expect(page.getByTestId('workflow-execution-log')).toHaveCount(1); expect(state.writes).toHaveLength(0);
});

test('待办未知结果关闭重开保留门槛，确认不重发且人工重试复用原键', async ({ page }) => {
  const state = await setup(page); acknowledgeSecondWrite(state); await page.goto('/#/workflow/todos'); await openTodo(page);
  await page.getByTestId('workflow-todo-approve').click(); await expect(page.getByTestId('workflow-todo-approve')).toBeDisabled();
  await page.locator('.el-drawer__close-btn').click(); await expect(page.getByRole('dialog')).toBeHidden(); await openTodo(page);
  await expect(page.getByTestId('workflow-todo-approve')).toBeDisabled(); await unlock(page, 'todo'); expect(state.writes).toHaveLength(1);
  await page.getByTestId('workflow-todo-approve').click(); await expect.poll(() => state.writes.length).toBe(2);
  await expect(page.getByTestId('workflow-todo-receipt')).toContainText('操作已确认');
  expect(state.writes[1].body.idempotencyKey).toBe(state.writes[0].body.idempotencyKey);
});

test('待办成功后列表失败保持成功回执并关闭旧详情', async ({ page }) => {
  const state = await setup(page); state.write = route => { state.failReads = true; return json(route, { ...instance, revision: 4 }); };
  await page.goto('/#/workflow/todos'); await openTodo(page); await page.getByTestId('workflow-todo-approve').click();
  await expect.poll(() => state.readFailures).toBe(1); await expect(page.getByTestId('workflow-todo-receipt')).toContainText('操作已确认');
  await expect(page.getByRole('dialog')).toBeHidden(); expect(state.writes).toHaveLength(1);
});

for (const kind of ['instance', 'todo']) {
  test(`${kind} 同会话权限撤回恢复保留未知门槛与幂等键`, async ({ page }) => {
    const state = await setup(page); acknowledgeSecondWrite(state); const isInstance = kind === 'instance';
    await page.goto('/#/workflow/' + (isInstance ? 'instances' : 'todos'));
    if (isInstance) { await openInstance(page); await pause(page); }
    else { await openTodo(page); await page.getByTestId('workflow-todo-approve').click(); }
    const action = page.getByTestId(`workflow-${kind}-${isInstance ? 'pause' : 'approve'}`);
    if (isInstance) await expect(action).toHaveCount(0); else await expect(action).toBeDisabled();
    state.permissions = permissions.filter(value => !value.endsWith('.pause') && !value.endsWith('.approve'));
    await refreshIdentity(page); await expect(action).toHaveCount(0);
    state.permissions = [...permissions]; await refreshIdentity(page);
    if (isInstance) await openInstance(page); else await openTodo(page);
    if (isInstance) await expect(action).toHaveCount(0); else await expect(action).toBeDisabled();
    await expect(page.getByTestId(`workflow-${kind}-unlock`)).toBeEnabled();
    await unlock(page, kind); expect(state.writes).toHaveLength(1);
    if (isInstance) await pause(page); else await action.click();
    await expect.poll(() => state.writes.length).toBe(2);
    await expect(page.getByTestId(`workflow-${kind}-receipt`)).toContainText('操作已确认');
    expect(state.writes[1].body.idempotencyKey).toBe(state.writes[0].body.idempotencyKey);
  });
}
