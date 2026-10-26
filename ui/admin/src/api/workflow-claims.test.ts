import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { claimWorkflowPool, listWorkflowClaimPools, releaseWorkflowTodo } from './workflow-claims';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '01912345-6789-7abc-8def-0123456789ab';
const receipt = { id, definitionVersionId: id, formVersionId: id, businessType: 'claim', businessId: 'one', businessTitle: null,
  statusKey: 'active', revision: 2, activeTodoId: null, startedAtUtc: '2026-10-25T00:00:00Z' };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(http.request).mockResolvedValue(receipt); });
describe('生成的领取操作与业务边界', () => {
  it('领取请求不能增加办理人或租户字段', async () => {
    await expect(claimWorkflowPool(id, { expectedRevision: 1, idempotencyKey: 'claim', actorUserId: id } as never)).rejects.toThrow();
    expect(http.request).not.toHaveBeenCalled();
  });
  it('释放提交三个修订并读取生成的固定回执', async () => {
    const body = { expectedRevision: 1, expectedPoolRevision: 2, expectedFormRevision: 3, idempotencyKey: 'release' };
    expect(await releaseWorkflowTodo(id, body)).toEqual(receipt);
    expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/todos/${id}/release`, expect.objectContaining({ method: 'POST', body: JSON.stringify(body) }), undefined);
  });
  it('生成形状之后仍拒绝不可领取的池记录', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [{ id: 'invalid' }], totalCount: 1, page: 1, pageSize: 20 });
    await expect(listWorkflowClaimPools()).rejects.toThrow();
  });
});
