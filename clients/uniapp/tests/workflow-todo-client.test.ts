import { describe, expect, it, vi } from 'vitest';
import workflowFixture from '../../../packages/client-contracts/src/fixtures/workflow-form-schema-v1.json';
import type { HttpClient } from '../src/api/http';
import { createWorkflowTodoClient } from '../src/features/workflow/workflow-todo-client';
import { HttpProblem } from '../src/api/problem-details';
import type { WorkflowSchemaCache } from '../src/features/workflow/workflow-schema-cache';

const todoId = '01912345-6789-7abc-8def-0123456789ab';
const formVersionId = '01912345-6789-7abc-8def-0123456789af';

function todoDetail() {
  return {
    id: todoId,
    instanceId: '01912345-6789-7abc-8def-0123456789ac',
    stepId: '01912345-6789-7abc-8def-0123456789ad',
    assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
    statusKey: 'active',
    revision: 3,
    formVersionId,
    formSchemaHash: workflowFixture.contentHash,
    allowedActions: ['approve', 'reject', 'return', 'countersign', 'transfer', 'remind'], signRequired: false, commentRequired: false,
    formSchema: workflowFixture.formSchema,
    submission: workflowFixture.submission,
    fieldPolicies: workflowFixture.fieldPolicies,
    submissionRevision: 4,
    approvalModeKey: 'single',
    requiredApprovalCount: 1,
    approvedCount: 0,
    rejectedCount: 0,
    pendingCount: 1
  };
}

describe('workflow todo client', () => {
  it('人工管理候选、历史与退回目标是真实分页，缺失代表人的多人目标可用', async () => {
    const target = { stepId: formVersionId, nodeKey: 'group', assigneeUserId: null, completedAtUtc: '2026-10-26T00:00:00Z' };
    const http: HttpClient = { request: vi.fn().mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 21 })
      .mockResolvedValueOnce([target]).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 21 }) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    await client.listManagementCandidates(todoId, 'transfer', 2); expect(await client.listReturnTargets(todoId, 2)).toEqual([target]); await client.listHistory(2);
    expect(http.request).toHaveBeenNthCalledWith(1, { path: `/api/v1/workflow/todos/${todoId}/transfer-candidates?page=2&pageSize=20` });
    expect(http.request).toHaveBeenNthCalledWith(2, { path: `/api/v1/workflow/todos/${todoId}/return-targets?page=2&pageSize=20` });
    expect(http.request).toHaveBeenNthCalledWith(3, { path: '/api/v1/workflow/todos/mine/history?page=2&pageSize=20' });
    await expect(client.listManagementCandidates(todoId, 'delegate' as never, 1)).rejects.toThrow();
    await expect(client.listReturnTargets(todoId, 0)).rejects.toThrow(); expect(http.request).toHaveBeenCalledTimes(3);
  });
  it('只有明确的加签链不存在代码可以转换为空，其他404保持失败', async () => {
    const http: HttpClient = { request: vi.fn().mockRejectedValueOnce(new HttpProblem({ status: 404, code: 'workflow.todo.countersign_chain_not_found' }))
      .mockRejectedValueOnce(new HttpProblem({ status: 404, code: 'workflow.todo.not_found' })) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    expect(await client.countersignChain(todoId)).toBeNull(); await expect(client.countersignChain(todoId)).rejects.toBeInstanceOf(HttpProblem);
  });
  it('转办、退回、加签和取消只投影固定动作字段，不转发额外身份', async () => {
    const response = { id: todoId, definitionVersionId: formVersionId, formVersionId, businessType: 'human', businessId: 'one', businessTitle: null,
      statusKey: 'active', revision: 4, activeTodoId: null, startedAtUtc: '2026-10-26T00:00:00Z' };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(response) }; const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    const common = { expectedRevision: 3, comment: '管理', idempotencyKey: 'human-action' };
    await client.transfer(todoId, { ...common, expectedFormRevision: 4, assigneeUserId: formVersionId, tenantId: todoId } as never);
    await client.returnTodo(todoId, { ...common, expectedFormRevision: 4, fieldPatch: {}, targetStepId: formVersionId, actorUserId: todoId } as never);
    await client.countersign(todoId, { ...common, directionKey: 'before', assigneeUserIds: [formVersionId], actorUserId: todoId } as never);
    await client.cancelCountersign(todoId, { ...common, tenantId: todoId } as never);
    for (const call of vi.mocked(http.request).mock.calls) { const request = call[0]; expect(request.method).toBe('POST'); expect(request.data).not.toHaveProperty('tenantId'); expect(request.data).not.toHaveProperty('actorUserId'); }
    expect(vi.mocked(http.request).mock.calls[2]![0].data).toEqual({ ...common, directionKey: 'before', assigneeUserIds: [formVersionId] });
  });
  it('代理候选和本人委托历史使用真实20条第二页并拒绝非法页码', async () => {
    const candidate = { items: [{ id: formVersionId, username: 'proxy', displayName: 'Proxy' }], page: 2, pageSize: 20, total: 21 };
    const http: HttpClient = { request: vi.fn().mockResolvedValueOnce(candidate).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 0 }) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    expect(await client.listDelegationCandidates(todoId, 2)).toEqual(candidate);
    expect(http.request).toHaveBeenNthCalledWith(1, { path: `/api/v1/workflow/todos/${todoId}/delegation-candidates?page=2&pageSize=20` });
    expect((await client.listDelegated(2)).page).toBe(2);
    expect(http.request).toHaveBeenNthCalledWith(2, { path: '/api/v1/workflow/todos/mine/history?resultActionKey=delegate&page=2&pageSize=20' });
    await expect(client.listDelegated(0)).rejects.toThrow('workflow.todo.invalid-page');
    await expect(client.listDelegationCandidates(todoId, 1.5)).rejects.toThrow('workflow.todo.invalid-page');
    expect(http.request).toHaveBeenCalledTimes(2);
  });
  it.each(['delegate', 'recallDelegation'] as const)('移动交接%s仅发送固定修订和授权动作证据', async action => {
    const response = { id: todoId, definitionVersionId: formVersionId, formVersionId, businessType: 'delegate', businessId: 'one',
      businessTitle: null, statusKey: 'active', revision: 4, activeTodoId: formVersionId, startedAtUtc: '2026-10-25T00:00:00Z' };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(response) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    const common = { expectedRevision: 2, expectedFormRevision: 3, comment: '交接', idempotencyKey: 'mobile-handoff' };
    if (action === 'delegate') await client.delegate(todoId, { ...common, assigneeUserId: formVersionId });
    else await client.recallDelegation(todoId, common);
    expect(http.request).toHaveBeenCalledWith({ path: `/api/v1/workflow/todos/${todoId}/${action === 'delegate' ? 'delegate' : 'delegation/recall'}`,
      method: 'POST', data: action === 'delegate' ? { ...common, assigneeUserId: formVersionId } : common });
  });
  it('移动端代理状态使用共享业务守卫，关闭状态不能继续标记可收回', async () => {
    const http: HttpClient = { request: vi.fn().mockResolvedValueOnce({ delegation: null }).mockResolvedValueOnce({ delegation: {
      originTodoId: todoId, proxyTodoId: formVersionId, originalAssigneeUserId: '01912345-6789-7abc-8def-0123456789a1',
      proxyUserId: '01912345-6789-7abc-8def-0123456789a2', statusKey: 'completed', proxyRevision: 2, formRevision: 3, canRecall: true,
      createdAtUtc: '2026-10-25T00:00:00Z' } }) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    expect(await client.delegation(todoId)).toBeNull(); await expect(client.delegation(todoId)).rejects.toThrow('client.invalid_workflow_todo_delegation');
  });
  it('新增独立代理动作不会使已有移动办理详情失败', async () => {
    const value = { ...todoDetail(), activityKindKey: 'human.approval', allowedActions: ['approve', 'delegate'] };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(value) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    expect((await client.get(todoId)).allowedActions).toEqual(['approve', 'delegate']);
  });
  it('附件按个人Todo请求原始字节，错误形状和非法标识失败关闭', async () => {
    const bytes = new Uint8Array([137, 80, 78, 71]).buffer;
    const http: HttpClient = { request: vi.fn().mockResolvedValue(bytes) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    expect(await client.downloadAttachment(todoId, formVersionId)).toBe(bytes);
    expect(http.request).toHaveBeenCalledWith({ path: `/api/v1/workflow/todos/${todoId}/form-attachments/${formVersionId}/content`,
      responseType: 'arraybuffer', headers: { Accept: 'application/octet-stream' } });
    vi.mocked(http.request).mockResolvedValue({ content: 'unsafe' });
    await expect(client.downloadAttachment(todoId, formVersionId)).rejects.toThrow();
    await expect(client.downloadAttachment('https://foreign.test', formVersionId)).rejects.toThrow();
  });
  it('领取不允许覆盖候选或租户，释放绑定三个独立修订', async () => {
    const response = { id: todoId, definitionVersionId: formVersionId, formVersionId, businessType: 'claim', businessId: 'one', businessTitle: null,
      statusKey: 'active', revision: 2, activeTodoId: null, startedAtUtc: '2026-10-25T00:00:00Z' };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(response) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    await client.claim(todoId, { expectedRevision: 1, idempotencyKey: 'claim-one' });
    expect(http.request).toHaveBeenLastCalledWith({ path: `/api/v1/workflow/claim-pools/${todoId}/claim`, method: 'POST', data: { expectedRevision: 1, idempotencyKey: 'claim-one' } });
    await client.release(todoId, { expectedRevision: 1, expectedPoolRevision: 2, expectedFormRevision: 3, idempotencyKey: 'release-one' });
    expect(http.request).toHaveBeenLastCalledWith({ path: `/api/v1/workflow/todos/${todoId}/release`, method: 'POST', data: { expectedRevision: 1,
      expectedPoolRevision: 2, expectedFormRevision: 3, idempotencyKey: 'release-one' } });
    await expect(client.claim(todoId, { expectedRevision: 1, idempotencyKey: 'bad', tenantId: todoId } as never)).rejects.toThrow();
  });
  it('领取办理的独立池修订与真实活动类型保留到缓存投影', async () => {
    const detail = { ...todoDetail(), activityKindKey: 'human.claim', approvalModeKey: 'none', requiredApprovalCount: 0, pendingCount: 0,
      allowedActions: ['complete', 'release'], claimProgress: { poolStatusKey: 'claimed', poolRevision: 2, claimCycle: 1, allowRelease: true } };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(detail) };
    const client = createWorkflowTodoClient(http, { read: vi.fn().mockReturnValue(workflowFixture.formSchema), write: vi.fn() });
    const result = await client.get(todoId); expect(result.claimProgress?.poolRevision).toBe(2); expect(result.activityKindKey).toBe('human.claim');
  });
  it('投票请求只发送独立选票修订、选择和个人值', async () => {
    const response = { id: todoId, definitionVersionId: formVersionId, formVersionId, businessType: 'vote', businessId: 'one', businessTitle: null,
      statusKey: 'active', revision: 2, activeTodoId: null, startedAtUtc: '2026-10-25T00:00:00Z' };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(response) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    const body = { expectedRevision: 1, expectedBallotRevision: 3, choiceKey: 'yes' as const, values: { title: 'private' }, idempotencyKey: 'ballot-one' };
    await client.vote(todoId, body);
    expect(http.request).toHaveBeenCalledWith({ path: `/api/v1/workflow/todos/${todoId}/vote`, method: 'POST', data: body });
    await expect(client.vote(todoId, { ...body, expectedFormRevision: 8 } as typeof body)).rejects.toThrow('workflow.todo.invalid-vote');
    expect(http.request).toHaveBeenCalledTimes(1);
  });
  it('guards todo detail and caches only the visible immutable schema', async () => {
    const http: HttpClient = {
      request: vi.fn().mockResolvedValue(todoDetail())
    };
    const cache: WorkflowSchemaCache = {
      read: vi.fn(),
      write: vi.fn()
    };
    const client = createWorkflowTodoClient(http, cache);

    const detail = await client.get(todoId);

    expect(detail.formSchemaHash).toBe(workflowFixture.contentHash);
    expect(http.request).toHaveBeenCalledWith({
      path: `/api/v1/workflow/todos/${todoId}/runtime`
    });
    expect(cache.write).toHaveBeenCalledWith(
      formVersionId,
      workflowFixture.contentHash,
      detail.formSchema
    );
  });

  it('fails closed when the API returns an invalid schema hash', async () => {
    const http: HttpClient = {
      request: vi.fn().mockResolvedValue({
        ...todoDetail(),
        formSchemaHash: 'invalid'
      })
    };
    const cache: WorkflowSchemaCache = {
      read: vi.fn(),
      write: vi.fn()
    };

    await expect(createWorkflowTodoClient(http, cache).get(todoId))
      .rejects.toThrow('workflow.todo.invalid-detail');
    expect(cache.write).not.toHaveBeenCalled();
  });

  it.each(['approve', 'complete'] as const)('sends %s without adding tenant or authority fields', async action => {
    const http: HttpClient = {
      request: vi.fn().mockResolvedValue({
        id: '01912345-6789-7abc-8def-0123456789b0',
        definitionVersionId: '01912345-6789-7abc-8def-0123456789b1',
        formVersionId,
        businessType: 'leave',
        businessId: 'leave-1',
        businessTitle: null,
        statusKey: 'completed',
        revision: 4,
        activeTodoId: null,
        startedAtUtc: '2026-08-30T00:00:00Z'
      })
    };
    const cache: WorkflowSchemaCache = { read: vi.fn(), write: vi.fn() };

    await createWorkflowTodoClient(http, cache)[action](todoId, {
      expectedRevision: 3,
      expectedFormRevision: 4,
      fieldPatch: { title: 'ready' },
      comment: 'ok',
      idempotencyKey: `mobile-${action}-1`
    });

    expect(http.request).toHaveBeenCalledWith({
      path: `/api/v1/workflow/todos/${todoId}/${action}`,
      method: 'POST',
      data: {
        expectedRevision: 3,
        expectedFormRevision: 4,
        fieldPatch: { title: 'ready' },
        comment: 'ok',
        idempotencyKey: `mobile-${action}-1`
      }
    });
  });

  it('reads independent task progress and rejects a task response carrying approval authority', async () => {
    const task = { ...todoDetail(), activityKindKey: 'human.task', approvalModeKey: 'none', requiredApprovalCount: 0,
      approvedCount: 0, rejectedCount: 0, pendingCount: 0, allowedActions: ['complete'],
      taskProgress: { completionModeKey: 'all', memberCount: 3, requiredCompletionCount: 3, completedCount: 1, pendingCount: 2, cancelledCount: 0 } };
    const http: HttpClient = { request: vi.fn().mockResolvedValue(task) };
    const cache: WorkflowSchemaCache = { read: vi.fn(), write: vi.fn() };
    const client = createWorkflowTodoClient(http, cache);
    expect((await client.get(todoId)).taskProgress?.completedCount).toBe(1);
    vi.mocked(http.request).mockResolvedValue({ ...task, allowedActions: ['approve'] });
    await expect(client.get(todoId)).rejects.toThrow('workflow.todo.invalid-detail');
    expect(cache.write).toHaveBeenCalledTimes(1);
  });
});
