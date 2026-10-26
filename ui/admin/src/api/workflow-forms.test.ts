import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowCreateForm,
  workflowGetDesignerAsset,
  workflowSaveDesignerAsset,
  workflowGetForm,
  workflowGetFormComponentCatalog,
  workflowListForms,
  workflowPublishForm,
  workflowUpdateFormDraft,
  type WorkflowFormSchema
} from '@fullnet/client-contracts';
import {
  createWorkflowForm,
  getWorkflowDesignerAsset,
  saveWorkflowDesignerAsset,
  getWorkflowForm,
  getWorkflowFormComponentCatalog,
  listWorkflowForms,
  publishWorkflowForm,
  updateWorkflowFormDraft
} from './workflow-forms';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowCreateForm: vi.fn(),
  workflowGetDesignerAsset: vi.fn(),
  workflowSaveDesignerAsset: vi.fn(),
  workflowGetForm: vi.fn(),
  workflowGetFormComponentCatalog: vi.fn(),
  workflowListForms: vi.fn(),
  workflowPublishForm: vi.fn(),
  workflowUpdateFormDraft: vi.fn()
}));

const draft = {
  schemaVersion: 1,
  adapterVersion: 1,
  sections: [{
    sectionKey: 'main',
    fields: [{ fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }]
  }]
} satisfies WorkflowFormSchema;

describe('workflow forms api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('完整设计稿只接受对象根，并保留未知扩展内容', async () => {
    const asset = { formId: 'form-1', draftRevision: 1, sourceCommit: 'c67479e496bab56a93a3dff168a4f529d8293c67',
      formatVersion: 3, contentHash: 'a'.repeat(64), designerJson: { widgetList: [], formConfig: { jsonVersion: 3 }, extension: { keep: true } } };
    vi.mocked(workflowGetDesignerAsset).mockResolvedValue(asset);
    expect((await getWorkflowDesignerAsset('form-1'))?.designerJson).toEqual(asset.designerJson);
    vi.mocked(workflowGetDesignerAsset).mockResolvedValue({ ...asset, designerJson: [] });
    await expect(getWorkflowDesignerAsset('form-1')).rejects.toThrow('client.invalid_workflow_designer_asset');
  });

  it('完整设计资产读取和保存保留扩展字段及配置', async () => {
    const designerJson = { widgetList: [{ type: 'future-widget', options: { keep: [1, true, null] } }], formConfig: { jsonVersion: 3, cssCode: '.a {}' }, extension: { keep: true } };
    const asset = { formId: 'form-1', draftRevision: 2, formatVersion: 3, sourceCommit: 'source', contentHash: 'hash', designerJson };
    vi.mocked(workflowGetDesignerAsset).mockResolvedValue(asset);
    vi.mocked(workflowSaveDesignerAsset).mockResolvedValue(asset);
    expect((await getWorkflowDesignerAsset('form-1'))?.designerJson).toEqual(designerJson);
    expect((await saveWorkflowDesignerAsset('form-1', 1, designerJson)).designerJson).toEqual(designerJson);
  });

  it.each([null, [], 'invalid', {}, { widgetList: [] }, { widgetList: [], formConfig: [] }])('非法完整设计资产 %j 读取和保存均失败关闭', async designerJson => {
    const asset = { formId: 'form-1', draftRevision: 2, formatVersion: 3, sourceCommit: 'source', contentHash: 'hash', designerJson };
    vi.mocked(workflowGetDesignerAsset).mockResolvedValue(asset);
    vi.mocked(workflowSaveDesignerAsset).mockResolvedValue(asset);
    await expect(getWorkflowDesignerAsset('form-1')).rejects.toThrow('client.invalid_workflow_designer_asset');
    await expect(saveWorkflowDesignerAsset('form-1', 1, { widgetList: [], formConfig: {} })).rejects.toThrow('client.invalid_workflow_designer_asset');
  });

  it('只通过生成客户端读取列表、详情与组件目录', async () => {
    vi.mocked(workflowListForms).mockResolvedValue([]);
    vi.mocked(workflowGetForm).mockResolvedValue(formResponse());
    vi.mocked(workflowGetFormComponentCatalog).mockResolvedValue(catalogResponse());

    await listWorkflowForms();
    await getWorkflowForm('form-1');
    await getWorkflowFormComponentCatalog();

    expect(workflowListForms).toHaveBeenCalledWith(expect.anything(), {}, undefined);
    expect(workflowGetForm).toHaveBeenCalledWith(expect.anything(), { formId: 'form-1' }, undefined);
    expect(workflowGetFormComponentCatalog).toHaveBeenCalledWith(expect.anything(), {}, undefined);
  });

  it('创建、保存和发布均保留强类型请求及并发修订号', async () => {
    vi.mocked(workflowCreateForm).mockResolvedValue(formResponse());
    vi.mocked(workflowUpdateFormDraft).mockResolvedValue(formResponse(3));
    vi.mocked(workflowPublishForm).mockResolvedValue(versionResponse());

    await createWorkflowForm('purchase.request', draft);
    await updateWorkflowFormDraft('form-1', 2, draft);
    await publishWorkflowForm('form-1', { expectedRevision: 3 });

    expect(workflowCreateForm).toHaveBeenCalledWith(
      expect.anything(),
      { body: { formKey: 'purchase.request', draft, designerJson: { widgetList: [], formConfig: { jsonVersion: 3 } } } },
      undefined
    );
    expect(workflowUpdateFormDraft).toHaveBeenCalledWith(
      expect.anything(),
      { formId: 'form-1', body: { expectedRevision: 2, draft } },
      undefined
    );
    expect(workflowPublishForm).toHaveBeenCalledWith(
      expect.anything(),
      { formId: 'form-1', body: { expectedRevision: 3 } },
      undefined
    );
  });
});

function formResponse(draftRevision = 2) {
  return {
    id: 'form-1', formKey: 'purchase.request', statusKey: 'active', version: 1, draft, draftRevision,
    latestPublishedVersionId: null, createdAtUtc: '2026-08-30T00:00:00Z', updatedAtUtc: null
  };
}

function catalogResponse() {
  return {
    catalogVersion: 1, schemaVersion: 1, adapterVersion: 1,
    components: [{
      fieldTypeKey: 'text', designable: true, publishable: true,
      executable: true, constraintKeys: ['minLength', 'maxLength']
    }]
  };
}

function versionResponse() {
  return {
    id: 'version-1', formDefinitionId: 'form-1', versionNumber: 1,
    schemaVersion: 1, adapterVersion: 1, componentCatalogVersion: 1,
    formSchemaJson: '{}', webRenderSchemaJson: '{}', contentHash: 'hash',
    publishedById: 'user-1', publishedAtUtc: '2026-08-30T00:00:00Z'
  };
}
