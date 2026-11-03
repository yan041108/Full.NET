import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getWorkflowSimulationStarterSelections, listWorkflowSimulationStarterSelectionCandidates } from './workflow-observability';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '01912345-6789-7abc-8def-0123456789ab';
const form = '01912345-6789-7abc-8def-0123456789ac';
const source = { kind: 'simulation' as const, definitionId: id, draftRevision: 3, formVersionId: form };
beforeEach(() => vi.clearAllMocks());

describe('仿真选人的实际生成协议适配', () => {
  it('发送固定草稿及表单锚点，并拒绝另一修订的元数据', async () => {
    vi.mocked(http.request).mockResolvedValue({ definitionId: id, draftRevision: 3, formVersionId: form, items: [] });
    expect((await getWorkflowSimulationStarterSelections(source)).items).toEqual([]);
    expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/definitions/${id}/simulation/starter-selections?expectedRevision=3&formVersionId=${form}`, { method: 'GET' }, undefined);
    vi.mocked(http.request).mockResolvedValue({ definitionId: id, draftRevision: 2, formVersionId: form, items: [] });
    await expect(getWorkflowSimulationStarterSelections(source)).rejects.toThrow('workflow.simulation.binding_mismatch');
  });
  it('拒绝重复选择声明，不接受客户端重定义范围', async () => {
    const descriptor = { selectionKey: 'reviewChoice', nodeKey: 'claim', nodeName: 'Claim', modeKey: 'single', maximumUsers: 1, rangeKey: 'specified_users' };
    vi.mocked(http.request).mockResolvedValue({ definitionId: id, draftRevision: 3, formVersionId: form, items: [descriptor, descriptor] });
    await expect(getWorkflowSimulationStarterSelections(source)).rejects.toThrow();
  });
  it('候选分页带相同锚点，并拒绝服务器返回错页', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [{ id, username: 'reviewer', displayName: 'Reviewer' }], page: 2, pageSize: 20, total: 21 });
    await listWorkflowSimulationStarterSelectionCandidates(source, 'reviewChoice', 2);
    expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/definitions/${id}/simulation/starter-selections/reviewChoice/candidates?expectedRevision=3&formVersionId=${form}&page=2&pageSize=20`, { method: 'GET' }, undefined);
    vi.mocked(http.request).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    await expect(listWorkflowSimulationStarterSelectionCandidates(source, 'reviewChoice', 2)).rejects.toThrow();
  });
});
