import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowCreateForm,
  workflowGetForm,
  workflowGetFormComponentCatalog,
  workflowListForms,
  workflowPublishForm,
  workflowUpdateFormDraft,
  type WorkflowFormSchema
} from '@fullnet/client-contracts';
import {
  createWorkflowForm,
  getWorkflowForm,
  getWorkflowFormComponentCatalog,
  listWorkflowForms,
  publishWorkflowForm,
  updateWorkflowFormDraft
} from './workflow-forms';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowCreateForm: vi.fn(),
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
      { body: { formKey: 'purchase.request', draft } },
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
    id: 'form-1', formKey: 'purchase.request', draft, draftRevision,
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
