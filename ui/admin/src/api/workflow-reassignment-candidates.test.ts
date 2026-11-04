import { beforeEach, expect, it, vi } from 'vitest';
import { listWorkflowReassignmentCandidates } from './workflow-reassignment-candidates';
import { request } from './http';
vi.mock('./http', () => ({ request: vi.fn() }));
const id = '01912345-6789-7abc-8def-0123456789aa', todo = '01912345-6789-7abc-8def-0123456789bb';
const item = { id, username: 'reviewer', displayName: 'Review user' };
const page = { instanceId: id, instanceRevision: 3, todoId: todo, todoRevision: 7, items: [item], page: 1, pageSize: 20, total: 1 };
beforeEach(() => { vi.resetAllMocks(); vi.mocked(request).mockResolvedValue(page); });
it('显式实例待办与页码使用只读 GET，并传递取消信号和规范化总数', async () => {
  vi.mocked(request).mockResolvedValue({ ...page, total: '1' }); const signal = new AbortController().signal;
  expect(await listWorkflowReassignmentCandidates(id, todo, 1, 20, signal)).toEqual(page);
  expect(request).toHaveBeenCalledExactlyOnceWith(`/api/v1/workflow/instances/${id}/reassignment-candidates?todoId=${todo}&page=1&pageSize=20`, { method: 'GET' }, signal);
});
it.each([[0, 20], [1, 101], [1.5, 20], [2_147_483_647, 100]])('拒绝无界页码 %s/%s，不发请求', async (number, size) => {
  await expect(listWorkflowReassignmentCandidates(id, todo, number, size)).rejects.toThrow('client.invalid_workflow_reassignment_candidates_request'); expect(request).not.toHaveBeenCalled();
});
it.each(['invalid', '00000000-0000-0000-0000-000000000000'])('拒绝非法资源 %s', async value => {
  await expect(listWorkflowReassignmentCandidates(id, value)).rejects.toThrow('client.invalid_workflow_reassignment_candidates_request'); expect(request).not.toHaveBeenCalled();
});
it.each([{ instanceId: todo }, { todoId: id }, { todoRevision: 0 }, { page: 2 }, { total: 0 }, { items: [item, item] }, { items: [{ ...item, username: ' ' }] }, { items: [{ ...item, displayName: '' }] }, { items: [{ ...item, id: '00000000-0000-0000-0000-000000000000' }] }])('拒绝错页或不可选择响应 %j', async patch => {
  vi.mocked(request).mockResolvedValue({ ...page, ...patch }); await expect(listWorkflowReassignmentCandidates(id, todo)).rejects.toThrow();
});
it('保留服务端问题详情，不静默返回空候选', async () => {
  const problem = { status: 403, code: 'workflow.instance_forbidden' }; vi.mocked(request).mockRejectedValue(problem);
  await expect(listWorkflowReassignmentCandidates(id, todo)).rejects.toBe(problem);
});
