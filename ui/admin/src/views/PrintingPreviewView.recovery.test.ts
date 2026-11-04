import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElMessage } from 'element-plus';
import type { PrintingTemplate, PrintingTemplateSummary } from '@fullnet/client-contracts';
import View from './PrintingPreviewView.vue';
import * as api from '../api/printing-templates';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, printTemplate } from '../test/data-output-fixtures';
import { hostPrintingDraft, hostPrintingVersion } from '../test/printing-host-fixtures';
vi.mock('../api/printing-templates', () => ({ createPrintingTemplate: vi.fn(), listPrintingTemplates: vi.fn(), previewPrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn(), getPrintingTemplate: vi.fn(), getPrintingTemplateVersion: vi.fn(), listPrintingTemplateVersions: vi.fn(), updatePrintingTemplate: vi.fn(), getPrintingFormSchema: vi.fn() }));
const permissions = ['read', 'create', 'publish', 'preview'].map(action => 'printing.templates.' + action);
const other = { ...hostPrintingDraft, id: '01980000-0000-7000-8000-000000000003', templateKey: 'other', name: '其他模板' };
const created = { ...hostPrintingDraft, id: '01980000-0000-7000-8000-000000000004', templateKey: 'created-fixture', name: '新建模板' };
const summary = ({ layoutHtml: _, ...row }: PrintingTemplate): PrintingTemplateSummary => row;
let catalog: PrintingTemplate[], wrapper: VueWrapper;
const find = (id: string) => catalog.find(row => row.id.toLowerCase() === id.toLowerCase())!;
function body(row: PrintingTemplate, number = row.latestPublishedVersionNumber) {
  const offset = row.id === other.id ? 100 : row.id === created.id ? 200 : 0;
  return { ...hostPrintingVersion({ ...row, latestPublishedVersionNumber: number - 1 }), id: '019bc2b1-2a40-7cc3-8992-' + (1000 + offset + number).toString(16).padStart(12, '0') };
}
interface State {
  selectedTemplateId: string; createDialogVisible: boolean; publishOpen: boolean; catalogReady: boolean; createBlocked: boolean;
  templates: PrintingTemplateSummary[]; writeReceipt?: { outcome: string; kind: string; templateId?: string; versionNumber?: number }; publicationBlocks: Map<string, number>;
  load: () => Promise<void>; submitCreate: () => Promise<void>; confirmPublish: () => Promise<void>; publishSelected: () => Promise<void>; runPreview: () => Promise<void>;
  acknowledgePublicationRecovery: () => void; recoveryPrompt?: { confirm: () => void };
}
const state = () => wrapper.vm as unknown as State;
async function setup(granted = permissions) { const context = createShareSession(granted); wrapper = mount(View, { global: { plugins: [context.pinia], stubs: { ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>' } } } }); await flushPromises(); return context.session; }
async function openCreate() {
  await wrapper.get('[data-testid="printing-preview-create"]').trigger('click');
  await wrapper.get('[data-testid="printing-template-key"]').setValue(created.templateKey); await wrapper.get('[data-testid="printing-template-name"]').setValue(created.name); await wrapper.get('[data-testid="printing-template-layout"]').setValue(created.layoutHtml);
}
async function publish() { await state().confirmPublish(); await state().publishSelected(); }
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} })); catalog = [{ ...hostPrintingDraft }, { ...other }];
  vi.mocked(api.listPrintingTemplates).mockImplementation(async () => ({ items: catalog.map(summary), total: catalog.length, page: 1, pageSize: 20 }));
  vi.mocked(api.getPrintingTemplate).mockImplementation(async id => ({ ...find(id) }));
  vi.mocked(api.getPrintingTemplateVersion).mockImplementation(async (id, number) => body(find(id), number));
  vi.mocked(api.listPrintingTemplateVersions).mockImplementation(async id => { const target = find(id), items = Array.from({ length: target.latestPublishedVersionNumber }, (_entry, index) => { const { layoutHtml: _, ...row } = body(target, target.latestPublishedVersionNumber - index); return row; }); return { items, total: items.length, page: 1, pageSize: 20 }; });
  // 目录只反映夹具中已确认的服务端写入，保存和发布通过两个独立页面动作发出。
  vi.mocked(api.createPrintingTemplate).mockImplementation(async input => { const row = { ...created, ...input }; catalog.push(row); return row; });
  vi.mocked(api.publishPrintingTemplate).mockImplementation(async (id, input) => { const target = find(id), result = { ...body(target, target.latestPublishedVersionNumber + 1), changeNote: input.changeNote ?? null }; catalog = catalog.map(row => row.id === target.id ? { ...row, version: input.version + 1, latestPublishedVersionNumber: result.versionNumber } : row); return result; });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
describe('Host 打印创建发布恢复', () => {
  it('创建成功关闭表单，单独发布失败保留草稿及发布未知回执', async () => {
    await setup(); await openCreate(); await state().submitCreate(); expect(state().writeReceipt).toMatchObject({ kind: 'create', outcome: 'succeeded', templateId: created.id }); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
    vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await publish(); expect(state().createDialogVisible).toBe(false); expect(state().createBlocked).toBe(false); expect(state().writeReceipt).toMatchObject({ kind: 'publish', outcome: 'unconfirmed', templateId: created.id });
    await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('未知发布同修订刷新仍保护，核对后只重试发布而不重新创建', async () => {
    await setup(); await openCreate(); await state().submitCreate(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await publish();
    await state().load(); await publish(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
    state().acknowledgePublicationRecovery(); state().recoveryPrompt!.confirm(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce(); await publish();
    expect(api.createPrintingTemplate).toHaveBeenCalledOnce(); expect(api.publishPrintingTemplate).toHaveBeenLastCalledWith(created.id, { version: 1, changeNote: null }, expect.any(AbortSignal));
  });
  it('刷新确认已发布仅更新目录，不自动再次发布草稿', async () => {
    await setup(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await publish(); catalog[0] = { ...catalog[0]!, version: 2, latestPublishedVersionNumber: 1 };
    await state().load(); await state().publishSelected(); expect(state().templates[0]).toMatchObject({ version: 2, latestPublishedVersionNumber: 1 }); expect(state().publicationBlocks.has(printTemplate.id)).toBe(false); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('发布未知后目录刷新失败不解除再次发布门槛', async () => {
    await setup(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await publish(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('offline'));
    await state().load(); await publish(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce(); expect(state().catalogReady).toBe(false); expect(state().writeReceipt?.outcome).toBe('unconfirmed');
  });
  it('切换并发布其他草稿不能覆盖原模板的未知发布门槛', async () => {
    await setup(); vi.mocked(api.publishPrintingTemplate).mockRejectedValue(new Error('lost')); await publish(); state().selectedTemplateId = other.id; await publish(); state().selectedTemplateId = printTemplate.id; await publish();
    expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2); expect(state().publicationBlocks.has(printTemplate.id)).toBe(true);
  });
  it('其他回执覆盖后已确认发布仍不能被陈旧补读或旧确认重发', async () => {
    await setup(); await state().confirmPublish(); await flushPromises(); const old = wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'printing-template-confirm-publish')!.vm.$.vnode.props!.onClick as () => void;
    vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('offline')); await state().publishSelected(); await state().load(); state().selectedTemplateId = other.id;
    vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await publish(); state().selectedTemplateId = printTemplate.id;
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce({ items: [summary(hostPrintingDraft), summary(other)], total: 2, page: 1, pageSize: 20 }); await state().load(); old(); await flushPromises();
    expect(state().templates[0]).toMatchObject({ version: 2, latestPublishedVersionNumber: 1 }); expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it('草稿发布在途切换主体后丢弃迟到结果和回执', async () => {
    const session = await setup(), pending = deferred<ReturnType<typeof hostPrintingVersion>>(); await state().confirmPublish(); vi.mocked(api.publishPrintingTemplate).mockReturnValueOnce(pending.promise);
    const action = state().publishSelected(), signal = vi.mocked(api.publishPrintingTemplate).mock.lastCall![2]!; session.currentUser!.sessionId = 'next'; expect(signal.aborted).toBe(true); pending.resolve(body(hostPrintingDraft, 1)); await action; expect(state().writeReceipt).toBeUndefined();
  });
  it('缺少发布权限不能直接发布已有草稿', async () => { await setup(permissions.filter(code => code !== 'printing.templates.publish')); await publish(); expect(api.publishPrintingTemplate).not.toHaveBeenCalled(); });
  it('关闭在途独立发布保留已创建但发布未知的回执', async () => {
    await setup(); await openCreate(); await state().submitCreate(); await state().confirmPublish(); const pending = deferred<ReturnType<typeof hostPrintingVersion>>(); vi.mocked(api.publishPrintingTemplate).mockReturnValueOnce(pending.promise);
    const action = state().publishSelected(); state().publishOpen = false; pending.resolve(body(created, 1)); await action; expect(state().writeReceipt).toMatchObject({ kind: 'publish', outcome: 'unconfirmed', templateId: created.id }); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('旧创建窗口关闭和提交回调不能影响新窗口', async () => {
    await setup(); await openCreate(); const buttons = wrapper.findAllComponents(ElButton), submit = buttons.find(item => item.attributes('data-testid') === 'printing-preview-submit')!.vm.$.vnode.props!.onClick as () => void, close = buttons.find(item => item.attributes('data-testid') === 'printing-preview-create-back')!.vm.$.vnode.props!.onClick as () => void;
    state().createDialogVisible = false; await flushPromises(); await openCreate(); close(); submit(); await flushPromises(); expect(state().createDialogVisible).toBe(true); expect(api.createPrintingTemplate).not.toHaveBeenCalled();
  });
  it('发布版本变化取消等待正文，不接入旧版本内容', async () => {
    catalog[0] = { ...printTemplate }; await setup(); const pending = deferred<ReturnType<typeof hostPrintingVersion>>(); vi.mocked(api.getPrintingTemplateVersion).mockReturnValueOnce(pending.promise); const action = state().runPreview(), signal = vi.mocked(api.getPrintingTemplateVersion).mock.lastCall![2]!;
    catalog[0] = { ...printTemplate, version: 2, latestPublishedVersionNumber: 2 }; await state().load(); expect(signal.aborted).toBe(true); pending.resolve(body(printTemplate, 1)); await action; expect(wrapper.find('iframe').exists()).toBe(false);
  });
  it('Host 版式接受等价UUID，拒绝其他模板版本归属', async () => {
    catalog[0] = { ...printTemplate }; await setup(); state().selectedTemplateId = printTemplate.id.toUpperCase(); await state().runPreview(); await flushPromises(); expect(wrapper.get('iframe').attributes('srcdoc')).toContain('夹具');
    vi.mocked(api.getPrintingTemplateVersion).mockResolvedValueOnce({ ...body(printTemplate, 1), templateId: other.id }); await state().runPreview(); await flushPromises(); expect(wrapper.find('iframe').exists()).toBe(false);
  });
});
