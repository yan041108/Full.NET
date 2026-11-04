import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { enableAutoUnmount, flushPromises, mount, shallowMount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessage, ElSelect } from 'element-plus';
import RegistrationWaysView from './RegistrationWaysView.vue';
import { getRegistrationPolicy, listRegistrationWays, updateRegistrationPolicy } from '../api/registration-ways';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { defineComponent, h, KeepAlive, reactive, ref } from 'vue';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';

vi.mock('../api/registration-ways', () => ({
  getRegistrationPolicy: vi.fn(), listRegistrationWays: vi.fn(), updateRegistrationPolicy: vi.fn(),
  createRegistrationWay: vi.fn(), updateRegistrationWay: vi.fn(), deleteRegistrationWay: vi.fn()
}));

enableAutoUnmount(afterEach);

afterEach(() => vi.restoreAllMocks());

const id = '01900000-0000-7000-8000-000000000001';

const policy = { id, registrationMode: 1 as const, isPublicRegistrationEnabled: false, updatedAtUtc: '2026-11-04T00:00:00Z', version: 7 };

const grants = ['identity.registration_ways.read', 'identity.registration_policy.read', 'identity.registration_policy.update'];

function createView(permissions = grants) {
  const pinia = createPinia(); setActivePinia(pinia);
  useSessionStore().currentUser = { id, username: 'operator', displayName: '验收', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  return mount(RegistrationWaysView, { global: { plugins: [pinia], directives: { loading: {} }, stubs: {
    ArtSearchBar: true, ArtTableHeader: { template: '<div><slot name="left"/></div>' },
    ArtTableActionGroup: { template: '<div><slot/></div>' },
    ArtFormDialog: { props: ['open', 'title'], template: '<section v-if="open" :data-title="title"><slot/></section>' }
  } } });
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getRegistrationPolicy).mockResolvedValue(policy);
  vi.mocked(listRegistrationWays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 });
  vi.mocked(updateRegistrationPolicy).mockResolvedValue({ ...policy, registrationMode: 2, isPublicRegistrationEnabled: true, version: 8 });
});

describe('注册策略三态管理', () => {
  it('展示仅邀请模式，显式保存开放模式与当前版本', async () => {
    const wrapper = createView(); await flushPromises();
    expect(wrapper.findComponent(ElSelect).props('modelValue')).toBe(1);
    expect(wrapper.get('[data-testid="registration-policy-save"]').attributes('disabled')).toBeDefined();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 2); await wrapper.vm.$nextTick();
    await wrapper.get('[data-testid="registration-policy-save"]').trigger('click'); await flushPromises();
    expect(updateRegistrationPolicy).toHaveBeenCalledExactlyOnceWith({ registrationMode: 2, isPublicRegistrationEnabled: true, version: 7 });
    expect(wrapper.get('[data-testid="registration-policy-save"]').attributes('disabled')).toBeDefined();
  });

  it('冲突后禁止自动重放，重新读取再以新版本保存', async () => {
    vi.mocked(updateRegistrationPolicy).mockRejectedValueOnce({ status: 409, title: '注册策略版本冲突', code: 'identity.registration_policy.version_conflict' });
    const wrapper = createView(); await flushPromises();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 2); await wrapper.vm.$nextTick();
    await wrapper.get('[data-testid="registration-policy-save"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('结果或版本未确认');
    expect(wrapper.findComponent(ElSelect).props('disabled')).toBe(true);
    expect(updateRegistrationPolicy).toHaveBeenCalledTimes(1);
    vi.mocked(getRegistrationPolicy).mockResolvedValue({ ...policy, registrationMode: 0, version: 8 });
    await wrapper.findAll('button').find(button => button.text() === '重新读取注册策略')!.trigger('click'); await flushPromises();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 1); await wrapper.vm.$nextTick();
    await wrapper.get('[data-testid="registration-policy-save"]').trigger('click'); await flushPromises();
    expect(updateRegistrationPolicy).toHaveBeenLastCalledWith({ registrationMode: 1, isPublicRegistrationEnabled: false, version: 8 });
  });

  it('只有方式权限时不请求或展示策略，列表继续加载', async () => {
    const wrapper = createView(['identity.registration_ways.read']); await flushPromises();
    expect(getRegistrationPolicy).not.toHaveBeenCalled();
    expect(wrapper.find('.registration-policy-card').exists()).toBe(false);
    expect(listRegistrationWays).toHaveBeenCalledOnce();
  });

  it('保存中防重入，响应未知后重新读取确认已提交结果', async () => {
    let fail!: (error: Error) => void;
    vi.mocked(updateRegistrationPolicy).mockReturnValueOnce(new Promise((_resolve, reject) => { fail = reject; }));
    const wrapper = createView(); await flushPromises();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 2); await wrapper.vm.$nextTick();
    await wrapper.get('[data-testid="registration-policy-save"]').trigger('click');
    await wrapper.get('[data-testid="registration-policy-save"]').trigger('click');
    expect(updateRegistrationPolicy).toHaveBeenCalledTimes(1);
    fail(new Error('response lost')); await flushPromises();
    expect(wrapper.text()).toContain('注册策略保存未确认');
    expect(wrapper.findComponent(ElSelect).props('disabled')).toBe(true);
    vi.mocked(getRegistrationPolicy).mockResolvedValue({ ...policy, registrationMode: 2, isPublicRegistrationEnabled: true, version: 8 });
    await wrapper.findAll('button').find(button => button.text() === '重新读取注册策略')!.trigger('click'); await flushPromises();
    expect(wrapper.findComponent(ElSelect).props('modelValue')).toBe(2);
    expect(wrapper.get('[data-testid="registration-policy-save"]').attributes('disabled')).toBeDefined();
    expect(updateRegistrationPolicy).toHaveBeenCalledTimes(1);
  });

  it('读取失败可见且不阻塞注册方式列表', async () => {
    vi.mocked(getRegistrationPolicy).mockRejectedValue(new Error('unavailable'));
    const wrapper = createView(); await flushPromises();
    expect(wrapper.text()).toContain('注册策略读取失败');
    expect(wrapper.get('[data-testid="registration-policy-save"]').attributes('disabled')).toBeDefined();
    expect(listRegistrationWays).toHaveBeenCalledOnce();
  });

  it('只读权限禁止修改，文案随语言更新', async () => {
    const wrapper = createView(grants.filter(value => !value.endsWith('.update'))); await flushPromises();
    expect(wrapper.findComponent(ElSelect).props('disabled')).toBe(true);
    expect(wrapper.find('[data-testid="registration-policy-save"]').exists()).toBe(false);
    useAdminI18n().setLocale('en-US'); await wrapper.vm.$nextTick();
    expect(wrapper.get('input[role="combobox"]').attributes('aria-label')).toBe('Registration mode');
  });

  it('注册方式读取者可查看链接，复制失败后保留手动复制入口', async () => {
    vi.mocked(listRegistrationWays).mockResolvedValue({ items: [{ id, tenantId: id, name: '测试入口', code: 'test', isEnabled: true,
      roleId: id, organizationUnitId: id, positionId: null, sortOrder: 0, remark: null, createdAtUtc: policy.updatedAtUtc, updatedAtUtc: null, version: 1 }],
      total: 1, page: 1, pageSize: 20 });
    const descriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    const writeText = vi.fn().mockRejectedValueOnce(new Error('blocked')).mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    vi.spyOn(ElMessage, 'error').mockReturnValue({ close: vi.fn() });
    vi.spyOn(ElMessage, 'success').mockReturnValue({ close: vi.fn() });
    try {
      const wrapper = createView(['identity.registration_ways.read']); await flushPromises();
      await wrapper.get('[data-testid="registration-way-link"]').trigger('click'); await flushPromises();
      const input = wrapper.get('input[aria-label="注册链接"]');
      const value = (input.element as HTMLInputElement).value;
      expect(new URL(value).origin).toBe(window.location.origin);
      expect(new URL(value).hash).toBe(`#/register?registrationWayId=${id}`);
      expect(input.attributes('readonly')).toBeDefined();
      await wrapper.get('[data-testid="registration-link-copy"]').trigger('click'); await flushPromises();
      expect(ElMessage.error).toHaveBeenCalledWith('无法自动复制，请选中链接后手动复制。');
      expect((input.element as HTMLInputElement).value).toBe(value);
      await wrapper.get('[data-testid="registration-link-copy"]').trigger('click'); await flushPromises();
      expect(writeText).toHaveBeenLastCalledWith(value);
      expect(ElMessage.success).toHaveBeenCalledWith('注册链接已复制。');
    } finally {
      if (descriptor) Object.defineProperty(navigator, 'clipboard', descriptor);
      else Reflect.deleteProperty(navigator, 'clipboard');
    }
  });
});

let session: { currentUser: { id: string; sessionId: string } };

let canUpdate = true;

vi.mock('../auth/permission', () => ({ usePermission: () => ({ can: () => canUpdate }) }));

vi.mock('../framework/art-design/composables/useArtCrudTableLayout', () => ({
  useArtCrudTableLayout: () => ({ tableMainRef: ref(), tableHeight: ref(360), tableSize: ref('default'),
    tableZebra: ref(true), tableBorder: ref(true), tableHeaderBackground: ref(true),
    tableHeaderCellStyle: ref({}), updateTableHeight: vi.fn(), watchLoading: vi.fn() })
}));

vi.mock('../auth/session', () => ({ useSessionStore: () => session }));

vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn(), showProblem: vi.fn() }));

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
