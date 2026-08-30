import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { WorkflowFormSchema } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import {
  createWorkflowForm,
  getWorkflowForm,
  getWorkflowFormComponentCatalog,
  listWorkflowForms,
  publishWorkflowForm,
  updateWorkflowFormDraft
} from '../api/workflow-forms';
import WorkflowFormsView from './WorkflowFormsView.vue';

vi.mock('../api/workflow-forms', () => ({
  createWorkflowForm: vi.fn(),
  getWorkflowForm: vi.fn(),
  getWorkflowFormComponentCatalog: vi.fn(),
  listWorkflowForms: vi.fn(),
  publishWorkflowForm: vi.fn(),
  updateWorkflowFormDraft: vi.fn()
}));

const draft = {
  schemaVersion: 1,
  adapterVersion: 1,
  sections: [{
    sectionKey: 'main',
    fields: [{ fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }]
  }]
} satisfies WorkflowFormSchema;
const form = {
  id: 'form-1', formKey: 'purchase.request', draft, draftRevision: 2,
  latestPublishedVersionId: null, createdAtUtc: '2026-08-30T00:00:00Z', updatedAtUtc: null
};
const catalog = {
  catalogVersion: 1, schemaVersion: 1, adapterVersion: 1,
  components: [{
    fieldTypeKey: 'text', designable: true, publishable: true,
    executable: true, constraintKeys: ['minLength', 'maxLength']
  }]
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = {
    id: 'user-1', username: 'starter', displayName: '管理员', tenantId: 'tenant-1',
    actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false, permissions,
    sessionId: 'session-1', preferredLocale: 'zh-CN', profileVersion: 1
  };
  return mount(WorkflowFormsView, { global: { plugins: [pinia] } });
}

describe('WorkflowFormsView', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listWorkflowForms).mockResolvedValue([form]);
    vi.mocked(getWorkflowForm).mockResolvedValue(form);
    vi.mocked(getWorkflowFormComponentCatalog).mockResolvedValue(catalog);
  });

  it('只读权限可加载页面但不创建写操作入口', async () => {
    const wrapper = mountWithPermissions(['workflow.forms.read']);
    await flushPromises();

    expect(listWorkflowForms).toHaveBeenCalledOnce();
    expect(wrapper.text()).toContain('purchase.request');
    expect(wrapper.find('[data-testid="workflow-form-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-form-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-form-publish"]').exists()).toBe(false);
  });

  it('创建表单时发送安全默认 Draft 并刷新权威列表', async () => {
    vi.mocked(createWorkflowForm).mockResolvedValue(form);
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.create']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-create"]').trigger('click');
    await wrapper.get('[data-testid="workflow-form-key"]').setValue('purchase.request');
    await wrapper.get('[data-testid="workflow-form-create-submit"]').trigger('click');
    await flushPromises();

    expect(createWorkflowForm).toHaveBeenCalledWith({
      formKey: 'purchase.request',
      draft
    });
    expect(listWorkflowForms).toHaveBeenCalledTimes(2);
  });

  it('编辑先读取权威 Draft 与目录并用当前修订号保存', async () => {
    const saved = { ...form, draftRevision: 3 };
    vi.mocked(updateWorkflowFormDraft).mockResolvedValue(saved);
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-field-key="summary"] [data-field-property="required"]').setValue(false);
    await wrapper.get('[data-testid="workflow-form-save"]').trigger('click');
    await flushPromises();

    expect(getWorkflowForm).toHaveBeenCalledWith('form-1');
    expect(getWorkflowFormComponentCatalog).toHaveBeenCalledOnce();
    expect(updateWorkflowFormDraft).toHaveBeenCalledWith('form-1', {
      expectedRevision: 2,
      draft: expect.objectContaining({
        sections: [expect.objectContaining({
          fields: [expect.objectContaining({ required: false })]
        })]
      })
    });
    expect(wrapper.text()).toContain('Revision 3');
  });

  it('409 冲突保留本地 Draft，发布使用当前修订号并刷新权威对象', async () => {
    vi.mocked(updateWorkflowFormDraft).mockRejectedValue({
      status: 409, code: 'workflow.form_revision_conflict', title: '表单已被其他用户修改'
    });
    vi.mocked(publishWorkflowForm).mockResolvedValue({
      id: 'version-1', formDefinitionId: 'form-1', versionNumber: 1,
      schemaVersion: 1, adapterVersion: 1, componentCatalogVersion: 1,
      formSchemaJson: '{}', webRenderSchemaJson: '{}', contentHash: 'hash',
      publishedById: 'user-1', publishedAtUtc: '2026-08-30T00:00:00Z'
    });
    const wrapper = mountWithPermissions([
      'workflow.forms.read', 'workflow.forms.update', 'workflow.forms.publish'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-field-key="summary"] [data-field-property="required"]').setValue(false);
    await wrapper.get('[data-testid="workflow-form-save"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain('workflow.form_revision_conflict');
    expect(wrapper.get('[data-field-key="summary"] [data-field-property="required"]')
      .element).toHaveProperty('checked', false);

    await wrapper.get('[data-testid="workflow-form-publish"]').trigger('click');
    await flushPromises();
    expect(publishWorkflowForm).toHaveBeenCalledWith('form-1', { expectedRevision: 2 });
    expect(getWorkflowForm).toHaveBeenCalledTimes(2);
  });
});
