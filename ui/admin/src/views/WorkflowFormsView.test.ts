import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, h, ref } from 'vue';
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
import { workflowForm as form, workflowFormDraft as draft, workflowFormCatalog as catalog, workflowFormVersion } from '../test/workflow-form-fixtures';

vi.mock('../api/workflow-forms', () => ({
  createWorkflowForm: vi.fn(),
  getWorkflowForm: vi.fn(),
  getWorkflowFormComponentCatalog: vi.fn(),
  listWorkflowForms: vi.fn(),
  publishWorkflowForm: vi.fn(),
  updateWorkflowFormDraft: vi.fn(), setWorkflowFormStatus: vi.fn(), deleteWorkflowFormVersion: vi.fn(), listWorkflowFormVersions: vi.fn()
}));

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().state = 'authenticated';
  useSessionStore().currentUser = {
    id: workflowFormVersion().publishedById, username: 'starter', displayName: '管理员', tenantId: form.id,
    actorScope: 'tenant:' + form.id.replaceAll('-', ''), scope: 'tenant:' + form.id.replaceAll('-', ''), isSuperAdministrator: false,
    passwordChangeRequired: false, permissions,
    sessionId: 'session-1', preferredLocale: 'zh-CN', profileVersion: 1
  };
  return mount(WorkflowFormsView, {
    global: {
      plugins: [pinia],
      stubs: { VForm3WorkflowDesigner: VForm3DesignerStub }
    }
  });
}

const VForm3DesignerStub = defineComponent({
  name: 'VForm3WorkflowDesigner',
  props: { schema: { type: Object, required: true } },
  setup(props, { expose }) {
    const required = ref((props.schema as WorkflowFormSchema).sections[0]?.fields[0]?.required ?? false);
    expose({
      readSchema: () => ({
        ...(props.schema as WorkflowFormSchema),
        sections: (props.schema as WorkflowFormSchema).sections.map(section => ({
          ...section,
          fields: section.fields.map(field => ({ ...field, required: required.value }))
        }))
      })
    });
    return () => h('article', { 'data-field-key': 'summary' }, [
      h('input', {
        type: 'checkbox',
        'data-field-property': 'required',
        checked: required.value,
        onChange: (event: Event) => {
          required.value = (event.target as HTMLInputElement).checked;
        }
      })
    ]);
  }
});

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
    vi.mocked(listWorkflowForms).mockResolvedValueOnce([]);
    vi.mocked(createWorkflowForm).mockResolvedValue({ ...form, draftRevision: 1 });
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.create']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-create"]').trigger('click');
    await wrapper.get('[data-testid="workflow-form-key"]').setValue('purchase.request');
    await wrapper.get('[data-testid="workflow-form-create-submit"]').trigger('click');
    await flushPromises();

    expect(createWorkflowForm).toHaveBeenCalledWith('purchase.request', draft, expect.any(AbortSignal));
    expect(listWorkflowForms).toHaveBeenCalledTimes(2);
  });

  it('编辑先读取权威 Draft 与目录并用当前修订号保存', async () => {
    const saved = { ...form, draftRevision: 3, draft: { ...draft, sections: [{ ...draft.sections[0]!, fields: [{ ...draft.sections[0]!.fields[0]!, required: false }] }] } };
    vi.mocked(updateWorkflowFormDraft).mockResolvedValue(saved);
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-field-key="summary"] [data-field-property="required"]').setValue(false);
    await wrapper.get('[data-testid="workflow-form-save"]').trigger('click');
    await flushPromises();

    expect(getWorkflowForm).toHaveBeenCalledWith(form.id, expect.any(AbortSignal));
    expect(getWorkflowFormComponentCatalog).toHaveBeenCalledOnce();
    expect(updateWorkflowFormDraft).toHaveBeenCalledWith(
      form.id,
      2,
      expect.objectContaining({
        sections: [expect.objectContaining({
          fields: [expect.objectContaining({ required: false })]
        })]
      }),
      expect.any(AbortSignal)
    );
    expect(wrapper.text()).toContain('Revision 3');
  });

  it('409 冲突保留本地 Draft，发布使用当前修订号并刷新权威对象', async () => {
    vi.mocked(updateWorkflowFormDraft).mockRejectedValue({
      status: 409, code: 'workflow.form_revision_conflict', title: '表单已被其他用户修改'
    });
    vi.mocked(publishWorkflowForm).mockResolvedValue(workflowFormVersion());
    const wrapper = mountWithPermissions([
      'workflow.forms.read', 'workflow.forms.update', 'workflow.forms.publish'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-field-key="summary"] [data-field-property="required"]').setValue(false);
    await wrapper.get('[data-testid="workflow-form-save"]').trigger('click');
    await flushPromises();

    expect((wrapper.vm as unknown as { problem: { code: string } }).problem.code).toBe('workflow.form_revision_conflict');
    expect(wrapper.get('[data-field-key="summary"] [data-field-property="required"]')
      .element).toHaveProperty('checked', false);

    await wrapper.get('[data-testid="workflow-form-close-editor"]').trigger('click');
    await wrapper.get('[data-testid="workflow-form-refresh"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-publish"]').trigger('click');
    await flushPromises();
    expect(publishWorkflowForm).toHaveBeenCalledWith(form.id, { expectedRevision: 2 }, expect.any(AbortSignal));
    expect(getWorkflowForm).toHaveBeenCalledOnce();
  });
});
