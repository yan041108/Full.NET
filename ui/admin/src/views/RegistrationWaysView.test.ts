import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, shallowMount } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, reactive, ref } from 'vue';
import RegistrationWaysView from './RegistrationWaysView.vue';
import { getRegistrationPolicy, listRegistrationWays, updateRegistrationPolicy } from '../api/registration-ways';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';

let session: { currentUser: { id: string; sessionId: string } };
let canUpdate = true;
vi.mock('../auth/permission', () => ({ usePermission: () => ({ can: () => canUpdate }) }));
vi.mock('../framework/art-design/composables/useArtCrudTableLayout', () => ({
  useArtCrudTableLayout: () => ({ tableMainRef: ref(), tableHeight: ref(360), tableSize: ref('default'),
    tableZebra: ref(true), tableBorder: ref(true), tableHeaderBackground: ref(true),
    tableHeaderCellStyle: ref({}), updateTableHeight: vi.fn(), watchLoading: vi.fn() })
}));
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('../api/registration-ways', () => ({
  getRegistrationPolicy: vi.fn(), listRegistrationWays: vi.fn(), updateRegistrationPolicy: vi.fn(),
  createRegistrationWay: vi.fn(), updateRegistrationWay: vi.fn(), deleteRegistrationWay: vi.fn()
}));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn(), showProblem: vi.fn() }));
const policy = { id: '018f5f40-0000-7000-8000-000000000001', registrationMode: 1 as const, isPublicRegistrationEnabled: false, updatedAtUtc: '2026-10-07T00:00:00Z', version: 1 };
const options = { global: { renderStubDefaultSlot: true, stubs: {
  PermissionGate: { template: '<slot />' }, ElTableColumn: { template: '<div />' }
} } };
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((r, j) => { resolve = r; reject = j; });
  return { promise, resolve, reject };
}
function select(wrapper: ReturnType<typeof shallowMount>) { return wrapper.getComponent({ name: 'ElSelect' }); }

describe('注册策略管理边界', () => {
  beforeEach(() => {
    vi.resetAllMocks(); useAdminI18n().setLocale('en-US');
    canUpdate = true;
    session = reactive({ currentUser: { id: 'admin', sessionId: 'session' } });
    vi.mocked(getRegistrationPolicy).mockResolvedValue(policy);
    vi.mocked(listRegistrationWays).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });
  it('renders the create action through the actual table header slot', async () => {
    const wrapper = shallowMount(RegistrationWaysView, {
      ...options, global: { ...options.global, stubs: { ...options.global.stubs, ArtTableHeader: false } }
    });
    await flushPromises();
    expect(wrapper.find('[data-testid="registration-ways-action-create"]').exists()).toBe(true);
    wrapper.unmount();
  });
  it('shows the saved mode to readers without exposing update controls', async () => {
    canUpdate = false;
    const wrapper = shallowMount(RegistrationWaysView, {
      ...options, global: { ...options.global, stubs: { ...options.global.stubs, PermissionGate: false } }
    });
    await flushPromises();
    expect(wrapper.get('[data-testid="registration-policy-current"]').text()).toBe('Invitation only');
    expect(wrapper.find('[data-testid="registration-policy-mode"]').exists()).toBe(false);
    expect(updateRegistrationPolicy).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('ignores an old policy read after the session changes', async () => {
    const pending = deferred<typeof policy>();
    vi.mocked(getRegistrationPolicy).mockReturnValueOnce(pending.promise)
      .mockResolvedValueOnce({ ...policy, registrationMode: 0 });
    const wrapper = shallowMount(RegistrationWaysView, options);
    session.currentUser.sessionId = 'next'; await flushPromises();
    pending.resolve(policy); await flushPromises();
    expect(wrapper.get('[data-testid="registration-policy-current"]').text()).toBe('Registration disabled');
    wrapper.unmount();
  });
  it.each([0, 1, 2] as const)('saves explicit mode %s with its version', async mode => {
    vi.mocked(updateRegistrationPolicy).mockResolvedValue({ ...policy, registrationMode: mode, isPublicRegistrationEnabled: mode === 2, version: 2 });
    const wrapper = shallowMount(RegistrationWaysView, options); await flushPromises();
    select(wrapper).vm.$emit('change', mode); await flushPromises();
    expect(updateRegistrationPolicy).toHaveBeenCalledWith({ registrationMode: mode, isPublicRegistrationEnabled: mode === 2, version: 1 });
    wrapper.unmount();
  });
  it('prevents duplicate updates while pending', async () => {
    const pending = deferred<typeof policy>(); vi.mocked(updateRegistrationPolicy).mockReturnValue(pending.promise);
    const wrapper = shallowMount(RegistrationWaysView, options); await flushPromises();
    select(wrapper).vm.$emit('change', 0); select(wrapper).vm.$emit('change', 2);
    expect(updateRegistrationPolicy).toHaveBeenCalledTimes(1);
    pending.resolve(policy); await flushPromises(); wrapper.unmount();
  });
  it('loads ways even if policy fails and permits retry', async () => {
    vi.mocked(getRegistrationPolicy).mockRejectedValueOnce(new Error('network'));
    const wrapper = shallowMount(RegistrationWaysView, options); await flushPromises();
    expect(listRegistrationWays).toHaveBeenCalledTimes(1); expect(showProblem).toHaveBeenCalled();
    expect(select(wrapper).attributes('disabled')).toBeDefined();
    wrapper.get('[data-testid="registration-policy-retry"]').trigger('click'); await flushPromises();
    expect(getRegistrationPolicy).toHaveBeenCalledTimes(2); wrapper.unmount();
  });
  it('reports update failure without replacing the saved policy', async () => {
    vi.mocked(updateRegistrationPolicy).mockRejectedValue(new Error('conflict'));
    const wrapper = shallowMount(RegistrationWaysView, options); await flushPromises();
    select(wrapper).vm.$emit('change', 0); await flushPromises();
    expect(showProblem).toHaveBeenCalled(); expect(showSuccess).not.toHaveBeenCalled();
    expect(select(wrapper).props('modelValue')).toBe(1); wrapper.unmount();
  });
  it.each(['unmount', 'session'] as const)('ignores late update after %s', async action => {
    const pending = deferred<typeof policy>(); vi.mocked(updateRegistrationPolicy).mockReturnValue(pending.promise);
    const wrapper = shallowMount(RegistrationWaysView, options); await flushPromises();
    select(wrapper).vm.$emit('change', 0);
    if (action === 'unmount') wrapper.unmount(); else session.currentUser.sessionId = 'next';
    await flushPromises(); pending.resolve(policy); await flushPromises();
    expect(showSuccess).not.toHaveBeenCalled(); if (action !== 'unmount') wrapper.unmount();
  });
  it('invalidates late updates while cached and reloads on activation', async () => {
    const pending = deferred<typeof policy>(); vi.mocked(updateRegistrationPolicy).mockReturnValue(pending.promise);
    const active = ref(true);
    const host = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => active.value ? h(RegistrationWaysView) : null }) });
    const wrapper = shallowMount(host, { ...options, global: { ...options.global, stubs: { ...options.global.stubs, KeepAlive: false, RegistrationWaysView: false } } });
    await flushPromises(); select(wrapper).vm.$emit('change', 0); active.value = false; await flushPromises();
    pending.resolve(policy); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled();
    active.value = true; await flushPromises(); expect(getRegistrationPolicy).toHaveBeenCalledTimes(2); wrapper.unmount();
  });
});
