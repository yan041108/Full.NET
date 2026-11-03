import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import PrintingTemplateFields from './PrintingTemplateFields.vue';
import { getPrintingFormSchema } from '../api/printing-templates';

vi.mock('../api/printing-templates', () => ({ getPrintingFormSchema: vi.fn() }));
const schema = (key = 'printing.tenant_profile_card') => ({ formSchemaKey: key, displayName: '目录', description: '固定目录', fields: [{ fieldKey: 'tenantName', displayName: '租户名称' }] });
const wrappers: VueWrapper[] = [];
function render(key = 'printing.tenant_profile_card') { const wrapper = mount(PrintingTemplateFields, { props: { formSchemaKey: key, disabled: false } }); wrappers.push(wrapper); return wrapper; }
beforeEach(() => { vi.mocked(getPrintingFormSchema).mockReset().mockResolvedValue(schema()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.useRealTimers(); });

it('只展示当前目录的字面占位字段，点击发出本地插入事件，写入期间禁用', async () => {
  const wrapper = render(); await flushPromises(); expect(wrapper.text()).toContain('{{tenantName}}');
  await wrapper.get('[data-field-key="tenantName"]').trigger('click'); expect(wrapper.emitted('insert')).toEqual([['tenantName']]);
  await wrapper.setProps({ disabled: true }); await wrapper.get('[data-field-key="tenantName"]').trigger('click'); expect(wrapper.emitted('insert')).toHaveLength(1);
});
it('目录失败保留人工重试入口，不自动重放读取', async () => {
  vi.mocked(getPrintingFormSchema).mockRejectedValueOnce(new Error('offline')); const wrapper = render(); await flushPromises();
  expect(wrapper.text()).toContain('现有布局仍可编辑'); expect(wrapper.find('[data-field-key]').exists()).toBe(false); expect(getPrintingFormSchema).toHaveBeenCalledTimes(1);
  await wrapper.get('[data-testid="printing-template-fields-retry"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-field-key="tenantName"]').exists()).toBe(true);
});
it('切换 Schema 中止旧目录，迟到字段不能混入当前草稿', async () => {
  let resolve!: (value: ReturnType<typeof schema>) => void; vi.mocked(getPrintingFormSchema).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  const wrapper = render(); const signal = vi.mocked(getPrintingFormSchema).mock.calls[0]![1]!;
  vi.mocked(getPrintingFormSchema).mockResolvedValue({ ...schema('printing.other'), fields: [{ fieldKey: 'newField', displayName: '新字段' }] });
  await wrapper.setProps({ formSchemaKey: 'printing.other' }); await flushPromises(); expect(signal.aborted).toBe(true);
  resolve(schema()); await flushPromises(); expect(wrapper.find('[data-field-key="tenantName"]').exists()).toBe(false); expect(wrapper.find('[data-field-key="newField"]').exists()).toBe(true);
});
it('关闭编辑器会中止目录请求，不产生迟到插入事件', async () => {
  let resolve!: (value: ReturnType<typeof schema>) => void; vi.mocked(getPrintingFormSchema).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  const wrapper = render(); const signal = vi.mocked(getPrintingFormSchema).mock.calls[0]![1]!; wrapper.unmount(); wrappers.splice(wrappers.indexOf(wrapper), 1);
  expect(signal.aborted).toBe(true); resolve(schema()); await flushPromises(); expect(wrapper.emitted('insert')).toBeUndefined();
});
it('十秒超时取消请求并暂停，迟到响应不能移除错误或恢复字段', async () => {
  vi.useFakeTimers(); let resolve!: (value: ReturnType<typeof schema>) => void;
  vi.mocked(getPrintingFormSchema).mockImplementationOnce(() => new Promise(done => { resolve = done; })); const wrapper = render(); const signal = vi.mocked(getPrintingFormSchema).mock.calls[0]![1]!;
  await vi.advanceTimersByTimeAsync(10_001); expect(signal.aborted).toBe(true); resolve(schema()); await flushPromises();
  expect(wrapper.get('[data-testid="printing-template-fields"]').attributes('aria-busy')).toBe('false'); expect(wrapper.find('[data-field-key]').exists()).toBe(false);
  expect(wrapper.find('[data-testid="printing-template-fields-retry"]').exists()).toBe(true); expect(getPrintingFormSchema).toHaveBeenCalledTimes(1);
});
