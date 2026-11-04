import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElInput, ElSelect } from 'element-plus';
import { watch } from 'vue';
import { createMemoryHistory, createRouter, type Router, type LocationQueryRaw } from 'vue-router';
import View from './PrintingPublishedTemplatesView.vue';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate } from '../api/printing-templates';
import { createOutputSession, deferred, outputId, printResult } from '../test/data-output-fixtures';
vi.mock('../api/printing-templates', () => ({ listPrintingPublishedTemplates: vi.fn(), previewPrintingPublishedTemplate: vi.fn() }));
const schema = 'enterprise_request.request_summary', record = '01980000-0000-7000-8000-000000000002', other = '01980000-0000-7000-8000-000000000003';
const template = { templateId: outputId, templateKey: 'fixture', templateName: '已授权模板', versionNumber: 2, formSchemaKey: schema, requiresRecordId: true };
let wrapper: VueWrapper, router: Router;
interface State { templates: typeof template[]; selectedId: string; recordId: string; loading: boolean; previewing: boolean; preview?: typeof printResult; catalogReady: boolean; load: (force?: boolean) => Promise<void>; render: (print?: boolean) => Promise<void> }
const state = () => wrapper.vm as unknown as State;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([template, { ...template, versionNumber: 1 }]); vi.mocked(previewPrintingPublishedTemplate).mockResolvedValue({ ...printResult, versionNumber: 2, formSchemaKey: schema }); });
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(query: LocationQueryRaw = {}) {
  const context = createOutputSession(['printing.published_templates.read', 'printing.published_templates.preview']); router = createRouter({ history: createMemoryHistory(), routes: [{ name: 'printing-published-templates', path: '/printing', component: { render: () => null } }, { name: 'other', path: '/other', component: { render: () => null } }] }); await router.push({ name: 'printing-published-templates', query });
  wrapper = mount(View, { global: { plugins: [context.pinia, router], directives: { loading: () => {} } } }); await flushPromises(); return context;
}
describe('已授权打印目录、预览和浏览器操作恢复', () => {
  it('目录首次失败有独立错误，刷新可恢复且不自动预览或打印', async () => {
    vi.mocked(listPrintingPublishedTemplates).mockRejectedValueOnce(new Error('offline')); const print = vi.spyOn(window, 'print').mockImplementation(() => {}); await setup(); expect(wrapper.find('[data-testid="printing-published-catalog-problem"]').exists()).toBe(true); await wrapper.get('[data-testid="printing-published-refresh"]').trigger('click'); await flushPromises(); expect(state().catalogReady).toBe(true); expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled(); expect(print).not.toHaveBeenCalled();
  });
  it('同目录重读保留用户所选版本和手工记录，但清旧预览', async () => {
    await setup(); state().selectedId = outputId + ':1'; state().recordId = record; vi.mocked(previewPrintingPublishedTemplate).mockResolvedValueOnce({ ...printResult, formSchemaKey: schema }); await state().render(); await state().load(); expect(state().selectedId).toBe(outputId + ':1'); expect(state().recordId).toBe(record); expect(state().preview).toBeUndefined(); expect(previewPrintingPublishedTemplate).toHaveBeenCalledOnce();
  });
  it('相同绑定目录刷新保留手工修正的记录，不重放URL旧记录', async () => {
    await setup({ formSchemaKey: schema, recordId: record }); await wrapper.getComponent(ElInput).setValue(other); await state().load(); expect(state().recordId).toBe(other);
  });
  it('同表单版本切换保留手工记录，不能覆盖成URL默认值', async () => {
    await setup({ formSchemaKey: schema, recordId: record }); await wrapper.getComponent(ElInput).setValue(other); wrapper.getComponent(ElSelect).vm.$emit('update:modelValue', outputId + ':1'); await flushPromises(); expect(state().recordId).toBe(other);
  });
  it('目录重读失败保留选择和记录但停止预览，改输入不能吞掉目录错误', async () => {
    await setup(); state().recordId = record; vi.mocked(listPrintingPublishedTemplates).mockRejectedValueOnce(new Error('offline')); await state().load(); expect(state().selectedId).toBe(outputId + ':2'); expect(state().recordId).toBe(record); await state().render(); expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled(); await wrapper.getComponent(ElInput).setValue(other); expect(wrapper.find('[data-testid="printing-published-catalog-problem"]').exists()).toBe(true); await state().load(); await state().render(); expect(previewPrintingPublishedTemplate).toHaveBeenCalledOnce();
  });
  it.each([{ templateId: '00000000-0000-0000-0000-000000000000' }, { versionNumber: 0 }, { formSchemaKey: '' }])('无效目录资源%s不能成为预览目标', async change => {
    vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([{ ...template, ...change }]); await setup(); expect(state().catalogReady).toBe(false); await state().render(); expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
  });
  it('重复版本拒绝，而不是静默选择第一个', async () => {
    vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([template, { ...template, templateId: outputId.toUpperCase() }]); await setup(); expect(state().selectedId).toBe(''); expect(state().catalogReady).toBe(false);
  });
  it('所选资源从目录消失后不得把旧记录带到不同表单', async () => {
    await setup(); state().recordId = record; vi.mocked(listPrintingPublishedTemplates).mockResolvedValueOnce([{ ...template, templateId: other, formSchemaKey: 'other.schema' }]); await state().load(); expect(state().recordId).toBe(''); expect(state().selectedId).toBe(other + ':2');
  });
  it('相同身份目录的绑定表单发生变化也清空原记录', async () => {
    await setup(); state().recordId = record; state().templates = [{ ...template, formSchemaKey: 'other.schema' }]; await flushPromises(); expect(state().recordId).toBe('');
  });
  it('目录重读期间旧刷新、选择和记录回调均无效', async () => {
    await setup(); const oldRefresh = button('printing-published-refresh').vm.$.vnode.props!.onClick as () => void, oldRecord = wrapper.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void, oldSelect = wrapper.getComponent(ElSelect).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    await state().load(); oldRefresh(); oldRecord(record); oldSelect(outputId + ':1'); await flushPromises(); expect(listPrintingPublishedTemplates).toHaveBeenCalledTimes(2); expect(state().recordId).toBe(''); expect(state().selectedId).toBe(outputId + ':2');
  });
  it('预览完成后旧预览和打印回调不能再次发起请求', async () => {
    await setup(); state().recordId = record; await state().render(); await flushPromises(); const oldPreview = button('printing-published-preview').vm.$.vnode.props!.onClick as () => void, oldPrint = button('printing-published-print').vm.$.vnode.props!.onClick as () => void; await state().render(); oldPreview(); oldPrint(); await flushPromises(); expect(previewPrintingPublishedTemplate).toHaveBeenCalledTimes(2);
  });
  it('刷新目录取消在途预览，旧finally不能结束新目录loading', async () => {
    await setup(); state().recordId = record; const preview = deferred<typeof printResult>(), catalog = deferred<typeof template[]>(); vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(preview.promise); const rendering = state().render(); const signal = vi.mocked(previewPrintingPublishedTemplate).mock.calls[0]![2]!; vi.mocked(listPrintingPublishedTemplates).mockReturnValueOnce(catalog.promise); const reading = state().load(); expect(signal.aborted).toBe(true); preview.resolve({ ...printResult, formSchemaKey: schema, versionNumber: 2 }); await rendering; expect(state().loading).toBe(true); expect(state().preview).toBeUndefined(); catalog.resolve([template]); await reading;
  });
  it('预览失败后可以重试，错误与目录错误分开', async () => {
    await setup(); state().recordId = record; vi.mocked(previewPrintingPublishedTemplate).mockRejectedValueOnce(new Error('render')); await state().render(); expect(wrapper.find('[data-testid="printing-published-preview-problem"]').exists()).toBe(true); expect(wrapper.find('[data-testid="printing-published-catalog-problem"]').exists()).toBe(false); await state().render(); expect(state().preview).toBeDefined();
  });
  it('打印重验被拒绝时清除旧内容且不调用浏览器打印', async () => {
    await setup(); state().recordId = record; await state().render(); const print = vi.spyOn(window, 'print').mockImplementation(() => {}); vi.mocked(previewPrintingPublishedTemplate).mockRejectedValueOnce({ status: 403, code: 'denied', title: 'Denied' }); await state().render(true); expect(state().preview).toBeUndefined(); expect(print).not.toHaveBeenCalled();
  });
  it('浏览器打印异常保留新净化预览，后续打印仍重验服务器', async () => {
    await setup(); state().recordId = record; await state().render(); const print = vi.spyOn(window, 'print').mockImplementationOnce(() => { throw new Error('print'); }).mockImplementation(() => {}); await state().render(true); expect(state().preview).toBeDefined(); await state().render(true); expect(previewPrintingPublishedTemplate).toHaveBeenCalledTimes(3); expect(print).toHaveBeenCalledTimes(2);
  });
  it('预览回包交付前切换目标不能触发打印', async () => {
    await setup(); state().recordId = record; await state().render(); const print = vi.spyOn(window, 'print').mockImplementation(() => {}); vi.mocked(previewPrintingPublishedTemplate).mockImplementationOnce(async () => { queueMicrotask(() => { state().recordId = other; }); return { ...printResult, formSchemaKey: schema, versionNumber: 2 }; }); await state().render(true); expect(print).not.toHaveBeenCalled();
  });
  it('预览已赋值但DOM提交前切换记录仍阻止打印', async () => {
    await setup(); state().recordId = record; await state().render(); const print = vi.spyOn(window, 'print').mockImplementation(() => {});
    const stop = watch(() => state().preview, value => { if (value) state().recordId = other; }, { flush: 'sync' }); await state().render(true); stop(); expect(print).not.toHaveBeenCalled(); expect(state().preview).toBeUndefined();
  });
  it('新记录路由读取失败不得保留旧记录或旧目录', async () => {
    await setup({ formSchemaKey: schema, recordId: record }); vi.mocked(listPrintingPublishedTemplates).mockRejectedValueOnce(new Error('offline')); await router.replace({ query: { formSchemaKey: schema, recordId: other } }); await flushPromises(); expect(state().templates).toEqual([]); expect(state().recordId).toBe(''); expect(state().catalogReady).toBe(false);
  });
  it('无链接切换为显式空链接必须重新判定，不能复用无链接目录', async () => {
    await setup(); state().recordId = record; await router.replace({ query: { formSchemaKey: null, recordId: null } }); await flushPromises(); expect(state().catalogReady).toBe(false); expect(state().selectedId).toBe(''); expect(state().recordId).toBe(''); expect(listPrintingPublishedTemplates).toHaveBeenCalledTimes(2);
  });
  it('会话变更后同记录链接刷新不能自动重放；显式新链接才定位', async () => {
    const { session } = await setup({ formSchemaKey: schema, recordId: record }); session.currentUser!.sessionId = other; await flushPromises(); await state().load(); expect(state().selectedId).toBe(''); expect(state().recordId).toBe(''); await router.replace({ query: { formSchemaKey: schema, recordId: other } }); await flushPromises(); expect(state().recordId).toBe(other);
  });
  it('旧目录finally不能结束新路由的目录读取', async () => {
    await setup(); const old = deferred<typeof template[]>(), latest = deferred<typeof template[]>(); vi.mocked(listPrintingPublishedTemplates).mockReturnValueOnce(old.promise).mockReturnValueOnce(latest.promise); const reading = state().load(); await router.replace({ query: { formSchemaKey: schema, recordId: other } }); old.resolve([template]); await reading; expect(state().loading).toBe(true); latest.resolve([template]); await flushPromises(); expect(state().recordId).toBe(other);
  });
});
