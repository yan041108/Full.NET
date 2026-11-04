import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import View from './WebhookDeliveryHistoryDrawer.vue';
import * as api from '../../api/webhook-subscriptions';
import { createOutputSession, outputId } from '../../test/data-output-fixtures';
import type { WebhookDeliveryHistory, WebhookDeliveryReplayReceipt } from '@fullnet/client-contracts';
vi.mock('../../api/webhook-subscriptions', () => ({ listWebhookDeliveryHistory: vi.fn(), getWebhookDeliveryHistory: vi.fn(), listWebhookDeliveryAttempts: vi.fn(), replayWebhookDelivery: vi.fn(), getWebhookDeliveryReplayReceipt: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const subscription = { id: outputId, tenantId: outputId, eventType: 'fullnet.workflow.instance.completed', targetUrl: 'https://example.com/hooks', isActive: true, version: 1 };
const row = { id: outputId, tenantId: outputId, subscriptionId: outputId, eventId: other, statusKey: 'pending' as const, attemptCount: 1,
  nextAttemptAtUtc: '2026-11-04T00:01:00Z', lastErrorCode: 'webhooks.delivery.http_error', lastHttpStatus: 503, createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:30Z', version: 2 };
const read = 'webhooks.subscriptions.read', history = 'webhooks.deliveries.read';
let wrapper: VueWrapper;
type State = { items: WebhookDeliveryHistory[]; page: number; pageSize: number; total: number; filter: string; detail?: WebhookDeliveryHistory; detailTarget: string; problem: string; detailProblem: string;
  load: (recover?: boolean) => Promise<void>; openDetail: (value: WebhookDeliveryHistory) => Promise<void>;
  replayControls: { queued: (receipt: WebhookDeliveryReplayReceipt) => void };
  controls: { change: (page: number, size: number) => void; resize: (value: number) => void; filter: (value: string) => void; update: (value: boolean) => void; refresh: () => void };
  rows: { show: () => void }[] };
const state = () => wrapper.vm as unknown as State;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.listWebhookDeliveryHistory).mockImplementation(async (_id, page = 1, pageSize = 20) => ({ items: [row], page, pageSize, total: 21 })); vi.mocked(api.getWebhookDeliveryHistory).mockResolvedValue(row); });
afterEach(() => wrapper?.unmount());
async function setup(permissions = [read, history]) { const context = createOutputSession(permissions); wrapper = mount(View, { props: { subscription }, global: { plugins: [context.pinia], directives: { loading: {} }, stubs: { ElDrawer: { props: ['modelValue'], template: '<aside v-if="modelValue"><slot/></aside>' } } } }); await flushPromises(); return context; }
it('读取需要订阅及历史双重权限，没有创建或状态权限仍可查看', async () => {
  await setup([read]); expect(api.listWebhookDeliveryHistory).not.toHaveBeenCalled(); wrapper.unmount(); await setup([read, history]); expect(api.listWebhookDeliveryHistory).toHaveBeenCalledExactlyOnceWith(outputId, 1, 20, undefined, expect.any(AbortSignal));
});
it('分页及大小由 change 合并接入一次，旧页回调不能重复读', async () => {
  await setup(); const old = state().controls, paging = wrapper.getComponent(ElPagination);
  paging.vm.$emit('update:currentPage', 2); paging.vm.$emit('change', 2, 20); await flushPromises();
  expect(api.listWebhookDeliveryHistory).toHaveBeenLastCalledWith(outputId, 2, 20, undefined, expect.any(AbortSignal)); expect(api.listWebhookDeliveryHistory).toHaveBeenCalledTimes(2);
  old.change(3, 20); expect(api.listWebhookDeliveryHistory).toHaveBeenCalledTimes(2);
  state().controls.resize(50); await flushPromises();
  expect(api.listWebhookDeliveryHistory).toHaveBeenLastCalledWith(outputId, 1, 50, undefined, expect.any(AbortSignal));
});
it('筛选从第一页读取且传递明确状态，未知值不发请求', async () => {
  await setup(); state().page = 2; state().controls.filter('processing'); await flushPromises(); expect(api.listWebhookDeliveryHistory).toHaveBeenLastCalledWith(outputId, 1, 20, 'processing', expect.any(AbortSignal));
  const count = vi.mocked(api.listWebhookDeliveryHistory).mock.calls.length; state().controls.filter('unknown'); expect(api.listWebhookDeliveryHistory).toHaveBeenCalledTimes(count);
});
it('空尾页只根据权威总数回退一次', async () => {
  await setup(); state().page = 3; vi.mocked(api.listWebhookDeliveryHistory).mockResolvedValueOnce({ items: [], page: 3, pageSize: 20, total: 1 });
  await state().load(); expect(state().page).toBe(1); expect(api.listWebhookDeliveryHistory).toHaveBeenCalledTimes(3);
});
it('其他企业、订阅或错误页响应不进入当前表格', async () => {
  await setup(); for (const invalid of [{ ...row, tenantId: other }, { ...row, subscriptionId: other }]) {
    vi.mocked(api.listWebhookDeliveryHistory).mockResolvedValueOnce({ items: [invalid], page: 1, pageSize: 20, total: 1 }); await state().load(); expect(state().problem).not.toBe(''); expect(state().items[0]?.tenantId).toBe(outputId);
  }
});
it('当前详情显示安全失败类别，错误资源不能显示', async () => {
  await setup(); await state().openDetail(row); expect(wrapper.text()).toContain('接收端返回 HTTP 503');
  vi.mocked(api.getWebhookDeliveryHistory).mockResolvedValueOnce({ ...row, eventId: outputId }); await state().openDetail(row); expect(state().detail).toBeUndefined(); expect(state().detailProblem).not.toBe('');
});
it('更低修订详情和旧列表行不能接入新页', async () => {
  await setup(); vi.mocked(api.getWebhookDeliveryHistory).mockResolvedValueOnce({ ...row, version: 1 }); await state().openDetail(row); expect(state().detail).toBeUndefined();
  const old = state().rows[0]!.show; await state().load(); old(); expect(api.getWebhookDeliveryHistory).toHaveBeenCalledOnce();
});
it('关闭在途详情同步中止，迟到结果不可接入', async () => {
  let resolve!: (value: typeof row) => void; vi.mocked(api.getWebhookDeliveryHistory).mockImplementationOnce(() => new Promise(done => { resolve = done; })); await setup(); const pending = state().openDetail(row);
  const signal = vi.mocked(api.getWebhookDeliveryHistory).mock.calls[0]![2]!; state().controls.update(false); expect(signal.aborted).toBe(true); resolve(row); await pending; expect(state().detail).toBeUndefined(); expect(wrapper.emitted('close')).toHaveLength(1);
});
it('切换开始取消列表，迟到错误或数据不能恢复', async () => {
  let resolve!: (value: { items: typeof row[]; page: number; pageSize: number; total: number }) => void;
  vi.mocked(api.listWebhookDeliveryHistory).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  const { session } = await setup(); const signal = vi.mocked(api.listWebhookDeliveryHistory).mock.calls[0]![4]!; session.switching = true; expect(signal.aborted).toBe(true);
  resolve({ items: [row], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(state().items).toEqual([]); expect(state().problem).toBe('');
});
it('读取失败可人工刷新，不自动重试或触发投递', async () => {
  vi.mocked(api.listWebhookDeliveryHistory).mockRejectedValueOnce(new Error('offline')); await setup(); expect(state().problem).not.toBe(''); expect(api.listWebhookDeliveryHistory).toHaveBeenCalledOnce();
  state().controls.refresh(); await flushPromises(); expect(state().problem).toBe(''); expect(state().items).toHaveLength(1);
});
it('重放排队事实移除失败筛选行，重复回执不重复扣减总数', async () => {
 await setup(); const failed={...row,statusKey:'failed' as const,version:3}; state().items=[failed]; vi.mocked(api.getWebhookDeliveryHistory).mockResolvedValueOnce(failed); await state().openDetail(failed); state().items=[failed]; state().filter='failed'; state().total=1;
 const receipt={operationId:outputId,tenantId:outputId,subscriptionId:outputId,deliveryId:outputId,eventId:other,previousVersion:3,version:4,attemptCount:1,queuedAtUtc:'2026-11-05T00:00:00Z'};
 state().replayControls.queued(receipt); expect(state().detail?.statusKey).toBe('pending'); expect(state().detail?.version).toBe(4); expect(state().items).toEqual([]); expect(state().total).toBe(0); state().replayControls.queued(receipt); expect(state().total).toBe(0);
});
it('陈旧重放回执与旧详情回调不能回退新读取的修订', async () => {
 await setup(); const current={...row,statusKey:'failed' as const,version:7}; state().items=[current]; vi.mocked(api.getWebhookDeliveryHistory).mockResolvedValue(current); await state().openDetail(current); const old=state().replayControls.queued;
 const receipt={operationId:outputId,tenantId:outputId,subscriptionId:outputId,deliveryId:outputId,eventId:other,previousVersion:3,version:4,attemptCount:1,queuedAtUtc:'2026-11-05T00:00:00Z'};
 old(receipt); expect(state().detail?.version).toBe(7); await state().openDetail(current); old({...receipt,previousVersion:7,version:8}); expect(state().detail?.version).toBe(7);
});
