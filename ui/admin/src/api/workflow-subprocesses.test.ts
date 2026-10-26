import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getSubprocessAsset, listSubprocessDefinitions, listSubprocessVersions } from './workflow-subprocesses';
import { http } from './http';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '11111111-1111-4111-8111-111111111111'; const now = '2026-10-26T00:00:00Z';
const field = { fieldKey: 'title', fieldTypeKey: 'text', required: false, constraints: {} };
const schema = { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [field] }] };
const draft = { schemaVersion: 1, workflowKindKey: 'automation', nodes: [{ nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: {} }] };
const definition = { id, definitionKey: 'child', draft, draftRevision: 1, latestPublishedVersionId: id, businessTitleTemplate: null, statusKey: 'active', version: 1, createdAtUtc: now, updatedAtUtc: null };
const version = { id, definitionId: id, formVersionId: id, versionNumber: 1, schemaVersion: 1, canonicalJson: JSON.stringify(draft), contentHash: 'a'.repeat(64), businessTitleTemplate: null, publishedById: id, publishedAtUtc: now };
const form = { id, formDefinitionId: id, versionNumber: 1, schemaVersion: 1, adapterVersion: 1, componentCatalogVersion: 1, formSchemaJson: JSON.stringify(schema), webRenderSchemaJson: '{}', contentHash: 'a'.repeat(64), publishedById: id, publishedAtUtc: now };
beforeEach(() => vi.clearAllMocks());
describe('子流程真实发布资产客户端', () => {
  it('只保留活动且已发布定义，版本请求保留取消信号', async () => {
    vi.mocked(http.request).mockResolvedValueOnce([definition, { ...definition, statusKey: 'disabled' }, { ...definition, latestPublishedVersionId: null }]);
    expect(await listSubprocessDefinitions()).toEqual([definition]);
    const controller = new AbortController(); vi.mocked(http.request).mockResolvedValueOnce([version]);
    expect(await listSubprocessVersions(id, controller.signal)).toEqual([version]);
    expect(http.request).toHaveBeenLastCalledWith(`/api/v1/workflow/definitions/${id}/versions`, expect.objectContaining({ method: 'GET' }), controller.signal);
  });
  it('按固定版本读取真实表单，返回最小字段投影', async () => {
    vi.mocked(http.request).mockResolvedValueOnce(version).mockResolvedValueOnce(form);
    expect(await getSubprocessAsset(id)).toEqual({ definitionId: id, versionId: id, fields: [field] });
    expect(http.request).toHaveBeenNthCalledWith(1, `/api/v1/workflow/definition-versions/${id}`, expect.objectContaining({ method: 'GET' }), undefined);
    expect(http.request).toHaveBeenNthCalledWith(2, `/api/v1/workflow/form-versions/${id}`, expect.objectContaining({ method: 'GET' }), undefined);
  });
  it('拒绝把根最终提交动作作为子流程选择', async () => {
    const invalid = { ...draft, nodes: [{ ...draft.nodes[0], nodeTypeKey: 'business.command' }] };
    vi.mocked(http.request).mockResolvedValueOnce({ ...version, canonicalJson: JSON.stringify(invalid) }).mockResolvedValueOnce(form);
    await expect(getSubprocessAsset(id)).rejects.toThrow('client.invalid_workflow_subprocess_asset');
  });
  it('拒绝损坏表单或缺失真实发布元数据', async () => {
    vi.mocked(http.request).mockResolvedValueOnce(version).mockResolvedValueOnce({ ...form, formSchemaJson: '{}' });
    await expect(getSubprocessAsset(id)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValueOnce({ id }); await expect(getSubprocessAsset(id)).rejects.toThrow();
  });
});
