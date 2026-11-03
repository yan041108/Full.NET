import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import RegistrationWaysView from './RegistrationWaysView.vue';
import { getRegistrationPolicy, listRegistrationWays, updateRegistrationPolicy } from '../api/registration-ways';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

vi.mock('../api/registration-ways', () => ({
  getRegistrationPolicy: vi.fn(), listRegistrationWays: vi.fn(), updateRegistrationPolicy: vi.fn(),
  createRegistrationWay: vi.fn(), updateRegistrationWay: vi.fn(), deleteRegistrationWay: vi.fn()
}));
enableAutoUnmount(afterEach);
const id = '01900000-0000-7000-8000-000000000001';
const policy = { id, registrationMode: 1 as const, isPublicRegistrationEnabled: false, updatedAtUtc: '2026-11-04T00:00:00Z', version: 7 };
const grants = ['identity.registration_ways.read', 'identity.registration_policy.read', 'identity.registration_policy.update'];
function createView(permissions = grants) {
  const pinia = createPinia(); setActivePinia(pinia);
  useSessionStore().currentUser = { id, username: 'operator', displayName: '验收', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  return mount(RegistrationWaysView, { global: { plugins: [pinia], directives: { loading: {} }, stubs: {
    ArtSearchBar: true, ArtTableHeader: { template: '<div><slot name="left"/></div>' }
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
});
