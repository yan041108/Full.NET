import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import Delivery from './EnterpriseRequestDelivery.vue';
import { useSessionStore } from '../auth/session';

const api = vi.hoisted(() => ({ deliveryRequest: vi.fn(), deliveryAttachments: vi.fn(), deliveryReports: vi.fn(),
  deliveryPrint: vi.fn(), deliveryDownloadAttachment: vi.fn(), deliveryDownloadReport: vi.fn(),
  deliveryUpload: vi.fn(), deliveryDelete: vi.fn(), deliveryCreateReport: vi.fn(), deliveryCancelReport: vi.fn() }));
vi.mock('../api/enterprise-request-delivery', () => api);
const all = ['enterprise_requests.read', 'enterprise_request_lines.read', 'attachments.read', 'attachments.manage',
  'reports.read', 'reports.create', 'reports.cancel', 'documents.print', 'documents.amounts.read'].map(key => `enterprise_request.${key}`);
const wrappers: VueWrapper[] = [];
function render(permissions = all) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id: 'user', tenantId: 'tenant', sessionId: 'session', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(Delivery, { props: { requestId: 'request', modelValue: false,
    'onUpdate:modelValue': value => { void wrapper.setProps({ modelValue: value }); } }, global: { plugins: [pinia],
    stubs: { ElDialog: { props: ['modelValue'], template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>' } } } });
  wrappers.push(wrapper); return { wrapper, session };
}
function button(wrapper: VueWrapper, label: string) { return wrapper.findAll('button').find(item => item.text() === label)!; }
beforeEach(() => {
  vi.resetAllMocks(); api.deliveryRequest.mockResolvedValue({ version: '1', status: 'Draft' });
  api.deliveryAttachments.mockResolvedValue({ items: [], total: 0 }); api.deliveryReports.mockResolvedValue({ items: [], total: 0 });
});
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.useRealTimers(); });

it('读取单据期间撤权取消请求，迟到响应不得继续请求附件与报表', async () => {
  let resolve!: (value: unknown) => void; api.deliveryRequest.mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session } = render(); await wrapper.setProps({ modelValue: true });
  const signal = api.deliveryRequest.mock.calls[0]![1] as AbortSignal;
  session.currentUser!.permissions = []; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve({ version: '1', status: 'Draft' }); await flushPromises();
  expect(api.deliveryAttachments).not.toHaveBeenCalled(); expect(api.deliveryReports).not.toHaveBeenCalled();
  expect(wrapper.find('section').exists()).toBe(false); expect(button(wrapper, '附件与文档')).toBeUndefined();
});
it('金额权限撤销清除已展示预览，重新打开按当前权限重新加载', async () => {
  api.deliveryPrint.mockResolvedValue({ html: '<h1>金额 12.50</h1>' });
  const { wrapper, session } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  await button(wrapper, '打印预览').trigger('click'); await flushPromises(); expect(wrapper.find('iframe').exists()).toBe(true);
  session.currentUser!.permissions = all.filter(key => key !== 'enterprise_request.documents.amounts.read'); await flushPromises();
  expect(wrapper.find('iframe').exists()).toBe(false);
  await wrapper.setProps({ modelValue: true }); await flushPromises();
  expect(api.deliveryRequest).toHaveBeenCalledTimes(2); expect(wrapper.find('iframe').exists()).toBe(false);
});
it('切换上下文后不生成旧附件下载链接', async () => {
  api.deliveryAttachments.mockResolvedValue({ items: [{ id: 'attachment', phase: 'Ready', fileName: 'evidence.txt' }], total: 1 });
  let resolve!: (value: Blob) => void; api.deliveryDownloadAttachment.mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  const createUrl = vi.spyOn(URL, 'createObjectURL'); await button(wrapper, '下载').trigger('click');
  const signal = api.deliveryDownloadAttachment.mock.calls[0]![2] as AbortSignal;
  session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve(new Blob(['sensitive'])); await flushPromises(); expect(createUrl).not.toHaveBeenCalled();
  expect(wrapper.text()).not.toContain('evidence.txt');
});
it('只有附件读取权限时保留附件入口且不提供报表创建与打印', async () => {
  const { wrapper } = render(['enterprise_request.enterprise_requests.read', 'enterprise_request.attachments.read']);
  await wrapper.setProps({ modelValue: true }); await flushPromises();
  expect(api.deliveryAttachments).toHaveBeenCalledTimes(1); expect(api.deliveryReports).not.toHaveBeenCalled();
  expect(button(wrapper, '生成报表')).toBeUndefined(); expect(button(wrapper, '打印预览')).toBeUndefined();
});

const report = (phase = 'Queued') => ({ id: 'report-1', phase, version: '1', includesAmounts: false, expiresAtUtc: '2026-11-04T00:00:00Z' });
async function openPending() {
  vi.useFakeTimers(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
  const state = render(); await state.wrapper.setProps({ modelValue: true }); await flushPromises(); return state;
}
it('后台报表自动变为可下载，终态停止且不更新主单版本快照', async () => {
  api.deliveryReports.mockResolvedValueOnce({ items: [report()], total: 1 }).mockResolvedValue({ items: [report('Ready')], total: 1 });
  const { wrapper } = await openPending(); expect(wrapper.text()).toContain('状态将自动刷新');
  await vi.advanceTimersByTimeAsync(5000); await flushPromises();
  expect(button(wrapper, '下载')).toBeDefined(); expect(api.deliveryRequest).toHaveBeenCalledTimes(1);
  await vi.advanceTimersByTimeAsync(20000); expect(api.deliveryReports).toHaveBeenCalledTimes(2);
});
it('关闭报表弹窗取消刷新，迟到结果不能重新打开弹窗', async () => {
  let resolve!: (value: unknown) => void;
  api.deliveryReports.mockResolvedValueOnce({ items: [report()], total: 1 })
    .mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  const { wrapper } = await openPending(); await vi.advanceTimersByTimeAsync(5000);
  const signal = api.deliveryReports.mock.calls[1]![2] as AbortSignal;
  await wrapper.setProps({ modelValue: false }); expect(signal.aborted).toBe(true);
  resolve({ items: [report('Ready')], total: 1 }); await flushPromises();
  expect(wrapper.find('section').exists()).toBe(false);
});
it('后台读取失败暂停，手动刷新恢复并清除错误', async () => {
  api.deliveryReports.mockResolvedValueOnce({ items: [report()], total: 1 }).mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValue({ items: [report('Ready')], total: 1 });
  const { wrapper } = await openPending(); await vi.advanceTimersByTimeAsync(5000); await flushPromises();
  expect(wrapper.text()).toContain('自动刷新已暂停'); await vi.advanceTimersByTimeAsync(20000);
  expect(api.deliveryReports).toHaveBeenCalledTimes(2);
  await button(wrapper, '刷新状态').trigger('click'); await flushPromises();
  expect(button(wrapper, '下载')).toBeDefined(); expect(wrapper.text()).not.toContain('操作未完成');
});
it('隐藏页面暂停报表读取，恢复可见后继续', async () => {
  api.deliveryReports.mockResolvedValueOnce({ items: [report()], total: 1 }).mockResolvedValue({ items: [report('Ready')], total: 1 });
  await openPending(); let hidden = true; vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
  document.dispatchEvent(new Event('visibilitychange')); await vi.advanceTimersByTimeAsync(10000);
  expect(api.deliveryReports).toHaveBeenCalledTimes(1);
  hidden = false; document.dispatchEvent(new Event('visibilitychange')); await vi.advanceTimersByTimeAsync(5000);
  expect(api.deliveryReports).toHaveBeenCalledTimes(2);
});
it('报表自动刷新五分钟后停止，不扩大分页和请求预算', async () => {
  api.deliveryReports.mockResolvedValue({ items: [report()], total: 1 }); const { wrapper } = await openPending();
  await vi.advanceTimersByTimeAsync(305000); expect(wrapper.text()).toContain('自动刷新已暂停');
  const count = api.deliveryReports.mock.calls.length; expect(count).toBeLessThanOrEqual(61);
  await vi.advanceTimersByTimeAsync(10000); expect(api.deliveryReports).toHaveBeenCalledTimes(count);
  expect(api.deliveryReports.mock.calls.every(call => call[1] === 1)).toBe(true);
});
it('单次报表读取三十秒无响应时取消并暂停', async () => {
  api.deliveryReports.mockResolvedValueOnce({ items: [report()], total: 1 }).mockImplementationOnce(() => new Promise(() => {}));
  const { wrapper } = await openPending(); await vi.advanceTimersByTimeAsync(35000);
  expect((api.deliveryReports.mock.calls[1]![2] as AbortSignal).aborted).toBe(true);
  expect(wrapper.text()).toContain('自动刷新已暂停'); await vi.advanceTimersByTimeAsync(10000);
  expect(api.deliveryReports).toHaveBeenCalledTimes(2);
});
it('展示服务端有效期，浏览器时钟不擅自将就绪结果改为过期', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2099-01-01T00:00:00Z'));
  api.deliveryReports.mockResolvedValue({ items: [report('Ready')], total: 1 }); const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  expect(wrapper.get('[data-testid="enterprise-report-expiry"]').attributes('datetime')).toBe('2026-11-04T00:00:00Z');
  expect(wrapper.text()).toContain('到期后请重新生成'); expect(wrapper.text()).toContain('就绪'); expect(wrapper.text()).not.toContain('此结果已过期');
  expect(button(wrapper, '下载')).toBeDefined();
});
it.each([['FailedCleanup', '失败'], ['CancelledCleanup', '已取消'], ['ExpiredCleanup', '已过期'], ['DeletedCleanup', '已删除']])('清理状态 %s 保留实际终态且不提供下载', async (state, label) => {
  api.deliveryReports.mockResolvedValue({ items: [report(state)], total: 1 }); const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  expect(wrapper.text()).toContain(label); expect(wrapper.get('[data-testid="enterprise-report-cleanup"]').text()).toContain('文件正在清理');
  expect(button(wrapper, '下载')).toBeUndefined(); if (state === 'ExpiredCleanup') expect(wrapper.text()).toContain('此结果已过期，不能下载');
});
it('清理完成后停止刷新，既有主单版本不因状态读取而变化', async () => {
  api.deliveryReports.mockResolvedValueOnce({ items: [report('ExpiredCleanup')], total: 1 }).mockResolvedValue({ items: [report('Expired')], total: 1 });
  const { wrapper } = await openPending(); await vi.advanceTimersByTimeAsync(5000); await flushPromises();
  expect(wrapper.find('[data-testid="enterprise-report-cleanup"]').exists()).toBe(false); expect(wrapper.text()).toContain('此结果已过期');
  const count = api.deliveryReports.mock.calls.length; await vi.advanceTimersByTimeAsync(20_000); expect(api.deliveryReports).toHaveBeenCalledTimes(count);
  expect(api.deliveryRequest).toHaveBeenCalledTimes(1);
});
it('异常时间及未知状态保持待确认，不伪造正在处理或已过期', async () => {
  api.deliveryReports.mockResolvedValue({ items: [{ ...report('Unknown'), expiresAtUtc: 'invalid' }], total: 1 });
  const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  expect(wrapper.text()).toContain('有效期待确认'); expect(wrapper.text()).toContain('状态待确认 (Unknown)'); expect(wrapper.text()).not.toContain('Invalid Date');
  expect(button(wrapper, '下载')).toBeUndefined();
});
it('确认取消固定点击时版本，清理中的结果立即不可下载', async () => {
  const ready = report('Ready'); api.deliveryReports.mockResolvedValueOnce({ items: [ready], total: 1 }).mockResolvedValue({ items: [report('CancelledCleanup')], total: 1 });
  const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  await wrapper.get('[data-testid="enterprise-report-cancel"]').trigger('click'); expect(api.deliveryCancelReport).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('结果立即不可下载');
  ready.version = '2'; await wrapper.get('[data-testid="enterprise-report-confirm-cancel"]').trigger('click'); await flushPromises();
  expect(api.deliveryCancelReport).toHaveBeenCalledWith('request', 'report-1', '1', expect.any(AbortSignal)); expect(wrapper.text()).toContain('已取消');
  expect(button(wrapper, '下载')).toBeUndefined();
});
it('取消结果不明时锁定写操作且不轮询，手动刷新后恢复', async () => {
  vi.useFakeTimers(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
  api.deliveryReports.mockResolvedValue({ items: [report()], total: 1 }); api.deliveryCancelReport.mockRejectedValue(new Error('Response lost'));
  const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  await wrapper.get('[data-testid="enterprise-report-cancel"]').trigger('click'); await wrapper.get('[data-testid="enterprise-report-confirm-cancel"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="enterprise-delivery-unconfirmed"]').exists()).toBe(true); expect(button(wrapper, '生成报表').attributes('disabled')).toBeDefined();
  await vi.advanceTimersByTimeAsync(20_000); expect(api.deliveryCancelReport).toHaveBeenCalledTimes(1); expect(api.deliveryReports).toHaveBeenCalledTimes(1);
  await button(wrapper, '刷新状态').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="enterprise-delivery-unconfirmed"]').exists()).toBe(false);
  expect(button(wrapper, '生成报表').attributes('disabled')).toBeUndefined();
});
it('切换会话关闭取消确认，不会使用旧版本发出取消', async () => {
  api.deliveryReports.mockResolvedValue({ items: [report('Ready')], total: 1 }); const { wrapper, session } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  await wrapper.get('[data-testid="enterprise-report-cancel"]').trigger('click'); session.switching = true; await flushPromises();
  expect(wrapper.find('[data-testid="enterprise-report-confirm-cancel"]').exists()).toBe(false); expect(api.deliveryCancelReport).not.toHaveBeenCalled();
});
