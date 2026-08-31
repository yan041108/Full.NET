import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createWorkflowDefinition,
  getWorkflowDefinition,
  getWorkflowNodeTypeCatalog,
  publishWorkflowDefinition,
  updateWorkflowDefinitionDraft
} from './workflow-definitions';

vi.mock('./http', () => ({
  http: { request: vi.fn() }
}));

const response = {
  id: '0198f955-899d-7000-8000-000000000001',
  definitionKey: 'expense',
  draft: { schemaVersion: 1, nodes: [] },
  draftRevision: 1,
  latestPublishedVersionId: null,
  version: 1,
  createdAtUtc: '2026-08-30T00:00:00Z',
  updatedAtUtc: null
};

describe('workflow definition 管理 API', () => {
  beforeEach(() => vi.mocked(http.request).mockReset());

  it('创建、读取和更新 Draft 使用精确管理端点', async () => {
    vi.mocked(http.request).mockResolvedValue(response);

    await createWorkflowDefinition('expense', response.draft);
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/workflow/definitions', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ definitionKey: 'expense', draft: response.draft })
    }), undefined);

    await getWorkflowDefinition(response.id);
    expect(http.request).toHaveBeenLastCalledWith(`/api/v1/workflow/definitions/${response.id}`, {
      method: 'GET'
    }, undefined);

    await updateWorkflowDefinitionDraft(response.id, 1, response.draft);
    expect(http.request).toHaveBeenLastCalledWith(`/api/v1/workflow/definitions/${response.id}/draft`, expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ expectedRevision: 1, draft: response.draft })
    }), undefined);
  });

  it('发布时同时提交 Draft 修订和已发布表单版本', async () => {
    vi.mocked(http.request).mockResolvedValue({
      id: '0198f955-899d-7000-8000-000000000002',
      definitionId: response.id,
      formVersionId: '0198f955-899d-7000-8000-000000000003',
      versionNumber: 1,
      schemaVersion: 1,
      canonicalJson: '{}',
      contentHash: 'a'.repeat(64),
      publishedById: '0198f955-899d-7000-8000-000000000004',
      publishedAtUtc: '2026-08-30T00:00:00Z'
    });

    await publishWorkflowDefinition(response.id, 1, '0198f955-899d-7000-8000-000000000003');

    expect(http.request).toHaveBeenCalledWith(`/api/v1/workflow/definitions/${response.id}/publish`, expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({
        expectedRevision: 1,
        formVersionId: '0198f955-899d-7000-8000-000000000003'
      })
    }), undefined);
  });

  it('读取节点目录并拒绝畸形响应', async () => {
    vi.mocked(http.request).mockResolvedValue({
      catalogVersion: 1,
      definitionSchemaVersion: 1,
      nodeTypes: [{
        nodeTypeKey: 'start',
        nodeSchemaVersion: 1,
        designable: true,
        publishable: true,
        executable: true,
        supportsFieldPolicies: false
      }]
    });
    await expect(getWorkflowNodeTypeCatalog()).resolves.toMatchObject({ catalogVersion: 1 });

    vi.mocked(http.request).mockResolvedValueOnce({ catalogVersion: 'bad' });
    await expect(getWorkflowNodeTypeCatalog()).rejects.toThrow(
      'client.invalid_workflow_node_type_catalog_response'
    );
  });
});
