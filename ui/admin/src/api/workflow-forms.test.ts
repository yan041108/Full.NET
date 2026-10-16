import { beforeEach, describe, expect, it, vi } from 'vitest';
import { workflowForm as form, workflowFormDraft as draft, workflowFormCatalog as catalog, workflowFormVersion } from '../test/workflow-form-fixtures';
import {
  workflowCreateForm,
  workflowGetForm,
  workflowGetFormComponentCatalog,
  workflowListForms,
  workflowPublishForm,
  workflowUpdateFormDraft
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


describe('workflow forms api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('只通过生成客户端读取列表、详情与组件目录', async () => {
    vi.mocked(workflowListForms).mockResolvedValue([]);
    vi.mocked(workflowGetForm).mockResolvedValue(formResponse());
    vi.mocked(workflowGetFormComponentCatalog).mockResolvedValue(catalogResponse());

    await listWorkflowForms();
    await getWorkflowForm(form.id);
    await getWorkflowFormComponentCatalog();

    expect(workflowListForms).toHaveBeenCalledWith(expect.anything(), {}, undefined);
    expect(workflowGetForm).toHaveBeenCalledWith(expect.anything(), { formId: form.id }, undefined);
    expect(workflowGetFormComponentCatalog).toHaveBeenCalledWith(expect.anything(), {}, undefined);
  });

  it('创建、保存和发布均保留强类型请求及并发修订号', async () => {
    vi.mocked(workflowCreateForm).mockResolvedValue(formResponse(1));
    vi.mocked(workflowUpdateFormDraft).mockResolvedValue(formResponse(3));
    vi.mocked(workflowPublishForm).mockResolvedValue(versionResponse());

    await createWorkflowForm('purchase.request', draft);
    await updateWorkflowFormDraft(form.id, 2, draft);
    await publishWorkflowForm(form.id, { expectedRevision: 3 });

    expect(workflowCreateForm).toHaveBeenCalledWith(
      expect.anything(),
      { body: { formKey: 'purchase.request', draft } },
      undefined
    );
    expect(workflowUpdateFormDraft).toHaveBeenCalledWith(
      expect.anything(),
      { formId: form.id, body: { expectedRevision: 2, draft } },
      undefined
    );
    expect(workflowPublishForm).toHaveBeenCalledWith(
      expect.anything(),
      { formId: form.id, body: { expectedRevision: 3 } },
      undefined
    );
  });
});

function formResponse(draftRevision = 2) { return { ...form, draftRevision }; }
function catalogResponse() { return catalog; }
function versionResponse() { return workflowFormVersion(); }
