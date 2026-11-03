import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useSessionStore } from '../auth/session';
import TenantRegistrationProgress from './TenantRegistrationProgress.vue';
import * as api from '../api/my-tenant-registrations';
import { showSuccess, showWarning } from '../feedback/fullNetMessage';

vi.mock('../api/my-tenant-registrations', () => ({ listMyTenantRegistrations: vi.fn(), continueMyTenantRegistration: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const initial = { id: '01981a3f-00c0-7000-8000-000000000010', tenantId: '01981a3f-00c0-7000-8000-000000000011',
  registrationWayName: '公开注册', phase: 'Cancelled' as const, errorCode: 'quota.rejected', createdAtUtc: '2026-11-04T00:00:00Z', version: 7, canContinue: true };
let wrapper: ReturnType<typeof mount> | undefined;
function setup(permissions = ['identity.tenant_registrations.read_self', 'identity.tenant_registrations.continue_self']) {
  const pinia = createPinia(); setActivePinia(pinia);
  const session = useSessionStore();
  session.$patch({ state: 'authenticated', currentUser: { id: 'self', username: 'self', displayName: 'Self', tenantId: null,
    actorScope: 'host', scope: 'host', isSuperAdministrator: false, permissions, sessionId: 'session', preferredLocale: 'zh-CN', profileVersion: 1 } });
  const reload = vi.spyOn(session, 'reloadContext').mockResolvedValue(undefined);
  wrapper = mount(TenantRegistrationProgress, { global: { plugins: [pinia] } });
  return { session, reload };
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.listMyTenantRegistrations).mockResolvedValue([initial]); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.useRealTimers(); });
describe('本人注册入驻面板', () => {
  it('只有读取权限时展示进度且不提供继续操作', async () => {
    setup(['identity.tenant_registrations.read_self']); await flushPromises();
    expect(wrapper!.text()).toContain('开通未完成');
    expect(wrapper!.text()).not.toContain('继续办理');
  });
  it('无读取权限时不调用接口', async () => { setup([]); await flushPromises(); expect(api.listMyTenantRegistrations).not.toHaveBeenCalled(); });
  it('继续携带原始版本，未完成响应不能提示已加入', async () => {
    vi.mocked(api.continueMyTenantRegistration).mockResolvedValue({ ...initial, version: 9 });
    const { reload } = setup(); await flushPromises();
    await wrapper!.findAll('button').find(button => button.text() === '继续办理')!.trigger('click'); await flushPromises();
    expect(api.continueMyTenantRegistration).toHaveBeenCalledWith(initial.id, 7);
    expect(showSuccess).not.toHaveBeenCalled(); expect(showWarning).toHaveBeenCalledOnce(); expect(reload).not.toHaveBeenCalled();
  });
  it('激活成功后重新读取可用企业', async () => {
    vi.mocked(api.continueMyTenantRegistration).mockResolvedValue({ ...initial, phase: 'Activated', canContinue: false, version: 10 });
    const { reload } = setup(); await flushPromises();
    vi.mocked(api.listMyTenantRegistrations).mockResolvedValue([{ ...initial, phase: 'Activated', canContinue: false, version: 10 }]);
    await wrapper!.findAll('button').find(button => button.text() === '继续办理')!.trigger('click'); await flushPromises();
    expect(reload).toHaveBeenCalledOnce(); expect(showSuccess).toHaveBeenCalledOnce(); expect(wrapper!.text()).toContain('已加入企业');
  });
  it('租户切换后丢弃旧请求结果', async () => {
    let complete!: (rows: typeof initial[]) => void;
    vi.mocked(api.listMyTenantRegistrations).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
    const { session } = setup(); await flushPromises();
    session.currentUser!.tenantId = initial.tenantId; await flushPromises();
    vi.mocked(api.listMyTenantRegistrations).mockResolvedValue([]);
    complete([{ ...initial, registrationWayName: '旧请求' }]); await flushPromises();
    expect(wrapper!.text()).not.toContain('旧请求');
  });
});
