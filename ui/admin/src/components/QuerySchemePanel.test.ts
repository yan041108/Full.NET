import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import QuerySchemePanel from './QuerySchemePanel.vue';
import { useSessionStore } from '../auth/session';
import type { SavedQueryConfiguration } from '@fullnet/client-contracts';
const api = vi.hoisted(() => ({ getQuerySchemeSchema: vi.fn(), listQuerySchemes: vi.fn(), getQueryScheme: vi.fn(), saveQueryScheme: vi.fn(),
  deleteQueryScheme: vi.fn(), shareQueryScheme: vi.fn(), getDefaultQueryScheme: vi.fn(), setDefaultQueryScheme: vi.fn() }));
vi.mock('../api/query-schemes', () => api);
const wrappers: VueWrapper[] = [], user = '019a45b7-2356-7000-8000-000000000001', id = '019a45b7-2356-7000-8000-000000000002', other = '019a45b7-2356-7000-8000-000000000003';
const business = 'enterprise_request.enterprise_requests.read', grid = 'enterprise_request.enterprise_requests';
const permissions = [business, ...['read', 'create', 'update', 'delete', 'share', 'set_default'].map(value => 'settings.query_schemes.' + value)];
const configuration = (): SavedQueryConfiguration => ({ filters: [], sortBy: 'id', descending: false, columns: [{ columnKey: 'title', order: 0, visible: true, width: null, fixed: null }] });
const schema = { gridKey: grid, schemaVersion: 1, readPermission: business, supportsHost: false, supportsTenant: true,
  filters: [{ fieldKey: 'title', maxLength: 200 }], sortKeys: ['id', 'title'], columnKeys: ['title'] };
const detail = (extra = {}, config = configuration()) => ({ summary: { id, ownerUserId: user, name: 'Saved', schemaVersion: 1, isShared: false,
  isCompatible: true, version: 1, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, ...extra }, configuration: config });
const page = (items: unknown[] = [], nextCursor: string | null = null) => ({ items, nextCursor, hasMore: nextCursor !== null });
function render(codes = permissions) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id: user, tenantId: 'tenant', sessionId: 'session', permissions: codes } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(QuerySchemePanel, { props: { gridKey: grid, businessPermission: business, active: true, defaults: configuration, fieldLabel: key => key },
    global: { plugins: [pinia], stubs: { ElPopconfirm: { template: '<span><slot name="reference"/></span>' } } } }); wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { Object.values(api).forEach(mock => mock.mockReset()); api.getQuerySchemeSchema.mockResolvedValue(schema);
  api.listQuerySchemes.mockResolvedValue(page()); api.getDefaultQueryScheme.mockResolvedValue({ scheme: null, version: 0, isUnavailable: false }); api.getQueryScheme.mockResolvedValue(detail()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
async function choose(wrapper: VueWrapper, target = id) {
  const select = wrapper.getComponent('[data-testid="query-scheme-select"]') as VueWrapper; select.vm.$emit('update:modelValue', target); select.vm.$emit('change', target); await flushPromises();
}
describe('受控查询方案面板', () => {
  it('初始默认从服务器恢复，并将受控配置交给原业务列表', async () => {
    const value = configuration(); value.filters = [{ fieldKey: 'title', operatorKey: 'contains', value: 'Literal' }];
    api.getDefaultQueryScheme.mockResolvedValue({ scheme: detail({}, value), version: 2, isUnavailable: false });
    const { wrapper } = render(); await flushPromises(); expect(wrapper.emitted('apply')?.[0]?.[0]).toEqual(value);
    expect((wrapper.get('[data-testid="query-scheme-name"]').element as HTMLInputElement).value).toBe('Saved'); expect(wrapper.emitted('ready')).toHaveLength(1);
  });
  it('新建保存受控条件及零版本，修改保留原方案版本', async () => {
    api.saveQueryScheme.mockResolvedValue(detail({ name: 'Private' })); const { wrapper } = render(); await flushPromises();
    await wrapper.get('[data-testid="query-scheme-name"]').setValue('Private');
    await wrapper.get('[data-testid="query-scheme-filter-title"]').setValue('Needle');
    await wrapper.get('[data-testid="query-scheme-save-new"]').trigger('click'); await flushPromises();
    expect(api.saveQueryScheme.mock.calls[0]!.slice(0, 2)).toEqual([grid, undefined]);
    expect(api.saveQueryScheme.mock.calls[0]![2]).toMatchObject({ name: 'Private', version: 0, schemaVersion: 1, configuration: { filters: [{ fieldKey: 'title', operatorKey: 'contains', value: 'Needle' }] } });
    await wrapper.get('[data-testid="query-scheme-update"]').trigger('click'); await flushPromises(); expect(api.saveQueryScheme.mock.lastCall![1]).toBe(id); expect(api.saveQueryScheme.mock.lastCall![2].version).toBe(1);
  });
  it('共享者不取得创建者的编辑、删除及共享资格', async () => {
    api.getQueryScheme.mockResolvedValue(detail({ ownerUserId: other, isShared: true })); const { wrapper } = render(); await flushPromises(); await choose(wrapper);
    for (const action of ['update', 'delete', 'share']) expect(wrapper.find(`[data-testid="query-scheme-${action}"]`).exists()).toBe(false);
    expect(wrapper.find('[data-testid="query-scheme-save-new"]').exists()).toBe(true);
  });
  it('仅有读取权限时隐藏写动作，但允许使用已有查询条件', async () => {
    const { wrapper } = render([business, 'settings.query_schemes.read']); await flushPromises();
    expect(wrapper.find('[data-testid="query-scheme-save-new"]').exists()).toBe(false); expect(wrapper.get('[data-testid="query-scheme-apply"]').attributes('disabled')).toBeUndefined();
  });
  it('版本冲突保留输入并禁止重复提交，显式刷新后才能再操作', async () => {
    api.getDefaultQueryScheme.mockResolvedValue({ scheme: detail(), version: 1, isUnavailable: false });
    api.saveQueryScheme.mockRejectedValue({ status: 409, code: 'settings.query_scheme.version_conflict', title: 'Conflict' });
    const { wrapper } = render(); await flushPromises(); await wrapper.get('[data-testid="query-scheme-name"]').setValue('Retained');
    await wrapper.get('[data-testid="query-scheme-update"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="query-scheme-name"]').element as HTMLInputElement).value).toBe('Retained');
    expect(wrapper.get('[data-testid="query-scheme-update"]').attributes('disabled')).toBeDefined(); expect(wrapper.text()).toContain('输入已保留');
    await wrapper.get('[data-testid="query-scheme-update"]').trigger('click'); expect(api.saveQueryScheme).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="query-scheme-refresh"]').trigger('click'); await flushPromises(); expect(api.saveQueryScheme).toHaveBeenCalledTimes(1);
  });
  it('旧 Schema 不能应用，创建者可修正为当前结构后保存', async () => {
    api.getQueryScheme.mockResolvedValue({ ...detail({ schemaVersion: 99, isCompatible: false }), configuration: null });
    api.saveQueryScheme.mockResolvedValue(detail({ version: 2 })); const { wrapper } = render(); await flushPromises(); await choose(wrapper);
    expect(wrapper.get('[data-testid="query-scheme-apply"]').attributes('disabled')).toBeDefined(); expect(wrapper.text()).toContain('不能应用');
    await wrapper.get('[data-testid="query-scheme-update"]').trigger('click'); await flushPromises(); expect(api.saveQueryScheme.mock.lastCall![2].schemaVersion).toBe(1);
    expect(wrapper.get('[data-testid="query-scheme-apply"]').attributes('disabled')).toBeUndefined();
  });
  it('两个列表保留自己的游标，不继承其他列表的位置', async () => {
    api.listQuerySchemes.mockImplementation((_grid, shared, cursor) => Promise.resolve(page([], cursor ? null : shared ? 'shared-next' : 'mine-next')));
    const { wrapper } = render(); await flushPromises();
    await wrapper.get('[data-testid="query-scheme-mine-next"]').trigger('click'); await flushPromises();
    expect(api.listQuerySchemes.mock.lastCall?.slice(0, 3)).toEqual([grid, false, 'mine-next']);
    await wrapper.get('[data-testid="query-scheme-shared-next"]').trigger('click'); await flushPromises();
    expect(api.listQuerySchemes.mock.lastCall?.slice(0, 3)).toEqual([grid, true, 'shared-next']);
    await wrapper.get('[data-testid="query-scheme-mine-previous"]').trigger('click'); await flushPromises();
    expect(api.listQuerySchemes.mock.lastCall?.slice(0, 3)).toEqual([grid, false, undefined]);
  });
  it('选择读取失败恢复原目标，不能把旧表单写到新目标', async () => {
    api.getDefaultQueryScheme.mockResolvedValue({ scheme: detail(), version: 0, isUnavailable: false }); api.getQueryScheme.mockRejectedValue({ status: 404, title: 'Withdrawn' });
    api.saveQueryScheme.mockResolvedValue(detail()); const { wrapper } = render(); await flushPromises(); await choose(wrapper, other);
    const select = wrapper.getComponent('[data-testid="query-scheme-select"]') as VueWrapper; expect((select.props() as { modelValue?: string }).modelValue).toBe(id);
    await wrapper.get('[data-testid="query-scheme-update"]').trigger('click'); await flushPromises(); expect(api.saveQueryScheme.mock.lastCall![1]).toBe(id);
  });
  it('默认撤销或目录故障明确反馈，仍通知父列表结束初始化', async () => {
    api.getDefaultQueryScheme.mockResolvedValue({ scheme: null, version: 1, isUnavailable: true }); const { wrapper } = render(); await flushPromises(); expect(wrapper.text()).toContain('原默认方案');
    api.getQuerySchemeSchema.mockRejectedValue(new Error('offline')); await wrapper.get('[data-testid="query-scheme-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.emitted('ready')).toHaveLength(2);
  });
  it('切换上下文取消在途写入，迟到结果不进入新身份，不自动重放', async () => {
    let resolve!: (value: unknown) => void; api.saveQueryScheme.mockImplementation(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = render(); await flushPromises(); await wrapper.get('[data-testid="query-scheme-name"]').setValue('Pending');
    await wrapper.get('[data-testid="query-scheme-save-new"]').trigger('click'); await flushPromises(); const count = wrapper.emitted('apply')!.length;
    const signal = api.saveQueryScheme.mock.calls[0]![3] as AbortSignal; session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(detail()); await flushPromises(); expect(wrapper.emitted('apply')).toHaveLength(count); expect(api.saveQueryScheme).toHaveBeenCalledTimes(1);
  });
});
