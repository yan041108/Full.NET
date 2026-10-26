import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getWorkflowStarterSelections, listWorkflowStarterSelectionCandidates, readWorkflowAssigneeSelections } from './workflow-starter-selections';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '01912345-6789-7abc-8def-0123456789ab';
beforeEach(() => vi.clearAllMocks());
describe('启动选择边界', () => {
  it('规范化选择键并保留有序成员，拒绝身份对象、重复和超额输入', () => {
    expect(Object.keys(readWorkflowAssigneeSelections({ z: [id], A: [id], a: [id] }))).toEqual(['A', 'a', 'z']);
    expect(() => readWorkflowAssigneeSelections({ one: [{ userId: id, tenantId: id }] })).toThrow();
    expect(() => readWorkflowAssigneeSelections({ one: [id, id.toUpperCase()] })).toThrow();
    expect(() => readWorkflowAssigneeSelections(Object.fromEntries(Array.from({ length: 65 }, (_, index) => [`node${index}`, [id]])))).toThrow();
  });
  it('拒绝版本串线和重复选择标识的元数据', async () => {
    const descriptor = { selectionKey: 'review', nodeKey: 'review', nodeName: 'Review', modeKey: 'single', maximumUsers: 1, rangeKey: 'all' };
    vi.mocked(http.request).mockResolvedValue({ definitionVersionId: id, items: [descriptor] });
    expect((await getWorkflowStarterSelections(id)).items).toHaveLength(1);
    vi.mocked(http.request).mockResolvedValue({ definitionVersionId: id, items: [descriptor, descriptor] });
    await expect(getWorkflowStarterSelections(id)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValue({ definitionVersionId: 'external', items: [] });
    await expect(getWorkflowStarterSelections(id)).rejects.toThrow();
  });
  it('候选查询只发送发布选择标识和页码，不发送客户端角色或租户范围', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [{ id, username: 'user', displayName: 'User' }], page: 2, pageSize: 20, total: 21 });
    await listWorkflowStarterSelectionCandidates(id, 'review', 2);
    expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/definition-versions/${id}/starter-selections/review/candidates?page=2&pageSize=20`, { method: 'GET' }, undefined);
  });
});
