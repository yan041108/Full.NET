import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElPagination, ElRadioGroup, ElSelect } from 'element-plus';
import type { TenantSubscription } from '@fullnet/client-contracts';
import TenantSubscriptionsDialog from './TenantSubscriptionsDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { cancelTenantSubscription, createTenantSubscription, getTenantSubscription, listTenantSubscriptions } from '../api/tenant-subscriptions';

vi.mock('../api/tenant-subscriptions', () => ({ getTenantSubscription: vi.fn(), listTenantSubscriptions: vi.fn(), createTenantSubscription: vi.fn(), cancelTenantSubscription: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const tenant = { id: id(1), identifier: 'acme', name: 'Acme', domain: 'acme.localhost', isActive: true, version: 2, defaultLocale: 'zh-CN' };
const pkg = { id: id(2), code: 'one', name: 'First plan', description: null, isActive: true, version: 1, assignedTenantCount: 0 };
const second = { ...pkg, id: id(3), name: 'Chosen plan' };
const row = { id: id(5), tenantId: tenant.id, packageId: pkg.id, status: 'Trial', currentPeriodStartUtc: '2026-11-01T00:00:00Z', currentPeriodEndUtc: '2026-12-01T00:00:00Z', version: 4 };
const page = (items: TenantSubscription[] = [row]) => ({ items, page: 1, pageSize: 20, total: items.length });
const dialog = { props: ['open', 'showConfirm', 'confirmDisabled'], emits: ['confirm', 'update:open'], template: '<section v-if="open"><slot/><button v-if="showConfirm !== false" data-testid="confirm" :disabled="confirmDisabled" @click="$emit(\'confirm\')">Confirm</button><button data-testid="close" @click="$emit(\'update:open\', false)">Close</button></section>' };
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; }
async function panel(options: { readOnly?: boolean; active?: boolean } = {}) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'operator', displayName: 'Operator', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, sessionId: id(10), permissions: ['tenancy.tenant_subscriptions.read', ...(options.readOnly ? [] : ['tenancy.tenant_subscriptions.manage'])], preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(TenantSubscriptionsDialog, { props: { open: true, tenant: { ...tenant, isActive: options.active ?? true }, packages: [pkg, second] },
    global: { plugins: [pinia], stubs: { ArtFormDialog: dialog, PaidSubscriptionPurchase: true, SubscriptionCheckoutAction: true } } });
  await flushPromises(); return { wrapper, session };
}
describe('租户订阅操作台', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(listTenantSubscriptions).mockResolvedValue(page()); vi.mocked(getTenantSubscription).mockResolvedValue(row); });
  it('只读来源在当前企业内独立定位，不改变当前筛选或第三页', async () => {
    vi.mocked(listTenantSubscriptions).mockImplementation(async (_tenant, selectedPage = 1, pageSize = 20) => ({ items: [row], total: 100, page: selectedPage, pageSize }));
    const { wrapper } = await panel({ readOnly: true });
    wrapper.getComponent({ name: 'ElRadioGroup' }).vm.$emit('update:modelValue', 'Trial'); await flushPromises();
    wrapper.getComponent({ name: 'ElPagination' }).vm.$emit('current-change', 3); await flushPromises();
    await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    const calls = vi.mocked(listTenantSubscriptions).mock.calls.length;
    vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, id: id(8), status: 'Expired' });
    wrapper.getComponent({ name: 'SubscriptionInvoiceDetails' }).vm.$emit('readSource', id(8)); await flushPromises();
    expect(getTenantSubscription).toHaveBeenLastCalledWith(tenant.id, id(8), expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(id(8));
    expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('已到期');
    expect(wrapper.getComponent({ name: 'ElRadioGroup' }).props('modelValue')).toBe('Trial');
    expect(wrapper.getComponent({ name: 'ElPagination' }).props('currentPage')).toBe(3);
    expect(listTenantSubscriptions).toHaveBeenCalledTimes(calls); expect(createTenantSubscription).not.toHaveBeenCalled(); expect(cancelTenantSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('来源 404 保留请求的原来源 ID，不显示上一张账单或破坏历史列表', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    vi.mocked(getTenantSubscription).mockRejectedValue({ status: 404, code: 'test.missing', title: 'missing' });
    wrapper.getComponent({ name: 'SubscriptionInvoiceDetails' }).vm.$emit('readSource', id(8)); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(id(8)); expect(wrapper.find('[data-testid="subscription-detail-status"]').exists()).toBe(false);
    expect(wrapper.findComponent({ name: 'SubscriptionInvoiceDetails' }).exists()).toBe(false);
    expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([row]); expect(listTenantSubscriptions).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('查看来源取消原详情刷新并拒绝其晚到结果', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    const old = deferred<TenantSubscription>(); vi.mocked(getTenantSubscription).mockReturnValueOnce(old.promise);
    await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises(); const oldSignal = vi.mocked(getTenantSubscription).mock.calls[1]![2];
    vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, id: id(8), status: 'Expired' });
    wrapper.getComponent({ name: 'SubscriptionInvoiceDetails' }).vm.$emit('readSource', id(8)); await flushPromises(); expect(oldSignal?.aborted).toBe(true);
    old.resolve(row); await flushPromises(); expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(id(8));
    expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('已到期'); wrapper.unmount();
  });
  it.each(['bad-id', '00000000-0000-0000-0000-000000000000', row.id])('来源事件 %s 不能触发无效或自指读取', async source => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    wrapper.getComponent({ name: 'SubscriptionInvoiceDetails' }).vm.$emit('readSource', source); await flushPromises();
    expect(getTenantSubscription).toHaveBeenCalledTimes(1); expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); wrapper.unmount();
  });
  it('空列表可按规范化 ID 独立定位，非法输入不发请求', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([])); vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, id: id(8), status: 'Expired' });
    const { wrapper } = await panel({ readOnly: true }); const input = wrapper.get('[data-testid="subscription-detail-lookup-input"]');
    await input.setValue('bad-id'); await input.trigger('keyup.enter'); expect(getTenantSubscription).not.toHaveBeenCalled();
    expect(wrapper.get('[data-testid="subscription-detail-lookup"]').attributes('disabled')).toBeDefined();
    await input.setValue('  ' + id(8).toUpperCase() + '  '); await input.trigger('keyup.enter'); await flushPromises();
    expect(getTenantSubscription).toHaveBeenCalledWith(tenant.id, id(8), expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(id(8)); expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('已到期');
    expect(listTenantSubscriptions).toHaveBeenCalledTimes(1); expect(createTenantSubscription).not.toHaveBeenCalled(); expect(cancelTenantSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('查不到列表之外的 ID 不撤销原列表读取资格', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([])); vi.mocked(getTenantSubscription).mockRejectedValue({ status: 404, code: 'tenancy.tenant_subscription.not_found' });
    const { wrapper } = await panel(); wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', pkg.id); await flushPromises();
    expect(wrapper.get('[data-testid="tenant-subscription-trial"]').attributes('disabled')).toBeUndefined();
    const input = wrapper.get('[data-testid="subscription-detail-lookup-input"]'); await input.setValue(id(8)); await input.trigger('keyup.enter'); await flushPromises();
    await wrapper.findAll('[data-testid="close"]').at(-1)!.trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="tenant-subscription-trial"]').attributes('disabled')).toBeUndefined();
    expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([]); expect(createTenantSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('目标企业变化清空查找草稿，不向新企业发送旧 ID', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="subscription-detail-lookup-input"]').setValue(id(5));
    await wrapper.setProps({ tenant: { ...tenant, id: id(8), name: 'Other tenant' } }); await flushPromises();
    expect((wrapper.get('[data-testid="subscription-detail-lookup-input"]').element as HTMLInputElement).value).toBe('');
    expect(getTenantSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('只读筛选切回第一页并保持筛选后的分页参数', async () => {
    const { wrapper } = await panel({ readOnly: true });
    vi.mocked(listTenantSubscriptions).mockResolvedValue({ ...page([{ ...row, status: 'Expired' }]), total: 41 });
    wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', 'Expired'); await flushPromises();
    expect(listTenantSubscriptions).toHaveBeenLastCalledWith(tenant.id, 1, 20, expect.any(AbortSignal), 'Expired');
    vi.mocked(listTenantSubscriptions).mockResolvedValue({ ...page([{ ...row, status: 'Expired' }]), page: 2, total: 41 });
    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(listTenantSubscriptions).toHaveBeenLastCalledWith(tenant.id, 2, 20, expect.any(AbortSignal), 'Expired');
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status: 'Cancelled' }]));
    wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', 'Cancelled'); await flushPromises();
    expect(listTenantSubscriptions).toHaveBeenLastCalledWith(tenant.id, 1, 20, expect.any(AbortSignal), 'Cancelled');
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page()); wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', ''); await flushPromises();
    expect(listTenantSubscriptions).toHaveBeenLastCalledWith(tenant.id, 1, 20, expect.any(AbortSignal));
    expect(createTenantSubscription).not.toHaveBeenCalled(); expect(cancelTenantSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('连续切换状态取消旧读取，不接入晚到的前一筛选结果', async () => {
    const { wrapper } = await panel(); const pending = deferred<ReturnType<typeof page>>();
    vi.mocked(listTenantSubscriptions).mockReturnValueOnce(pending.promise);
    wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', 'Expired'); await flushPromises();
    const signal = vi.mocked(listTenantSubscriptions).mock.calls.at(-1)![3]!;
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status: 'Cancelled' }]));
    wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', 'Cancelled'); await flushPromises(); expect(signal.aborted).toBe(true);
    pending.resolve(page([{ ...row, status: 'Expired' }])); await flushPromises();
    expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([{ ...row, status: 'Cancelled' }]); wrapper.unmount();
  });
  it('过滤响应混入其他状态时拒绝整页', async () => {
    const { wrapper } = await panel(); wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', 'Expired'); await flushPromises();
    expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([]); expect(wrapper.text()).toContain('未接入结果'); wrapper.unmount();
  });
  it('明确选择套餐并预览三十天试用，确认前不写入', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([]));
    vi.mocked(createTenantSubscription).mockImplementation(async (tenantId, body) => ({ ...row, ...body, id: id(6), tenantId, version: 1 }));
    const { wrapper } = await panel(); expect(wrapper.get('[data-testid="tenant-subscription-trial"]').attributes('disabled')).toBeDefined();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', second.id); await flushPromises();
    await wrapper.get('[data-testid="tenant-subscription-trial"]').trigger('click');
    expect(wrapper.text()).toContain('Chosen plan'); expect(createTenantSubscription).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const [target, body, signal] = vi.mocked(createTenantSubscription).mock.calls[0]!;
    expect(target).toBe(tenant.id); expect(body.packageId).toBe(second.id); expect(body.status).toBe('Trial'); expect(signal).toBeInstanceOf(AbortSignal);
    expect(Date.parse(body.currentPeriodEndUtc) - Date.parse(body.currentPeriodStartUtc)).toBe(30 * 86400000);
    expect(wrapper.emitted('changed')).toHaveLength(1); wrapper.unmount();
  });
  it('取消先确认原订阅和版本，再提交一次', async () => {
    vi.mocked(cancelTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', version: 5 });
    const { wrapper } = await panel(); await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click');
    expect(cancelTenantSubscription).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('不会自动发起渠道退款');
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(cancelTenantSubscription).toHaveBeenCalledWith(tenant.id, row.id, 4, expect.any(AbortSignal)); expect(cancelTenantSubscription).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('只读用户能读取历史但没有试用和取消入口', async () => {
    const { wrapper } = await panel({ readOnly: true }); expect(listTenantSubscriptions).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain('试用中'); expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="tenant-subscription-trial"]').exists()).toBe(false); wrapper.unmount();
  });
  it('只读详情展示原周期、试用和取消信息，套餐名称明确取当前目录', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status: 'Cancelled', trialEndsAtUtc: '2026-11-30T00:00:00Z', cancelledAtUtc: '2026-11-04T00:00:00Z' }]));
    vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', trialEndsAtUtc: '2026-11-30T00:00:00Z', cancelledAtUtc: '2026-11-04T00:00:00Z' });
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click');
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('已取消');
    expect(wrapper.findAll('time').some(time => time.attributes('datetime') === row.currentPeriodStartUtc)).toBe(true);
    expect(wrapper.text()).toContain('试用截止'); expect(wrapper.text()).toContain('取消时间'); expect(wrapper.text()).toContain('当前套餐目录名称'); expect(wrapper.text()).toContain('First plan');
    expect(createTenantSubscription).not.toHaveBeenCalled(); expect(cancelTenantSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('详情刷新只更新原订阅', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click');
    vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', version: 5 });
    await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('已取消'); wrapper.unmount();
  });
  it('原订阅独立于列表页读取，不把同位置其他订阅接入详情', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click');
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, id: id(8), status: 'Active', version: 9 }]));
    vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', version: 5 });
    await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('已取消');
    expect(listTenantSubscriptions).toHaveBeenCalledTimes(1); expect(getTenantSubscription).toHaveBeenLastCalledWith(tenant.id, row.id, expect.any(AbortSignal)); wrapper.unmount();
  });
  it('详情刷新失败保留上次读取且明确提示', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click');
    vi.mocked(getTenantSubscription).mockRejectedValue(new Error('offline')); await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('试用中'); expect(wrapper.text()).toContain('详情刷新失败，保留上次成功读取'); wrapper.unmount();
  });
  it.each([401, 403, 404])('原订阅明确拒绝 %s 后清除内容并保留目标 ID', async status => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    vi.mocked(getTenantSubscription).mockRejectedValue({ status, code: status === 404 ? 'tenancy.tenant_subscription.not_found' : 'authorization.permission_denied' });
    await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); expect(wrapper.find('[data-testid="subscription-detail-status"]').exists()).toBe(false);
    if (status !== 404) expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([]);
    expect(wrapper.text()).toContain(status === 404 ? '原订阅不存在' : '无权读取原订阅'); wrapper.unmount();
  });
  it.each(['subscription', 'tenant'])('不接入错配 %s 的独立读取', async target => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    vi.mocked(getTenantSubscription).mockResolvedValue({ ...row, ...(target === 'subscription' ? { id: id(8) } : { tenantId: id(8) }), status: 'Cancelled' });
    await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); expect(wrapper.get('[data-testid="subscription-detail-status"]').text()).toBe('试用中');
    expect(wrapper.text()).toContain('响应与原目标不一致'); wrapper.unmount();
  });
  it('关闭详情取消独立读取，不接入迟到内容', async () => {
    const pending = deferred<TenantSubscription>(); vi.mocked(getTenantSubscription).mockReturnValue(pending.promise);
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(getTenantSubscription).mock.calls[0]![2]!;
    await wrapper.findAll('[data-testid="close"]').at(-1)!.trigger('click'); expect(signal.aborted).toBe(true);
    pending.resolve({ ...row, status: 'Cancelled' }); await flushPromises(); expect(wrapper.find('[data-testid="subscription-detail-id"]').exists()).toBe(false); wrapper.unmount();
  });
  it('同一企业对象更新不会关闭原订阅详情', async () => {
    const { wrapper } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click');
    await wrapper.setProps({ tenant: { ...tenant, name: 'Updated directory name' } });
    expect(wrapper.get('[data-testid="subscription-detail-id"]').text()).toBe(row.id); expect(listTenantSubscriptions).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('撤权取消详情刷新并丢弃迟到内容', async () => {
    const { wrapper, session } = await panel({ readOnly: true }); await wrapper.get('[data-testid="tenant-subscription-detail"]').trigger('click');
    const pending = deferred<TenantSubscription>(); vi.mocked(getTenantSubscription).mockReturnValue(pending.promise);
    await wrapper.get('[data-testid="subscription-detail-refresh"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(getTenantSubscription).mock.calls.at(-1)![2]; session.currentUser!.permissions = []; await flushPromises(); expect(signal?.aborted).toBe(true);
    pending.resolve({ ...row, status: 'Cancelled' }); await flushPromises(); expect(wrapper.find('[data-testid="subscription-detail-id"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['Expired', 'Cancelled'])('终态 %s 不提供取消', async status => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status }])); const { wrapper } = await panel();
    expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(false); wrapper.unmount();
  });
  it('停用租户仍能核对历史及取消，不能新增试用', async () => {
    const { wrapper } = await panel({ active: false }); expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="tenant-subscription-trial"]').exists()).toBe(false); wrapper.unmount();
  });
  it('切换租户后迟到旧列表不能覆盖新列表', async () => {
    const pending = deferred<ReturnType<typeof page>>(); vi.mocked(listTenantSubscriptions).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(page([]));
    const { wrapper } = await panel(); const signal = vi.mocked(listTenantSubscriptions).mock.calls[0]![3];
    await wrapper.setProps({ tenant: { ...tenant, id: id(8), name: 'Other tenant' } }); await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve(page()); await flushPromises();
    expect(wrapper.text()).toContain('Other tenant'); expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([]); wrapper.unmount();
  });
  it('错配租户列表失败关闭写入资格', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, tenantId: id(8) }])); const { wrapper } = await panel();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(false); wrapper.unmount();
  });
  it('取消结果未知先刷新，不自动重发旧版本', async () => {
    vi.mocked(cancelTenantSubscription).mockRejectedValue(new Error('timeout')); const { wrapper } = await panel();
    await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="tenant-subscription-cancel"]').attributes('disabled')).toBeDefined();
    expect(cancelTenantSubscription).toHaveBeenCalledTimes(1); expect(listTenantSubscriptions).toHaveBeenCalledTimes(1);
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status: 'Cancelled', version: 5 }]));
    await wrapper.get('[data-testid="tenant-subscriptions-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('已取消'); expect(cancelTenantSubscription).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('取消已成功但随后重读失败仍保留成功回执', async () => {
    vi.mocked(cancelTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', version: 5 });
    const { wrapper } = await panel(); vi.mocked(listTenantSubscriptions).mockRejectedValue(new Error('read failed'));
    await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-operation-receipt"]').text()).toContain('操作已确认完成'); expect(wrapper.text()).toContain('订阅历史读取失败');
    expect(wrapper.emitted('changed')).toHaveLength(1); wrapper.unmount();
  });
  it('取消响应错配目标时不显示成功或重新启用写入', async () => {
    vi.mocked(cancelTenantSubscription).mockResolvedValue({ ...row, id: id(8), status: 'Cancelled' }); const { wrapper } = await panel();
    await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.emitted('changed')).toBeUndefined();
    expect(wrapper.get('[data-testid="tenant-subscription-cancel"]').attributes('disabled')).toBeDefined(); wrapper.unmount();
  });
  it('撤权会取消办理并丢弃迟到成功', async () => {
    const pending = deferred<typeof row>(); vi.mocked(cancelTenantSubscription).mockReturnValue(pending.promise);
    const { wrapper, session } = await panel(); await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(cancelTenantSubscription).mock.calls[0]![3]; session.currentUser!.permissions = []; await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve({ ...row, status: 'Cancelled' }); await flushPromises(); expect(wrapper.emitted('changed')).toBeUndefined(); wrapper.unmount();
  });
});
