import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import View from './WebhookSubscriptionsView.vue';
import * as api from '../api/webhook-subscriptions';
import { createOutputSession, outputId } from '../test/data-output-fixtures';
vi.mock('../api/webhook-subscriptions', () => ({ listWebhookSubscriptions: vi.fn(), createWebhookSubscription: vi.fn(), setWebhookSubscriptionStatus: vi.fn(), listWebhookDeliveryHistory: vi.fn(), getWebhookDeliveryHistory: vi.fn(), listWebhookDeliveryAttempts: vi.fn(), replayWebhookDelivery: vi.fn(), getWebhookDeliveryReplayReceipt: vi.fn() }));
const read = 'webhooks.subscriptions.read', create = 'webhooks.subscriptions.manage', status = 'webhooks.subscriptions.set_status';
const task = { id: outputId, tenantId: outputId, eventType: 'fullnet.workflow.instance.completed', targetUrl: 'https://example.com/hooks', isActive: true, version: 1 };
const secondId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
let wrapper: VueWrapper;
type State = { items: typeof task[]; editorOpen: boolean; form: { eventType: string; targetUrl: string; signingSecret: string }; createBlocked: boolean;
  receipt?: { id?: string; unknown: boolean }; confirmation?: { token: number }; reviewOpen: boolean;
  historyTarget?: typeof task; openHistory: (row: typeof task) => void;
  editorControls: { event: (value: string) => void; update: (value: boolean) => void }; confirmationControls: { confirm: () => void; update: (value: boolean) => void }; reviewControls: { confirm: () => void };
  openCreate: () => void; submitCreate: () => Promise<void>; load: (manual?: boolean) => Promise<void>; openStatus: (row: typeof task) => void; submitStatus: (token: number) => Promise<void> };
const state = () => wrapper.vm as unknown as State;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.listWebhookSubscriptions).mockResolvedValue([{ ...task }]);
  vi.mocked(api.createWebhookSubscription).mockResolvedValue({ ...task, id: secondId });
  vi.mocked(api.setWebhookSubscriptionStatus).mockResolvedValue({ ...task, isActive: false, version: 2 }); });
afterEach(() => wrapper?.unmount());
async function setup(permissions = [read, create, status], host = false) {
  const context = createOutputSession(permissions); if (host) Object.assign(context.session.currentUser!, { tenantId: null, scope: 'host', actorScope: 'host' });
  wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: {} }, stubs: { WebhookDeliveryHistoryDrawer: { template: '<aside data-testid="history-stub"/>' }, ElDialog: { props: ['modelValue'], template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>' } } } }); await flushPromises(); return context;
}
function fillCreate() { state().openCreate(); Object.assign(state().form, { targetUrl: task.targetUrl, signingSecret: 'independent-test-secret' }); }
it('当前企业可读取，创建与状态权限独立，Host 不读取租户订阅', async () => {
  await setup([read]); expect(wrapper.find('[data-testid="webhook-create"]').exists()).toBe(false); expect(wrapper.find('[data-testid="webhook-status"]').exists()).toBe(false); expect(api.listWebhookSubscriptions).toHaveBeenCalledOnce();
  wrapper.unmount(); await setup([read, create, status], true); expect(api.listWebhookSubscriptions).toHaveBeenCalledOnce(); expect(wrapper.text()).toContain('请先进入企业');
});
it('只给动作权限不能发出读取或直接写入', async () => {
  await setup([create, status]); state().openCreate(); state().openStatus(task); await state().submitCreate(); await state().submitStatus(1);
  expect(api.listWebhookSubscriptions).not.toHaveBeenCalled(); expect(api.createWebhookSubscription).not.toHaveBeenCalled(); expect(api.setWebhookSubscriptionStatus).not.toHaveBeenCalled();
});
it('状态确认展示原来源，放弃不写入，明确确认后更新版本', async () => {
  await setup(); state().openStatus(task); const controls = state().confirmationControls; controls.update(false); expect(api.setWebhookSubscriptionStatus).not.toHaveBeenCalled();
  state().openStatus(task); await state().submitStatus(state().confirmation!.token);
  expect(api.setWebhookSubscriptionStatus).toHaveBeenCalledExactlyOnceWith(task.id, { isActive: false, version: 1 }, expect.any(AbortSignal)); expect(state().receipt).toMatchObject({ id: task.id, unknown: false });
  expect(state().items[0]).toMatchObject({ isActive: false, version: 2 });
});
it('原行版本和旧弹窗回调不可操作新确认', async () => {
  await setup(); state().openStatus(task); const old = state().confirmationControls; old.update(false); state().openStatus(task); old.confirm(); await flushPromises(); expect(api.setWebhookSubscriptionStatus).not.toHaveBeenCalled();
  state().items = [{ ...task, version: 2 }]; await state().submitStatus(state().confirmation!.token); expect(api.setWebhookSubscriptionStatus).not.toHaveBeenCalled();
});
it('状态未知不自动重发，普通补读不解锁，人工读取后才可再次确认', async () => {
  vi.mocked(api.setWebhookSubscriptionStatus).mockRejectedValue(new Error('lost')); await setup(); state().openStatus(task); await state().submitStatus(state().confirmation!.token);
  state().confirmationControls.update(false); state().openStatus(task); expect(state().confirmation).toBeUndefined(); await state().load(); state().openStatus(task); expect(state().confirmation).toBeUndefined();
  await state().load(true); state().openStatus(task); expect(state().confirmation).toBeDefined(); expect(api.setWebhookSubscriptionStatus).toHaveBeenCalledOnce(); expect(state().receipt?.unknown).toBe(true);
});
it('错配状态来源保留未知事实，不替换原订阅或误报成功', async () => {
  vi.mocked(api.setWebhookSubscriptionStatus).mockResolvedValue({ ...task, targetUrl: 'https://other.example.com/hooks', isActive: false, version: 2 });
  await setup(); state().openStatus(task); await state().submitStatus(state().confirmation!.token); expect(state().items[0]).toEqual(task); expect(state().receipt?.unknown).toBe(true);
});
it('创建成功保留新回执并清空密钥，补读失败不覆盖成功', async () => {
  await setup(); fillCreate(); vi.mocked(api.listWebhookSubscriptions).mockRejectedValueOnce(new Error('read failed')); await state().submitCreate();
  expect(state().receipt).toMatchObject({ id: secondId, unknown: false }); expect(state().form.signingSecret).toBe(''); expect(state().items.some(row => row.id === secondId)).toBe(true);
});
it('关闭在途创建保留未知且清空密钥，迟到成功不能接入', async () => {
  let resolve!: (row: typeof task) => void; vi.mocked(api.createWebhookSubscription).mockImplementationOnce(() => new Promise(done => { resolve = done; })); await setup(); fillCreate(); const pending = state().submitCreate();
  const signal = vi.mocked(api.createWebhookSubscription).mock.calls[0]![1]!; state().editorControls.update(false); expect(signal.aborted).toBe(true); expect(state().form.signingSecret).toBe('');
  resolve({ ...task, id: secondId }); await pending; expect(state().items).toEqual([task]); expect(state().receipt?.unknown).toBe(true); expect(api.listWebhookSubscriptions).toHaveBeenCalledOnce();
});
it('未知创建必须人工读取和显式核对，核对本身不自动创建', async () => {
  vi.mocked(api.createWebhookSubscription).mockRejectedValueOnce(new Error('lost')); await setup(); fillCreate(); await state().submitCreate(); await state().submitCreate(); expect(api.createWebhookSubscription).toHaveBeenCalledOnce();
  state().reviewOpen = true; state().reviewControls.confirm(); expect(state().createBlocked).toBe(true); state().reviewOpen = false;
  await state().load(true); state().reviewOpen = true; state().reviewControls.confirm(); expect(state().createBlocked).toBe(false); expect(api.createWebhookSubscription).toHaveBeenCalledOnce();
});
it('旧编辑输入不能改新窗口，短密钥不提交', async () => {
  await setup(); fillCreate(); const old = state().editorControls; old.update(false); state().openCreate(); old.event('other'); expect(state().form.eventType).toBe(task.eventType);
  state().form.targetUrl = task.targetUrl; state().form.signingSecret = 'short'; await state().submitCreate(); expect(api.createWebhookSubscription).not.toHaveBeenCalled();
});
it('切换开始即取消在途写入，迟到结果不能污染另一企业', async () => {
  let resolve!: (row: typeof task) => void; vi.mocked(api.setWebhookSubscriptionStatus).mockImplementationOnce(() => new Promise(done => { resolve = done; })); const { session } = await setup();
  state().openStatus(task); const pending = state().submitStatus(state().confirmation!.token); const signal = vi.mocked(api.setWebhookSubscriptionStatus).mock.calls[0]![2]!;
  session.switching = true; expect(signal.aborted).toBe(true); resolve({ ...task, isActive: false, version: 2 }); await pending; expect(state().receipt).toBeUndefined(); expect(state().items).toEqual([]);
});
it('跨企业列表响应失败关闭且不展示敏感来源', async () => {
  vi.mocked(api.listWebhookSubscriptions).mockResolvedValue([{ ...task, tenantId: secondId }]); await setup(); expect(state().items).toEqual([]); expect(wrapper.text()).not.toContain(task.targetUrl);
});
it('历史读取独立于状态与创建，旧行和撤权不能打开历史', async () => {
  const { session } = await setup([read, 'webhooks.deliveries.read']); state().openHistory({ ...task, version: 2 }); expect(state().historyTarget).toBeUndefined();
  state().openHistory(task); expect(state().historyTarget?.id).toBe(task.id); expect(wrapper.find('[data-testid="webhook-status"]').exists()).toBe(false);
  session.currentUser!.permissions = [read]; await flushPromises(); expect(state().historyTarget).toBeUndefined(); state().openHistory(task); expect(state().historyTarget).toBeUndefined();
});
