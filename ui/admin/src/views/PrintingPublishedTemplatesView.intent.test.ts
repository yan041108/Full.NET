import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import { createMemoryHistory, createRouter, type Router, type LocationQueryRaw } from 'vue-router';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PrintingPublishedTemplatesView from './PrintingPublishedTemplatesView.vue';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate } from '../api/printing-templates';
import { createOutputSession, deferred, outputId, printResult } from '../test/data-output-fixtures';

vi.mock('../api/printing-templates', () => ({ listPrintingPublishedTemplates: vi.fn(), previewPrintingPublishedTemplate: vi.fn() }));
const schema = 'enterprise_request.request_summary';
const record = '01980000-0000-7000-8000-000000000002';
const nextRecord = '01980000-0000-7000-8000-000000000003';
const business = { templateId: outputId, templateKey: 'request', templateName: '申请摘要', formSchemaKey: schema, versionNumber: 2, requiresRecordId: true };
const profile = { ...business, formSchemaKey: 'printing.tenant_profile_card', requiresRecordId: false, templateName: '档案' };
let wrapper: VueWrapper;
let router: Router;
let visible = ref(true);
async function create(query: LocationQueryRaw = { formSchemaKey: schema, recordId: record }, permissions = ['printing.published_templates.read', 'printing.published_templates.preview'], keepAlive = false) {
  const context = createOutputSession(permissions);
  router = createRouter({ history: createMemoryHistory(), routes: [{ name: 'elsewhere', path: '/elsewhere', component: { render: () => null } }, { name: 'printing-published-templates', path: '/printing', component: { render: () => null } }] });
  await router.push({ name: 'printing-published-templates', query });
  visible = ref(true);
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(PrintingPublishedTemplatesView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : PrintingPublishedTemplatesView, { global: { plugins: [context.pinia, router] } });
  await flushPromises();
  return context.session;
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([profile, business, { ...business, versionNumber: 1 }]);
  vi.mocked(previewPrintingPublishedTemplate).mockResolvedValue({ ...printResult, formSchemaKey: schema, versionNumber: 2 });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });

function state() { return wrapper.vm as unknown as { templates: typeof business[]; selectedId: string; recordId: string; loading: boolean; previewing: boolean; render: (print?: boolean) => Promise<void> }; }
describe('租户打印目录与输出归属', () => {
  it('孤立预览权限不能绕过父Read直接读取内容', async () => {
    await create({}, ['printing.published_templates.preview']); state().templates = [business]; state().selectedId = outputId + ':2'; state().recordId = record; await state().render();
    expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="printing-published-template"]').exists()).toBe(false);
  });
  it('直接改变选择而不触发change也取消旧预览', async () => {
    await create(); const pending = deferred<typeof printResult>(); vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise); const action = state().render();
    state().selectedId = outputId + ':1'; pending.resolve({ ...printResult, versionNumber: 2, formSchemaKey: schema }); await action;
    expect(vi.mocked(previewPrintingPublishedTemplate).mock.calls[0]?.[2]?.aborted).toBe(true); expect(wrapper.text()).not.toContain('旧租户敏感内容');
  });
  it('模板表单或绑定要求变化时丢弃旧预览', async () => {
    await create(); const pending = deferred<typeof printResult>(); vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise); const action = state().render(); state().templates = [{ ...business, formSchemaKey: 'other' }];
    pending.resolve({ ...printResult, versionNumber: 2, formSchemaKey: schema }); await action; expect(wrapper.text()).not.toContain('旧租户敏感内容');
  });
  it('离开路由清除目录内容和忙状态，旧请求不能恢复', async () => {
    const pending = deferred<Awaited<ReturnType<typeof listPrintingPublishedTemplates>>>(); vi.mocked(listPrintingPublishedTemplates).mockReturnValueOnce(pending.promise); await create();
    await router.push({ name: 'elsewhere' }); await flushPromises(); expect(state().templates).toEqual([]); expect(state().loading).toBe(false); await state().render();
    pending.resolve([business]); await flushPromises(); expect(state().templates).toEqual([]); expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
  });
  it('返回路由只读取目录，不自动预览或打印', async () => {
    await create(); const print = vi.spyOn(window, 'print').mockImplementation(() => {}); await router.push({ name: 'elsewhere' }); await flushPromises(); await router.push({ name: 'printing-published-templates', query: { formSchemaKey: schema, recordId: nextRecord } }); await flushPromises();
    expect(state().recordId).toBe(nextRecord); expect(listPrintingPublishedTemplates).toHaveBeenCalledTimes(2); expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled(); expect(print).not.toHaveBeenCalled();
  });
  it('旧记录输入和预览回调不能作用于新会话', async () => {
    const session = await create(); const input = wrapper.findComponent({ name: 'ElInput' }); const update = input.vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    const preview = wrapper.findAllComponents({ name: 'ElButton' }).find(button => button.attributes('data-testid') === 'printing-published-preview')!.vm.$.vnode.props!.onClick as () => void;
    session.currentUser!.sessionId = 'next'; await flushPromises(); update(record); preview(); await flushPromises(); expect(state().recordId).toBe(''); expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
  });
  it('大小写等价的模板身份可定位和预览', async () => {
    await create(); state().selectedId = outputId.toUpperCase() + ':2'; vi.mocked(previewPrintingPublishedTemplate).mockResolvedValueOnce({ ...printResult, templateId: outputId.toUpperCase(), versionNumber: 2, formSchemaKey: schema });
    await state().render(); expect(wrapper.text()).toContain('旧租户敏感内容');
  });
});
