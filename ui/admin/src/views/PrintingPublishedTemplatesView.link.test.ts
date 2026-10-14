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
  router = createRouter({ history: createMemoryHistory(), routes: [{ name: 'printing-published-templates', path: '/printing', component: { render: () => null } }] });
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

describe('业务列表到已授权打印目录的记录定位', () => {
  it('仅选择同表单的已授模板并填入记录，不自动预览或打印', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {});
    await create();
    expect(wrapper.getComponent(ElSelect).props('modelValue')).toBe(outputId + ':2');
    expect(wrapper.get('[data-testid="printing-published-record"]').element).toHaveProperty('value', record);
    expect(wrapper.text()).not.toContain('档案');
    expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
    expect(print).not.toHaveBeenCalled();
  });
  it('同表单切换已授版本保留记录并精确提交', async () => {
    await create();
    const select = wrapper.getComponent(ElSelect);
    select.vm.$emit('update:modelValue', outputId + ':1'); select.vm.$emit('change');
    vi.mocked(previewPrintingPublishedTemplate).mockResolvedValue({ ...printResult, formSchemaKey: schema, versionNumber: 1 });
    await flushPromises();
    await wrapper.get('[data-testid="printing-published-preview"]').trigger('click'); await flushPromises();
    expect(previewPrintingPublishedTemplate).toHaveBeenCalledWith(outputId, { versionNumber: 1, recordId: record }, expect.any(AbortSignal));
  });
  it.each(['malformed', 'ungranted'])('无效定位 %s 不回退到档案或其它业务模板', async reason => {
    await create({ formSchemaKey: reason === 'ungranted' ? 'unknown.schema' : schema, recordId: reason === 'malformed' ? 'bad-id' : record });
    expect(wrapper.getComponent(ElSelect).props('modelValue')).toBe('');
    expect(wrapper.find('[data-testid="printing-published-record"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="printing-published-preview"]').attributes('disabled')).toBeDefined();
    expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
  });
  it('记录路由变化取消旧预览，迟到结果不能覆盖新记录', async () => {
    await create();
    const pending = deferred<typeof printResult>(); vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise);
    await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');
    const signal = vi.mocked(previewPrintingPublishedTemplate).mock.calls[0]![2]!;
    await router.replace({ query: { formSchemaKey: schema, recordId: nextRecord } }); await flushPromises();
    pending.resolve({ ...printResult, formSchemaKey: schema, versionNumber: 2, html: '<p>旧记录敏感字段</p>' }); await flushPromises();
    expect(signal.aborted).toBe(true);
    expect(wrapper.text()).not.toContain('旧记录敏感字段');
    expect(wrapper.get('[data-testid="printing-published-record"]').element).toHaveProperty('value', nextRecord);
  });
  it('租户变化不自动重放旧记录链接，显式新链接才重新定位', async () => {
    const session = await create();
    const tenantId = '01980000-0000-7000-8000-000000000004';
    session.currentUser = { ...session.currentUser!, tenantId, scope: `tenant:${tenantId.replaceAll('-', '')}` };
    await flushPromises();
    expect(wrapper.getComponent(ElSelect).props('modelValue')).toBe('');
    expect(wrapper.find('[data-testid="printing-published-record"]').exists()).toBe(false);
    await router.replace({ query: { formSchemaKey: schema, recordId: nextRecord } }); await flushPromises();
    expect(wrapper.get('[data-testid="printing-published-record"]').element).toHaveProperty('value', nextRecord);
    expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
  });
  it('迟到目录不能接管新路由的模板与记录', async () => {
    const pending = deferred<typeof business[]>(); vi.mocked(listPrintingPublishedTemplates).mockReturnValueOnce(pending.promise);
    await create();
    const signal = vi.mocked(listPrintingPublishedTemplates).mock.calls[0]![0]!;
    await router.replace({ query: { formSchemaKey: schema, recordId: nextRecord } }); await flushPromises();
    pending.resolve([{ ...business, templateId: nextRecord, templateName: '迟到模板' }]); await flushPromises();
    expect(signal.aborted).toBe(true);
    expect(wrapper.getComponent(ElSelect).props('modelValue')).toBe(outputId + ':2');
    expect(wrapper.text()).not.toContain('迟到模板');
  });
  it('没有目录读取权限时不读取链接或生成预览', async () => {
    await create(undefined, ['printing.published_templates.preview']);
    expect(listPrintingPublishedTemplates).not.toHaveBeenCalled();
    expect(previewPrintingPublishedTemplate).not.toHaveBeenCalled();
  });
  it('KeepAlive 停用取消旧预览，恢复不重放旧记录，显式新链接可重新定位', async () => {
    await create(undefined, undefined, true);
    const pending = deferred<typeof printResult>(); vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise);
    await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');
    const signal = vi.mocked(previewPrintingPublishedTemplate).mock.calls[0]![2]!;
    visible.value = false; await flushPromises();
    expect(signal.aborted).toBe(true);
    visible.value = true; await flushPromises();
    expect(listPrintingPublishedTemplates).toHaveBeenCalledTimes(2);
    expect(wrapper.getComponent(ElSelect).props('modelValue')).toBe('');
    expect(wrapper.find('[data-testid="printing-published-record"]').exists()).toBe(false);
    await router.replace({ query: { formSchemaKey: schema, recordId: nextRecord } }); await flushPromises();
    pending.resolve({ ...printResult, formSchemaKey: schema, versionNumber: 2, html: '<p>停用前的敏感内容</p>' }); await flushPromises();
    expect(wrapper.get('[data-testid="printing-published-record"]').element).toHaveProperty('value', nextRecord);
    expect(wrapper.text()).not.toContain('停用前的敏感内容');
    expect(previewPrintingPublishedTemplate).toHaveBeenCalledOnce();
  });
});
