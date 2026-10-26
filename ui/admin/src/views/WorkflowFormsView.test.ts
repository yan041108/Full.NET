import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, h, ref } from 'vue';
import { ElMessage } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { WorkflowFormSchema } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import {
  createWorkflowForm,
  deleteWorkflowFormVersion,
  getWorkflowForm,
  getWorkflowDesignerAsset,
  listWorkflowForms,
  listWorkflowFormVersions,
  publishWorkflowForm,
  saveWorkflowDesignerAsset
} from '../api/workflow-forms';
import WorkflowFormsView from './WorkflowFormsView.vue';

vi.mock('../api/workflow-forms', () => ({
  createWorkflowForm: vi.fn(),
  deleteWorkflowFormVersion: vi.fn(),
  getWorkflowForm: vi.fn(),
  getWorkflowDesignerAsset: vi.fn(),
  listWorkflowForms: vi.fn(),
  listWorkflowFormVersions: vi.fn(),
  publishWorkflowForm: vi.fn(),
  saveWorkflowDesignerAsset: vi.fn()
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
  id: 'form-1', formKey: 'purchase.request', statusKey: 'active', version: 1, draft, draftRevision: 2,
  latestPublishedVersionId: null, createdAtUtc: '2026-08-30T00:00:00Z', updatedAtUtc: null
};
const design = { widgetList: [{ id: 'title', type: 'input', options: { name: 'summary', required: true, label: '摘要', custom: { keep: '原样配置' } } }], formConfig: { jsonVersion: 3, labelWidth: 180, onFormMounted: 'console.log("test")' } };
const asset = { formId: form.id, draftRevision: 2, sourceCommit: 'c67479e496bab56a93a3dff168a4f529d8293c67', formatVersion: 3, designerJson: design, contentHash: 'hash' };
const version = { id: 'version-1', formDefinitionId: form.id, versionNumber: 1, schemaVersion: 1, adapterVersion: 1, componentCatalogVersion: 1, formSchemaJson: '{}', webRenderSchemaJson: '{}', contentHash: 'hash', publishedById: 'user-1', publishedAtUtc: '2026-10-24T00:00:00Z' };

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = {
    id: 'user-1', username: 'starter', displayName: '管理员', tenantId: 'tenant-1',
    actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false,
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

let readDesignGate: Promise<void> | undefined;
const VForm3DesignerStub = defineComponent({
  name: 'VForm3WorkflowDesigner',
  props: { designerJson: { type: Object, required: true } },
  setup(props, { expose }) {
    const required = ref(true);
    expose({ readDesign: async () => { await readDesignGate; return { ...props.designerJson, widgetList: [{ ...(props.designerJson.widgetList[0]), options: { ...props.designerJson.widgetList[0].options, required: required.value } }] }; } });
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
    vi.resetAllMocks();
    readDesignGate = undefined;
    vi.mocked(listWorkflowForms).mockResolvedValue([form]);
    vi.mocked(getWorkflowForm).mockResolvedValue(form);
    vi.mocked(getWorkflowDesignerAsset).mockResolvedValue(asset);
    vi.mocked(listWorkflowFormVersions).mockResolvedValue([]);
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

    expect(createWorkflowForm).toHaveBeenCalledWith('purchase.request', draft);
    expect(listWorkflowForms).toHaveBeenCalledTimes(2);
  });

  it('编辑读取完整资产，改动属性后以当前修订保存全部原版配置', async () => {
    const saved = { ...asset, draftRevision: 3 };
    vi.mocked(saveWorkflowDesignerAsset).mockResolvedValue(saved);
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-field-key="summary"] [data-field-property="required"]').setValue(false);
    await wrapper.get('[data-testid="workflow-form-save"]').trigger('click');
    await flushPromises();

    expect(getWorkflowForm).toHaveBeenCalledWith('form-1', expect.any(AbortSignal));
    expect(getWorkflowDesignerAsset).toHaveBeenCalledOnce();
    expect(saveWorkflowDesignerAsset).toHaveBeenCalledWith(
      'form-1',
      2,
      expect.objectContaining({
        widgetList: [expect.objectContaining({ options: expect.objectContaining({ required: false, label: '摘要', custom: { keep: '原样配置' } }) })],
        formConfig: design.formConfig
      })
    );
    expect(wrapper.text()).toContain('Revision 3');
  });

  it('409 冲突保留本地 Draft，发布使用当前修订号并刷新权威对象', async () => {
    vi.mocked(saveWorkflowDesignerAsset).mockRejectedValue({
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
    expect(publishWorkflowForm).toHaveBeenCalledWith('form-1', expect.objectContaining({ expectedRevision: 2, expectedBindingRevision: 2, targets: ['web'], rendererHash: expect.stringMatching(/^[a-f0-9]{64}$/) }));
    expect(getWorkflowForm).toHaveBeenCalledTimes(2);
  });
  it.each([
    { formRevision: 5, assetRevision: 2 },
    { formRevision: 2, assetRevision: 5 }
  ])('详情修订 $formRevision、设计修订 $assetRevision 时保存使用设计资产自带修订', async ({ formRevision, assetRevision }) => {
    vi.mocked(getWorkflowForm).mockResolvedValue({ ...form, draftRevision: formRevision });
    vi.mocked(getWorkflowDesignerAsset).mockResolvedValue({ ...asset, draftRevision: assetRevision });
    if (assetRevision < formRevision) {
      vi.mocked(saveWorkflowDesignerAsset).mockRejectedValue({ status: 409, code: 'workflow.form_revision_conflict', title: '表单已被其他用户修改' });
    } else {
      vi.mocked(saveWorkflowDesignerAsset).mockResolvedValue({ ...asset, draftRevision: assetRevision + 1 });
    }
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-form-save"]').trigger('click'); await flushPromises();
      expect(saveWorkflowDesignerAsset).toHaveBeenCalledWith('form-1', assetRevision, expect.objectContaining({ formConfig: design.formConfig }));
      if (assetRevision < formRevision) {
        expect(wrapper.get('[role="alert"]').text()).toContain('workflow.form_revision_conflict');
        expect(wrapper.findComponent(VForm3DesignerStub).props('designerJson')).toEqual(design);
      } else {
        expect(wrapper.text()).toContain(`Revision ${assetRevision + 1}`);
      }
    } finally { wrapper.unmount(); }
  });
  it('详情读取期间首次创建的设计资产不能被较早的空资产结果替代', async () => {
    let finish!: (value: typeof form) => void;
    const detail = new Promise<typeof form>(resolve => { finish = resolve; });
    vi.mocked(getWorkflowForm).mockReturnValueOnce(detail);
    let persisted: typeof asset | undefined;
    vi.mocked(getWorkflowDesignerAsset).mockImplementation(async () => persisted);
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      const latestDesign = { ...design, formConfig: { ...design.formConfig, customConfig: { keep: '并发新建资产' } } };
      persisted = { ...asset, draftRevision: 3, designerJson: latestDesign };
      finish({ ...form, draftRevision: 3 }); await flushPromises();
      expect(wrapper.findComponent(VForm3DesignerStub).props('designerJson')).toEqual(latestDesign);
      vi.mocked(saveWorkflowDesignerAsset).mockResolvedValue({ ...persisted, draftRevision: 4 });
      await wrapper.get('[data-testid="workflow-form-save"]').trigger('click'); await flushPromises();
      expect(saveWorkflowDesignerAsset).toHaveBeenCalledWith('form-1', 3, expect.objectContaining({ formConfig: latestDesign.formConfig }));
    } finally { finish(form); wrapper.unmount(); }
  });
  it.each(['tenant', 'account', 'session', 'read', 'update'])('%s 上下文变化清除缓存的设计稿和页面数据', async kind => {
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(true);
      vi.mocked(listWorkflowForms).mockResolvedValue([]);
      const session = useSessionStore(); const user = session.currentUser!;
      session.currentUser = { ...user,
        ...(kind === 'tenant' ? { tenantId: 'tenant-2' } : {}), ...(kind === 'account' ? { id: 'user-2' } : {}),
        ...(kind === 'session' ? { sessionId: 'session-2' } : {}),
        ...(kind === 'read' ? { permissions: [] } : {}), ...(kind === 'update' ? { permissions: ['workflow.forms.read'] } : {}) };
      await flushPromises();
      expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false);
      expect(wrapper.find('[data-testid="workflow-form-save"]').exists()).toBe(false);
      expect(wrapper.text()).not.toContain(form.formKey);
    } finally { wrapper.unmount(); }
  });
  it('相同账号作用域的资料刷新保留未保存设计，不重复加载', async () => {
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-field-property="required"]').setValue(false);
      const session = useSessionStore(); session.currentUser = { ...session.currentUser!, displayName: '更新后的昵称', profileVersion: 2 };
      await flushPromises();
      expect(wrapper.get('[data-field-property="required"]').element).toHaveProperty('checked', false);
      expect(listWorkflowForms).toHaveBeenCalledTimes(1);
    } finally { wrapper.unmount(); }
  });
  it('切换上下文中止旧详情读取，迟到结果不能触发资产读取或解除新请求的 busy', async () => {
    let finishDetail!: (value: typeof form) => void; let finishList!: (value: typeof form[]) => void;
    vi.mocked(getWorkflowForm).mockReturnValueOnce(new Promise(resolve => { finishDetail = resolve; }));
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      const signal = vi.mocked(getWorkflowForm).mock.calls[0][1];
      vi.mocked(listWorkflowForms).mockReturnValueOnce(new Promise(resolve => { finishList = resolve; }));
      const session = useSessionStore(); session.currentUser = { ...session.currentUser!, tenantId: 'tenant-2' }; await flushPromises();
      const aborted = signal?.aborted; finishDetail(form); await flushPromises();
      expect(aborted).toBe(true); expect(getWorkflowDesignerAsset).not.toHaveBeenCalled();
      expect(wrapper.get('section.workflow-forms').attributes('aria-busy')).toBe('true');
      finishList([]); await flushPromises();
      expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false);
      expect(wrapper.get('section.workflow-forms').attributes('aria-busy')).toBe('false');
    } finally { finishDetail(form); finishList?.([]); wrapper.unmount(); }
  });
  it('撤销编辑权限后忽略旧资产回执，不重新打开设计器', async () => {
    let finish!: (value: typeof asset) => void;
    vi.mocked(getWorkflowDesignerAsset).mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      const signal = vi.mocked(getWorkflowDesignerAsset).mock.calls[0][1];
      vi.mocked(listWorkflowForms).mockResolvedValue([]);
      const session = useSessionStore(); session.currentUser = { ...session.currentUser!, permissions: ['workflow.forms.read'] }; await flushPromises();
      finish(asset); await flushPromises();
      expect(signal?.aborted).toBe(true); expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false);
    } finally { finish(asset); wrapper.unmount(); }
  });
  it('读取画布期间切换租户不继续发出原编辑器的保存请求', async () => {
    let finish!: () => void; readDesignGate = new Promise(resolve => { finish = resolve; });
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-form-save"]').trigger('click'); await flushPromises();
      vi.mocked(listWorkflowForms).mockResolvedValue([]);
      const session = useSessionStore(); session.currentUser = { ...session.currentUser!, tenantId: 'tenant-2' }; await flushPromises();
      finish(); await flushPromises();
      expect(saveWorkflowDesignerAsset).not.toHaveBeenCalled(); expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false);
    } finally { finish(); wrapper.unmount(); }
  });
  it('保存已经发出后切换租户，迟到成功回执不重开旧设计器或回填旧列表', async () => {
    let finish!: (value: typeof asset) => void;
    vi.mocked(saveWorkflowDesignerAsset).mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-form-save"]').trigger('click'); await flushPromises();
      expect(saveWorkflowDesignerAsset).toHaveBeenCalledTimes(1); vi.mocked(listWorkflowForms).mockResolvedValue([]);
      const session = useSessionStore(); session.currentUser = { ...session.currentUser!, tenantId: 'tenant-2' }; await flushPromises();
      finish({ ...asset, draftRevision: 3 }); await flushPromises();
      expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false); expect(wrapper.text()).not.toContain(form.formKey);
    } finally { finish(asset); wrapper.unmount(); }
  });
  it('删除版本失败时不提示删除成功', async () => {
    vi.mocked(listWorkflowFormVersions).mockResolvedValue([version]);
    vi.mocked(deleteWorkflowFormVersion).mockRejectedValue({ status: 409, code: 'workflow.form_version_in_use', title: '版本正在使用' });
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.delete_version']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-versions"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-form-delete-version"]').trigger('click'); await flushPromises();
      expect(success).not.toHaveBeenCalled(); expect(wrapper.get('[role="alert"]').text()).toContain('workflow.form_version_in_use');
    } finally { success.mockRestore(); wrapper.unmount(); }
  });
  it('删除成功后刷新版本和定义，仅提示一次成功', async () => {
    vi.mocked(listWorkflowFormVersions).mockResolvedValueOnce([version]).mockResolvedValueOnce([]);
    vi.mocked(deleteWorkflowFormVersion).mockResolvedValue(undefined);
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.delete_version']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-versions"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-form-delete-version"]').trigger('click'); await flushPromises();
      expect(deleteWorkflowFormVersion).toHaveBeenCalledExactlyOnceWith('version-1');
      expect(wrapper.find('[data-testid="workflow-form-delete-version"]').exists()).toBe(false);
      expect(success).toHaveBeenCalledTimes(1); expect(getWorkflowForm).toHaveBeenCalledWith(form.id, expect.any(AbortSignal));
    } finally { success.mockRestore(); wrapper.unmount(); }
  });
  it('上下文切换期间不读取，切换失败恢复原上下文后重新加载', async () => {
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
      const session = useSessionStore(); session.switching = true; await flushPromises();
      expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false); expect(listWorkflowForms).toHaveBeenCalledTimes(1);
      session.switching = false; await flushPromises();
      expect(listWorkflowForms).toHaveBeenCalledTimes(2); expect(wrapper.text()).toContain(form.formKey);
      expect(wrapper.findComponent(VForm3DesignerStub).exists()).toBe(false);
    } finally { wrapper.unmount(); }
  });
  it('页面卸载后中止读取，忽略迟到错误且不发起下一条资产请求', async () => {
    let fail!: (error: Error) => void;
    vi.mocked(getWorkflowForm).mockReturnValueOnce(new Promise((_resolve, reject) => { fail = reject; }));
    const wrapper = mountWithPermissions(['workflow.forms.read', 'workflow.forms.update']);
    await flushPromises(); await wrapper.get('[data-testid="workflow-form-edit"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(getWorkflowForm).mock.calls[0][1]; wrapper.unmount();
    fail(new Error('old-request-failed')); await flushPromises();
    expect(signal?.aborted).toBe(true); expect(getWorkflowDesignerAsset).not.toHaveBeenCalled();
  });
});
