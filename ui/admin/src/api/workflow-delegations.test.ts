import { beforeEach, expect, it, vi } from 'vitest';
import { delegateWorkflowTodo, getWorkflowTodoDelegation, listWorkflowTodoDelegationCandidates, recallWorkflowTodoDelegation } from './workflow-delegations';
import { http } from './http';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const origin = '01912345-6789-7abc-8def-0123456789a1'; const proxy = '01912345-6789-7abc-8def-0123456789a2';
const actor = '01912345-6789-7abc-8def-0123456789a3'; const target = '01912345-6789-7abc-8def-0123456789a4';
const receipt = { id: origin, definitionVersionId: actor, formVersionId: actor, businessType: 'delegation', businessId: 'one',
  businessTitle: null, statusKey: 'active', revision: 4, activeTodoId: proxy, startedAtUtc: '2026-10-25T00:00:00Z' };
const grant = { originTodoId: origin, proxyTodoId: proxy, originalAssigneeUserId: actor, proxyUserId: target,
  statusKey: 'active', proxyRevision: 2, formRevision: 3, canRecall: true, createdAtUtc: '2026-10-25T00:00:00Z' };
beforeEach(() => vi.mocked(http.request).mockReset());

it('代理提交只含当前待办目标与固定证据，不夹带身份或字段补丁', async () => {
  vi.mocked(http.request).mockResolvedValue(receipt);
  expect(await delegateWorkflowTodo(origin, target, 2, 3, '委托', 'grant-key', actor)).toEqual(receipt);
  expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/todos/${origin}/delegate`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ assigneeUserId: target,
      expectedRevision: 2, expectedFormRevision: 3, comment: '委托', idempotencyKey: 'grant-key', signatureFileId: actor })
  }, undefined);
});
it('收回按原待办和服务端最新修订提交，不接受指定代理身份', async () => {
  vi.mocked(http.request).mockResolvedValue(receipt);
  expect(await recallWorkflowTodoDelegation(origin, 7, 9, '收回', 'recall-key')).toEqual(receipt);
  expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/todos/${origin}/delegation/recall`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ expectedRevision: 7,
      expectedFormRevision: 9, comment: '收回', idempotencyKey: 'recall-key' })
  }, undefined);
});
it('代理状态支持显式空值并验证可收回条件', async () => {
  vi.mocked(http.request).mockResolvedValueOnce({ delegation: null }).mockResolvedValueOnce({ delegation: grant });
  expect(await getWorkflowTodoDelegation(origin)).toBeNull(); expect(await getWorkflowTodoDelegation(origin)).toEqual(grant);
  vi.mocked(http.request).mockResolvedValue({ delegation: { ...grant, statusKey: 'completed', canRecall: true } });
  await expect(getWorkflowTodoDelegation(origin)).rejects.toThrow('client.invalid_workflow_todo_delegation');
});
it('候选使用真实分页和独立代理入口', async () => {
  const page = { items: [{ id: target, username: 'proxy', displayName: '代理人' }], page: 2, pageSize: 20, total: 21 };
  vi.mocked(http.request).mockResolvedValue(page);
  expect(await listWorkflowTodoDelegationCandidates(origin, 2, 20)).toEqual(page);
  expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/todos/${origin}/delegation-candidates?page=2&pageSize=20`, { method: 'GET' }, undefined);
});
