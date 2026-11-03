import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import PrintingPreviewView from './PrintingPreviewView.vue';
import { useSessionStore } from '../auth/session';
import { ElSelect, ElPopconfirm } from 'element-plus';
import { listPrintingTemplates, getPrintingTemplate, getPrintingTemplateVersion, createPrintingTemplate, updatePrintingTemplate, publishPrintingTemplate, listPrintingTemplateVersions, getPrintingFormSchema } from '../api/printing-templates';

vi.mock('../api/printing-templates', () => ({
  listPrintingTemplates: vi.fn(),
  createPrintingTemplate: vi.fn(),
  updatePrintingTemplate: vi.fn(),
  publishPrintingTemplate: vi.fn(),
  getPrintingFormSchema: vi.fn(),
  previewPrintingTemplate: vi.fn(), getPrintingTemplate: vi.fn(), getPrintingTemplateVersion: vi.fn(), listPrintingTemplateVersions: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 })
}));

const listMock = vi.mocked(listPrintingTemplates);
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const template = (version = 4) => ({ id, templateKey: 'review-template', name: '测试模板', formSchemaKey: 'printing.tenant_profile_card', layoutHtml: '<h1>Original</h1>',
  latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version });
const publication = () => ({ id, templateId: id, versionNumber: 1, layoutHtml: '<h1>Published</h1>', changeNote: null, publishedByUserId: id, publishedAtUtc: '2026-11-03T00:00:00Z' });
const wrappers: VueWrapper[] = [];
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  const wrapper = mount(PrintingPreviewView, { global: { plugins: [pinia], stubs: {
    ElDialog: { props: ['modelValue', 'title'], template: '<aside v-if="modelValue"><h2>{{ title }}</h2><slot/><slot name="footer"/></aside>' }
  } } }); wrappers.push(wrapper); return wrapper;
}

describe('PrintingPreviewView', () => {
  beforeEach(() => {
    vi.resetAllMocks(); listMock.mockResolvedValue({ items: [template()], page: 1, pageSize: 20, total: 1 });
    vi.mocked(getPrintingTemplate).mockResolvedValue(template(7)); vi.mocked(getPrintingTemplateVersion).mockResolvedValue(publication());
    vi.mocked(getPrintingFormSchema).mockResolvedValue({ formSchemaKey: 'printing.tenant_profile_card', displayName: '租户档案卡片', description: '', fields: [{ fieldKey: 'tenantName', displayName: '租户名称' }] });
    const { layoutHtml: _, ...summary } = publication(); vi.mocked(listPrintingTemplateVersions).mockResolvedValue({ items: [summary], page: 1, pageSize: 20, total: 1 });
  });

  it('shows preview action entry point', async () => {
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.preview']);
    await flushPromises();
    expect(wrapper.find('[data-testid="printing-preview-run"]').exists()).toBe(true);
  });

  it('removes executable template markup before inserting preview into the admin DOM', async () => {
    vi.mocked(getPrintingTemplateVersion).mockResolvedValue({ ...publication(), layoutHtml: '<div style="color:red">安全正文</div><img src="x" onerror="alert(1)"><svg onload="alert(2)"></svg><a href="javascript:alert(3)">链接</a>' });
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.preview']);
    await flushPromises();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', '019bc2b1-2a40-7cc3-8992-a80de51bf299');
    await flushPromises();
    await wrapper.get('[data-testid="printing-preview-run"]').trigger('click');
    await flushPromises();
    const surface = wrapper.get('.printing-preview-html');
    const document = new DOMParser().parseFromString(surface.attributes('srcdoc')!, 'text/html');
    expect(document.body.textContent).toContain('安全正文'); expect(document.querySelector('[onerror], [onload], svg, [href^="javascript:"]')).toBeNull();
    expect(document.querySelector('div')?.getAttribute('style')).toContain('color'); expect(surface.attributes('sandbox')).not.toContain('allow-scripts');
  });
  it('Host 读取资格显示配置，动作权限分别控制，切租户清空并停止请求', async () => {
    const wrapper = mountWithPermissions(['printing.templates.read']); await flushPromises(); expect(wrapper.text()).toContain('草稿并发版本');
    expect(wrapper.find('[data-testid="printing-preview-run"]').exists()).toBe(false); expect(wrapper.find('[data-testid="printing-template-publish"]').exists()).toBe(false);
    useSessionStore().currentUser = { ...useSessionStore().currentUser!, tenantId: id, actorScope: 'tenant', scope: 'tenant' }; await flushPromises();
    expect(wrapper.find('[data-testid="printing-preview-template"]').exists()).toBe(false); expect(listMock).toHaveBeenCalledTimes(1);
  });
  it('新建只保存草稿，即使具有发布权限也不自动发布', async () => {
    vi.mocked(createPrintingTemplate).mockResolvedValue({ ...template(1), latestPublishedVersionNumber: 0 });
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.create', 'printing.templates.publish']); await flushPromises();
    await wrapper.get('[data-testid="printing-preview-create"]').trigger('click');
    await wrapper.get('[data-testid="printing-template-key"]').setValue('new-layout'); await wrapper.get('[data-testid="printing-template-name"]').setValue('新草稿');
    await wrapper.get('[data-testid="printing-preview-submit"]').trigger('click'); await flushPromises();
    expect(createPrintingTemplate).toHaveBeenCalledWith(expect.objectContaining({ templateKey: 'new-layout', name: '新草稿', formSchemaKey: 'printing.tenant_profile_card' }), expect.any(AbortSignal));
    expect(publishPrintingTemplate).not.toHaveBeenCalled(); expect(getPrintingTemplateVersion).not.toHaveBeenCalled();
  });
  it('编辑使用详情中读取的并发版本而非目录版本，保存后不发布', async () => {
    vi.mocked(updatePrintingTemplate).mockResolvedValue(template(8));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.update']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="printing-template-layout"]').setValue('<p>Changed</p>'); await wrapper.get('[data-testid="printing-preview-submit"]').trigger('click'); await flushPromises();
    expect(updatePrintingTemplate).toHaveBeenCalledWith(id, expect.objectContaining({ version: 7, layoutHtml: '<p>Changed</p>' }), expect.any(AbortSignal)); expect(publishPrintingTemplate).not.toHaveBeenCalled();
  });
  it('显式确认发布时使用新鲜草稿版本和版本说明', async () => {
    vi.mocked(publishPrintingTemplate).mockResolvedValue(publication());
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.publish']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-publish"]').trigger('click'); await flushPromises(); expect(publishPrintingTemplate).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="printing-template-change-note"]').setValue('布局更新'); await wrapper.get('[data-testid="printing-template-confirm-publish"]').trigger('click'); await flushPromises();
    expect(publishPrintingTemplate).toHaveBeenCalledWith(id, { version: 7, changeNote: '布局更新' }, expect.any(AbortSignal));
  });
  it('编辑冲突保留本地内容和旧并发版本，确认重新载入后才能保存', async () => {
    vi.mocked(updatePrintingTemplate).mockRejectedValue({ status: 409, code: 'printing.template.concurrency_conflict', title: 'Conflict' });
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.update']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="printing-template-name"]').setValue('保留我的输入');
    await wrapper.get('[data-testid="printing-preview-submit"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="printing-template-name"]').element as HTMLInputElement).value).toBe('保留我的输入'); expect(wrapper.get('[data-testid="printing-preview-submit"]').attributes('disabled')).toBeDefined();
    vi.mocked(getPrintingTemplate).mockResolvedValue({ ...template(9), name: '远端名称' }); wrapper.findComponent(ElPopconfirm).vm.$emit('confirm'); await flushPromises();
    expect((wrapper.get('[data-testid="printing-template-name"]').element as HTMLInputElement).value).toBe('远端名称'); expect(wrapper.get('[data-testid="printing-preview-submit"]').attributes('disabled')).toBeUndefined();
  });
  it('发布结果不明只读刷新后才能再次确认，不重放写请求', async () => {
    vi.mocked(publishPrintingTemplate).mockRejectedValue(new Error('Response lost'));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.publish']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-publish"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="printing-template-confirm-publish"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="printing-template-unconfirmed"]').exists()).toBe(true); expect(publishPrintingTemplate).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="printing-template-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="printing-template-unconfirmed"]').exists()).toBe(false); expect(publishPrintingTemplate).toHaveBeenCalledTimes(1);
  });
  it('撤权取消在途版式读取，迟到正文不能重新显示或打印', async () => {
    let resolve!: (value: ReturnType<typeof publication>) => void; vi.mocked(getPrintingTemplateVersion).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.preview']); await flushPromises(); await wrapper.get('[data-testid="printing-preview-run"]').trigger('click');
    const signal = vi.mocked(getPrintingTemplateVersion).mock.calls[0]![2]!; useSessionStore().currentUser!.permissions = []; await flushPromises();
    expect(signal.aborted).toBe(true); resolve(publication()); await flushPromises(); expect(wrapper.find('iframe').exists()).toBe(false);
  });
  it('选择另一个发布版本会取消旧正文，旧版式不能污染新选择', async () => {
    let resolve!: (value: ReturnType<typeof publication>) => void; vi.mocked(getPrintingTemplateVersion).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.preview']); await flushPromises(); await wrapper.get('[data-testid="printing-preview-run"]').trigger('click');
    const signal = vi.mocked(getPrintingTemplateVersion).mock.calls[0]![2]!;
    wrapper.findAllComponents(ElSelect)[1]!.vm.$emit('update:modelValue', 2); await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(publication()); await flushPromises(); expect(wrapper.find('iframe').exists()).toBe(false);
    vi.mocked(getPrintingTemplateVersion).mockResolvedValue({ ...publication(), versionNumber: 2, layoutHtml: '<h1>Version two</h1>' });
    await wrapper.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises(); expect(wrapper.get('iframe').attributes('srcdoc')).toContain('Version two');
  });
  it('发布期间失去会话会取消请求，迟到成功不得自动读取或展示旧模板', async () => {
    let resolve!: (value: ReturnType<typeof publication>) => void; vi.mocked(publishPrintingTemplate).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.publish']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-publish"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="printing-template-confirm-publish"]').trigger('click');
    const signal = vi.mocked(publishPrintingTemplate).mock.calls[0]![2]!; useSessionStore().currentUser = undefined; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(publication()); await flushPromises(); expect(listMock).toHaveBeenCalledTimes(1); expect(wrapper.find('[data-testid="printing-preview-template"]').exists()).toBe(false);
  });
  it('写前读取的迟到结果不能解除创建结果未知的操作锁', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof listPrintingTemplates>>) => void; listMock.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    vi.mocked(createPrintingTemplate).mockRejectedValue(new Error('Response lost'));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.create']);
    await wrapper.get('[data-testid="printing-preview-create"]').trigger('click'); await wrapper.get('[data-testid="printing-template-key"]').setValue('new-layout'); await wrapper.get('[data-testid="printing-template-name"]').setValue('Unconfirmed');
    await wrapper.get('[data-testid="printing-preview-submit"]').trigger('click'); await flushPromises();
    expect(listMock.mock.calls[0]![1]!.aborted).toBe(true); resolve({ items: [template()], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper.find('[data-testid="printing-template-unconfirmed"]').exists()).toBe(true); expect(wrapper.get('[data-testid="printing-preview-submit"]').attributes('disabled')).toBeDefined();
    expect(createPrintingTemplate).toHaveBeenCalledTimes(1);
  });
  it('没有字段目录权限仍可编辑原草稿，不请求或显示字段目录', async () => {
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.update']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="printing-template-layout"]').exists()).toBe(true); expect(wrapper.find('[data-testid="printing-template-fields"]').exists()).toBe(false); expect(getPrintingFormSchema).not.toHaveBeenCalled();
  });
  it('按光标选区替换占位字段并恢复光标，不自动保存或发布', async () => {
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.update', 'printing.form_schemas.read']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises();
    const input = wrapper.get('[data-testid="printing-template-layout"]'); await input.setValue('<p>replace</p>'); (input.element as HTMLTextAreaElement).setSelectionRange(3, 10);
    await wrapper.get('[data-field-key="tenantName"]').trigger('click'); await flushPromises();
    expect((input.element as HTMLTextAreaElement).value).toBe('<p>{{tenantName}}</p>'); expect((input.element as HTMLTextAreaElement).selectionStart).toBe(17);
    expect(updatePrintingTemplate).not.toHaveBeenCalled(); expect(publishPrintingTemplate).not.toHaveBeenCalled();
  });
  it('插入不能超过布局上限，选区足够时可以替换且未知保存后不能继续插入', async () => {
    vi.mocked(updatePrintingTemplate).mockRejectedValue(new Error('Response lost'));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.update', 'printing.form_schemas.read']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises();
    const input = wrapper.get('[data-testid="printing-template-layout"]'), element = input.element as HTMLTextAreaElement;
    await input.setValue('x'.repeat(65536)); element.setSelectionRange(65536, 65536); await wrapper.get('[data-field-key="tenantName"]').trigger('click'); await flushPromises(); expect(element.value).toHaveLength(65536); expect(element.value).not.toContain('{{tenantName}}');
    element.setSelectionRange(0, 14); await wrapper.get('[data-field-key="tenantName"]').trigger('click'); await flushPromises(); expect(element.value).toHaveLength(65536); expect(element.value.startsWith('{{tenantName}}')).toBe(true);
    await wrapper.get('[data-testid="printing-preview-submit"]').trigger('click'); await flushPromises(); expect(wrapper.get('[data-field-key="tenantName"]').attributes('disabled')).toBeDefined();
  });
  it('撤销字段目录权限中止读取并关闭旧草稿，迟到字段不重现', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof getPrintingFormSchema>>) => void; vi.mocked(getPrintingFormSchema).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['printing.templates.read', 'printing.templates.update', 'printing.form_schemas.read']); await flushPromises();
    await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises(); const signal = vi.mocked(getPrintingFormSchema).mock.calls[0]![1]!;
    useSessionStore().currentUser!.permissions = ['printing.templates.read', 'printing.templates.update']; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve({ formSchemaKey: 'printing.tenant_profile_card', displayName: '', description: '', fields: [{ fieldKey: 'tenantName', displayName: '租户名称' }] }); await flushPromises(); expect(wrapper.find('[data-field-key]').exists()).toBe(false);
  });
});
