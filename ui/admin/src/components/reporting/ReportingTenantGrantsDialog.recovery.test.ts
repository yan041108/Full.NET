import { mount, flushPromises, DOMWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ElInput, ElPagination, ElSelect, vLoading } from 'element-plus';
import ReportingTenantGrantsDialog from './ReportingTenantGrantsDialog.vue';
import ArtFormDialog from '../../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../../auth/session';
import { listReportingDefinitionVersions, listReportingTenantVersionGrants, setReportingTenantVersionGrant } from '../../api/reporting-definitions';
import { showSuccess, showProblem, showWarning } from '../../feedback/fullNetMessage';

vi.mock('../../api/reporting-definitions', () => ({listReportingDefinitionVersions:vi.fn(),listReportingTenantVersionGrants:vi.fn(),setReportingTenantVersionGrant:vi.fn()}));
vi.mock('../../feedback/fullNetMessage', () => ({showSuccess:vi.fn(),showProblem:vi.fn(),showWarning:vi.fn()}));
const body = new DOMWrapper(document.body);
const mounted: ReturnType<typeof mount>[] = [];
afterEach(() => { for (const wrapper of mounted.splice(0)) wrapper.unmount(); });
const definition = {id:'019bc2b1-2a40-7cc3-8992-a80de51bf291',name:'Fixture',latestPublishedVersionNumber:2};
const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf292';
const versions = vi.mocked(listReportingDefinitionVersions); const grants = vi.mocked(listReportingTenantVersionGrants); const setGrant = vi.mocked(setReportingTenantVersionGrant);
function deferred<T>() { let resolve!: (v:T)=>void; let reject!: (e:unknown)=>void; const promise = new Promise<T>((r,j)=>{resolve=r;reject=j;});return {promise,resolve,reject}; }
function create(permissions = ['reporting.definitions.read','reporting.definitions.grant_tenants'], scope = 'host') {
 const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(); session.state='authenticated';
 session.currentUser={id:tenant,username:'host',displayName:'Host',tenantId:scope==='host'?null:tenant,scope:scope as 'host',actorScope:scope as 'host',
  isSuperAdministrator:false,passwordChangeRequired:false,permissions,sessionId:definition.id,preferredLocale:'zh-CN',profileVersion:1};
 // 使用真实 Teleport，避免弹窗与选择器嵌套传送门的替身引起递归更新。
 const wrapper=mount(ReportingTenantGrantsDialog,{props:{definition},global:{plugins:[pinia],directives:{loading:vLoading},stubs:{Teleport:false}}});
 mounted.push(wrapper);return {session,wrapper};
}
async function enter(wrapper:ReturnType<typeof create>['wrapper'], value=tenant) { wrapper.getComponent(ElInput).vm.$emit('update:modelValue',value); await flushPromises(); }

const other = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
function state(wrapper: ReturnType<typeof create>['wrapper']) { return wrapper.vm as unknown as { selectedVersion?: number; tenantId: string; tenants: string[]; total: number; receipt?: { outcome: string; target: string; version: number }; loadVersions: () => Promise<void>; loadGrants: (page: number) => Promise<void>; changeGrant: (tenant: string, grant: boolean) => Promise<void>; finishConfirmation: (confirmed: boolean) => void }; }
beforeEach(() => { vi.resetAllMocks(); versions.mockResolvedValue([1, 2].map(n => ({ id: tenant.slice(0, -1) + n, definitionId: definition.id, versionNumber: n, dataSourceId: tenant, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}' , changeNote: null, publishedByUserId: tenant, publishedAtUtc: '2026-10-16T00:00:00Z' }))); grants.mockResolvedValue({ items: [tenant, other], page: 1, pageSize: 20, total: 2 }); setGrant.mockResolvedValue(true); });
describe('报表授权结果与恢复', () => {
  it('撤销确认成功先移除该租户，补读失败保留其余授权与回执', async () => {
    const { wrapper } = create(); await flushPromises(); grants.mockRejectedValueOnce(new Error('offline')); const action = state(wrapper).changeGrant(tenant, false); state(wrapper).finishConfirmation(true); await action;
    expect(state(wrapper).tenants).toEqual([other]); expect(state(wrapper).total).toBe(1); expect(state(wrapper).receipt).toMatchObject({ target: tenant, version: 2, outcome: 'succeeded' });
  });
  it('不属于当前页的租户不能从陈旧撤销入口写入', async () => {
    const { wrapper } = create(); await flushPromises(); await state(wrapper).changeGrant(definition.id, false); expect(setGrant).not.toHaveBeenCalled(); expect(body.find('[data-testid="reporting-grant-confirm-revoke"]').exists()).toBe(false);
  });
  it('版本必须来自实际目录，不能伪造版本发起授权', async () => {
    const { wrapper } = create(); await flushPromises(); state(wrapper).selectedVersion = 99; await state(wrapper).changeGrant(tenant, true); expect(setGrant).not.toHaveBeenCalled();
  });
  it('直接变更版本立即解除旧确认并取消在途操作', async () => {
    const { wrapper } = create(); await flushPromises(); const action = state(wrapper).changeGrant(tenant, false); state(wrapper).selectedVersion = 1; await action;
    expect(body.find('[data-testid="reporting-grant-confirm-revoke"]').exists()).toBe(false); expect(setGrant).not.toHaveBeenCalled(); expect(state(wrapper).tenants).toEqual([]);
  });
  it('旧确认与关闭回调不能批准或关闭下一项确认', async () => {
    const { wrapper } = create(); await flushPromises(); const first = state(wrapper).changeGrant(tenant, false); await flushPromises();
    const dialog = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === 'reporting-grant-confirm-revoke')!;
    const accept = dialog.vm.$.vnode.props!.onConfirm as () => void, close = dialog.vm.$.vnode.props!['onUpdate:open'] as (open: boolean) => void;
    state(wrapper).finishConfirmation(false); await first; const second = state(wrapper).changeGrant(other, false); accept(); close(false); await flushPromises();
    expect(setGrant).not.toHaveBeenCalled(); state(wrapper).finishConfirmation(false); await second;
  });
  it('未知操作失败刷新不解锁，成功读取原版本后才能显式重试', async () => {
    const { wrapper } = create(); await flushPromises(); setGrant.mockRejectedValueOnce(new Error('lost')); await state(wrapper).changeGrant(tenant, true); await state(wrapper).changeGrant(tenant, true); expect(setGrant).toHaveBeenCalledOnce();
    grants.mockRejectedValueOnce(new Error('offline')); await state(wrapper).loadGrants(1); await state(wrapper).changeGrant(tenant, true); expect(setGrant).toHaveBeenCalledOnce();
    await state(wrapper).loadGrants(1); await state(wrapper).changeGrant(tenant, true); expect(setGrant).toHaveBeenCalledTimes(2);
  });
  it('读取其他版本不能解除原版本的未知提交门槛', async () => {
    const { wrapper } = create(); await flushPromises(); setGrant.mockRejectedValueOnce(new Error('lost')); await state(wrapper).changeGrant(tenant, true);
    state(wrapper).selectedVersion = 1; await state(wrapper).loadGrants(1); state(wrapper).selectedVersion = 2; await state(wrapper).changeGrant(tenant, true); expect(setGrant).toHaveBeenCalledOnce();
  });
  it('其他租户的失败回执不能覆盖原租户的未知提交门槛', async () => {
    const { wrapper } = create(); await flushPromises(); setGrant.mockRejectedValue(new Error('lost')); await state(wrapper).changeGrant(tenant, true); await state(wrapper).changeGrant(other, true); await state(wrapper).changeGrant(tenant, true); expect(setGrant).toHaveBeenCalledTimes(2);
  });
  it('操作确认后更早发起的目录读取不能恢复已撤销行', async () => {
    const { wrapper } = create(); await flushPromises(); const mutation = deferred<boolean>(); setGrant.mockReturnValueOnce(mutation.promise);
    const action = state(wrapper).changeGrant(tenant, false); state(wrapper).finishConfirmation(true); await flushPromises();
    const old = deferred<Awaited<ReturnType<typeof listReportingTenantVersionGrants>>>(); grants.mockReturnValueOnce(old.promise); const reading = state(wrapper).loadGrants(1);
    grants.mockRejectedValueOnce(new Error('offline')); mutation.resolve(true); await action; old.resolve({ items: [tenant, other], page: 1, pageSize: 20, total: 2 }); await reading;
    expect(state(wrapper).tenants).toEqual([other]);
  });
  it('在途提交切换版本保留原版本未知状态，晚结果不能反馈成功', async () => {
    const { wrapper } = create(); await flushPromises(); const pending = deferred<boolean>(); setGrant.mockReturnValueOnce(pending.promise); const action = state(wrapper).changeGrant(tenant, true);
    state(wrapper).selectedVersion = 1; pending.resolve(true); await action; state(wrapper).selectedVersion = 2; await state(wrapper).changeGrant(tenant, true);
    expect(showSuccess).not.toHaveBeenCalled(); expect(setGrant).toHaveBeenCalledOnce();
  });
  it('明确403拒绝仍允许手动重试，不伪造为已完成', async () => {
    const { wrapper } = create(); await flushPromises(); setGrant.mockRejectedValueOnce({ status: 403, code: 'authorization.permission_denied', title: 'Denied' }); await state(wrapper).changeGrant(tenant, true);
    expect(state(wrapper).receipt?.outcome).toBe('failed'); await state(wrapper).changeGrant(tenant, true); expect(setGrant).toHaveBeenCalledTimes(2);
  });
  it.each([0, 302])('非错误 HTTP 状态 %s 不能作为明确拒绝解除重发门槛', async status => {
    const { wrapper } = create(); await flushPromises(); setGrant.mockRejectedValueOnce({ status, code: 'gateway.invalid_status' });
    await state(wrapper).changeGrant(tenant, true); await state(wrapper).changeGrant(tenant, true);
    expect(setGrant).toHaveBeenCalledOnce(); expect(state(wrapper).receipt?.outcome).toBe('unconfirmed');
  });
  it('版本目录失败后可以用可见入口重试', async () => {
    versions.mockRejectedValueOnce(new Error('offline')); const { wrapper } = create(); await flushPromises(); expect(grants).not.toHaveBeenCalled();
    await body.get('[data-testid="reporting-grant-versions-refresh"]').trigger('click'); await flushPromises(); expect(versions).toHaveBeenCalledTimes(2); expect(grants).toHaveBeenCalledOnce();
  });
  it('重新读取版本失败保留错误，不用旧版本继续补读掩盖失败', async () => {
    const { wrapper } = create(); await flushPromises(); versions.mockRejectedValueOnce(new Error('offline')); await state(wrapper).loadVersions();
    expect(grants).toHaveBeenCalledOnce(); expect(state(wrapper).selectedVersion).toBe(2);
    expect((wrapper.vm as unknown as { problem?: { code: string } }).problem?.code).toBe('client.unexpected_error');
  });
  it('关闭后即使父组件暂未卸载也不能再次直接写入', async () => {
    const { wrapper } = create(); await flushPromises(); wrapper.getComponent(ArtFormDialog).vm.$emit('update:open', false); await state(wrapper).changeGrant(tenant, true);
    expect(setGrant).not.toHaveBeenCalled(); expect(wrapper.emitted('close')).toHaveLength(1);
  });
  it('授权成功只清除本次输入，保留随后输入的其他租户', async () => {
    const { wrapper } = create(); await flushPromises(); state(wrapper).tenantId = tenant; const pending = deferred<boolean>(); setGrant.mockReturnValueOnce(pending.promise); const action = state(wrapper).changeGrant(tenant, true);
    state(wrapper).tenantId = other; pending.resolve(true); await action; expect(state(wrapper).tenantId).toBe(other);
  });
  it('旧行回调不能把撤销目标迁移到新版本', async () => {
    const { wrapper } = create(); await flushPromises();
    const oldRow = (wrapper.vm as unknown as { grantRows: { revoke: () => void }[] }).grantRows[0]!;
    state(wrapper).selectedVersion = 1; await state(wrapper).loadGrants(1); oldRow.revoke(); await flushPromises();
    expect(setGrant).not.toHaveBeenCalled(); expect(body.find('[data-testid="reporting-grant-confirm-revoke"]').exists()).toBe(false);
  });
  it('旧选择器回调不能改变新版本的选择', async () => {
    const { wrapper } = create(); await flushPromises();
    const oldSelect = wrapper.getComponent(ElSelect).vm.$.vnode.props!['onUpdate:modelValue'] as (value: number) => void;
    oldSelect(1); await flushPromises(); oldSelect(2); await flushPromises();
    expect(state(wrapper).selectedVersion).toBe(1); expect(grants).toHaveBeenCalledTimes(2);
  });
  it('页码模型重复事件只读一次，新页读取失败保留可重试入口', async () => {
    const { wrapper } = create(); await flushPromises();
    wrapper.getComponent(ElPagination).vm.$emit('current-change', 1); await flushPromises(); expect(grants).toHaveBeenCalledOnce();
    grants.mockRejectedValueOnce(new Error('offline')); wrapper.getComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(state(wrapper).tenants).toEqual([]); expect(grants).toHaveBeenCalledTimes(2);
    grants.mockResolvedValueOnce({ items: [other], page: 2, pageSize: 20, total: 21 });
    await body.get('[data-testid="reporting-grant-refresh"]').trigger('click'); await flushPromises(); expect(state(wrapper).tenants).toEqual([other]);
  });
  it('拒绝非法页码，不向后端发起读取', async () => {
    const { wrapper } = create(); await flushPromises();
    for (const page of [0, -1, 1.5, Number.NaN]) await state(wrapper).loadGrants(page);
    expect(grants).toHaveBeenCalledOnce();
  });
  it('空越界页仅补读一次末页，并发总数再变化不循环', async () => {
    const { wrapper } = create(); await flushPromises();
    grants.mockResolvedValueOnce({ items: [], page: 3, pageSize: 20, total: 21 }).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 0 });
    await state(wrapper).loadGrants(3); expect(grants).toHaveBeenCalledTimes(3);
    expect(grants).toHaveBeenLastCalledWith(definition.id, 2, 2, 20, expect.any(AbortSignal)); expect(state(wrapper).tenants).toEqual([]);
  });
  it('旧版本目录迟到不能替换较新目录与授权选择', async () => {
    const { wrapper } = create(); await flushPromises(); const rows = await versions.mock.results[0]!.value;
    const old = deferred<Awaited<ReturnType<typeof listReportingDefinitionVersions>>>(); versions.mockReturnValueOnce(old.promise);
    const oldRead = state(wrapper).loadVersions(); const oldSignal = versions.mock.calls[1]![1]!;
    versions.mockResolvedValueOnce(rows.filter((row: { versionNumber: number }) => row.versionNumber === 1)); await state(wrapper).loadVersions();
    expect(oldSignal.aborted).toBe(true); old.resolve(rows); await oldRead; expect(state(wrapper).selectedVersion).toBe(1);
  });
  it('false 写响应属于未知结果，不能报告成功或立即重发', async () => {
    const { wrapper } = create(); await flushPromises(); setGrant.mockResolvedValueOnce(false);
    await state(wrapper).changeGrant(tenant, true); await state(wrapper).changeGrant(tenant, true);
    expect(setGrant).toHaveBeenCalledOnce(); expect(showSuccess).not.toHaveBeenCalled(); expect(state(wrapper).receipt?.outcome).toBe('unconfirmed');
  });
  it('UUID 大小写等价的定义替换保留已选版本', async () => {
    const { wrapper } = create(); await flushPromises(); await wrapper.setProps({ definition: { ...definition, id: definition.id.toUpperCase() } });
    expect(wrapper.emitted('close')).toBeUndefined(); await state(wrapper).changeGrant(tenant.toUpperCase(), true);
    expect(setGrant).toHaveBeenCalledWith(definition.id, 2, tenant, true, expect.any(AbortSignal));
  });
});
