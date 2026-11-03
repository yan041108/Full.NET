import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils';
import { ElMessage } from 'element-plus';
import RegisterView from './RegisterView.vue';
import RecoverPasswordView from './RecoverPasswordView.vue';
import { requestEmailChallenge, recoverPassword, registerAccount, confirmRecoverPassword, verifyInvitation } from '../api/public-auth';
import { useAdminI18n } from '../i18n/adminI18n';
import { forgetRegistrationInvitation, readRegistrationInvitation, rememberRegistrationInvitation } from '../auth/registration-invitation';

vi.mock('../api/public-auth', () => ({
  requestEmailChallenge: vi.fn(), recoverPassword: vi.fn(), registerAccount: vi.fn(),
  confirmRecoverPassword: vi.fn(), verifyInvitation: vi.fn()
}));
const routeState = vi.hoisted(() => ({ query: {} as Record<string, string | string[]>, path: '/register' }));
const replaceRoute = vi.hoisted(() => vi.fn());
vi.mock('vue-router', () => ({
  useRoute: () => routeState, useRouter: () => ({ replace: replaceRoute })
}));
enableAutoUnmount(afterEach);

const response = { challengeId: '01900000-0000-7000-8000-000000000001', expiresAtUtc: '2030-01-01T00:00:00Z' };
beforeEach(() => { forgetRegistrationInvitation(); routeState.query = { registrationWayId: '01900000-0000-7000-8000-000000000002' }; });

describe.each(['register', 'recover'] as const)('邮箱挑战页面：%s', mode => {
  const sender = mode === 'register' ? vi.mocked(requestEmailChallenge) : vi.mocked(recoverPassword);
  const createView = () => mount(mode === 'register' ? RegisterView : RecoverPasswordView, {
    global: { stubs: { ArtLoginLeftPanel: true } }
  });

  beforeEach(() => {
    sender.mockReset();
    sender.mockResolvedValue(response);
    vi.mocked(registerAccount).mockResolvedValue({ userId: response.challengeId });
    vi.mocked(confirmRecoverPassword).mockResolvedValue(undefined);
    vi.spyOn(ElMessage, 'success').mockReturnValue({ close: vi.fn() });
    vi.spyOn(ElMessage, 'error').mockReturnValue({ close: vi.fn() });
  });
  afterEach(() => vi.restoreAllMocks());

  it('发送中禁止重复请求及提交，成功后使用当前挑战提交', async () => {
    let finish!: (value: typeof response) => void;
    sender.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const wrapper = createView();
    await wrapper.get('input[type="email"]').setValue('current@example.test');
    const buttons = wrapper.findAll('button');
    await buttons[0]!.trigger('click');
    await buttons[0]!.trigger('click');
    expect(sender).toHaveBeenCalledTimes(1);
    expect(buttons[0]!.classes()).toContain('is-loading');
    expect(buttons[1]!.attributes('disabled')).toBeDefined();
    finish(response);
    await flushPromises();
    await wrapper.get('input[aria-label="邮箱验证码"]').setValue('123456');
    await wrapper.get('input[type="password"]').setValue('FullNet!2026Secure');
    await buttons[1]!.trigger('click');
    await flushPromises();
    const submitter = mode === 'register' ? registerAccount : confirmRecoverPassword;
    expect(submitter).toHaveBeenCalledWith(expect.objectContaining({ challengeId: response.challengeId, challengeCode: '123456' }));
    if (mode === 'register') expect(registerAccount).toHaveBeenCalledWith(expect.objectContaining({ registrationWayId: routeState.query.registrationWayId }));
  });

  it('邮箱变化后丢弃旧请求响应并清空旧验证码', async () => {
    let finish!: (value: typeof response) => void;
    sender.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const wrapper = createView();
    await wrapper.get('input[type="email"]').setValue('old@example.test');
    await wrapper.findAll('button')[0]!.trigger('click');
    await wrapper.get('input[type="email"]').setValue('new@example.test');
    finish(response);
    await flushPromises();
    await wrapper.get('input[aria-label="邮箱验证码"]').setValue('123456');
    expect(wrapper.findAll('button')[1]!.attributes('disabled')).toBeDefined();
    expect(ElMessage.success).not.toHaveBeenCalled();
    expect(wrapper.findAll('button')[0]!.classes()).not.toContain('is-loading');
  });

  it('重发失败清除旧挑战并显示错误，随后允许重新获取', async () => {
    const wrapper = createView();
    await wrapper.get('input[type="email"]').setValue('current@example.test');
    await wrapper.findAll('button')[0]!.trigger('click');
    await flushPromises();
    await wrapper.get('input[aria-label="邮箱验证码"]').setValue('123456');
    sender.mockRejectedValueOnce(new Error('network failure'));
    await wrapper.findAll('button')[0]!.trigger('click');
    await flushPromises();
    expect(ElMessage.error).toHaveBeenCalledWith('验证码发送未完成，请重新获取验证码后再试。');
    expect((wrapper.get('input[aria-label="邮箱验证码"]').element as HTMLInputElement).value).toBe('');
    expect(wrapper.findAll('button')[1]!.attributes('disabled')).toBeDefined();
    await wrapper.findAll('button')[0]!.trigger('click');
    await flushPromises();
    expect(sender).toHaveBeenCalledTimes(3);
  });

  it('中英文切换更新可访问名称和操作文案', async () => {
    const wrapper = createView();
    expect(wrapper.find('input[aria-label="邮箱"]').exists()).toBe(true);
    useAdminI18n().setLocale('en-US');
    await wrapper.vm.$nextTick();
    expect(wrapper.find('input[aria-label="Email"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Send code');
    expect(wrapper.text()).toContain(mode === 'register' ? 'Create account' : 'Recover password');
  });
});

it('邀请注册按已验证邀请发送挑战，保留邀请用途和令牌边界', async () => {
  routeState.query = {};
  rememberRegistrationInvitation({ invitationId: response.challengeId, invitationToken: 'test-invitation' });
  vi.mocked(verifyInvitation).mockResolvedValue({ email: 'invited@example.test', registrationWayId: response.challengeId } as Awaited<ReturnType<typeof verifyInvitation>>);
  vi.mocked(requestEmailChallenge).mockResolvedValue(response);
  const wrapper = mount(RegisterView, { global: { stubs: { ArtLoginLeftPanel: true } } });
  await flushPromises();
  await wrapper.findAll('button')[0]!.trigger('click');
  await flushPromises();
  expect(requestEmailChallenge).toHaveBeenCalledWith('invited@example.test', 3, response.challengeId, 'test-invitation');
});

it('显式注册入口不复用先前放弃的邀请', async () => {
  vi.mocked(verifyInvitation).mockClear();
  vi.mocked(requestEmailChallenge).mockResolvedValue(response);
  rememberRegistrationInvitation({ invitationId: response.challengeId, invitationToken: 'old-invitation' });
  const wrapper = mount(RegisterView, { global: { stubs: { ArtLoginLeftPanel: true } } });
  await flushPromises();
  await wrapper.get('input[type="email"]').setValue('new@example.test');
  await wrapper.findAll('button')[0]!.trigger('click'); await flushPromises();
  expect(verifyInvitation).not.toHaveBeenCalled();
  expect(requestEmailChallenge).toHaveBeenLastCalledWith('new@example.test', 1, undefined, undefined);
});

it('标签页存储不可用时仍剥离邀请 URL 并使用当前页面已验证凭据', async () => {
  routeState.query = { invitationId: response.challengeId, invitationToken: 'private-token' };
  vi.mocked(verifyInvitation).mockResolvedValue({ email: 'invited@example.test', registrationWayId: response.challengeId } as Awaited<ReturnType<typeof verifyInvitation>>);
  vi.mocked(requestEmailChallenge).mockResolvedValue(response);
  sessionStorage.setItem('fullnet.registration.invitation', JSON.stringify({ invitationId: response.challengeId, invitationToken: 'stale-token' }));
  const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage denied'); });
  const removal = vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new DOMException('Storage denied'); });
  try {
    const wrapper = mount(RegisterView, { global: { stubs: { ArtLoginLeftPanel: true } } });
    await flushPromises();
    expect(replaceRoute).toHaveBeenCalledWith({ path: '/register' });
    await wrapper.findAll('button')[0]!.trigger('click');
    await flushPromises();
    expect(requestEmailChallenge).toHaveBeenLastCalledWith('invited@example.test', 3, response.challengeId, 'private-token');
    expect(readRegistrationInvitation()?.invitationToken).toBe('private-token');
    forgetRegistrationInvitation();
    expect(readRegistrationInvitation()).toBeUndefined();
  } finally { storage.mockRestore(); removal.mockRestore(); forgetRegistrationInvitation(); }
});

it.each([undefined, 'not-a-guid', '00000000-0000-0000-0000-000000000000', ['a', 'b']])('注册入口缺失或非法时禁止发送验证码：%s', async value => {
  vi.mocked(requestEmailChallenge).mockClear();
  routeState.query = value === undefined ? {} : { registrationWayId: value };
  const wrapper = mount(RegisterView, { global: { stubs: { ArtLoginLeftPanel: true } } });
  await flushPromises();
  await wrapper.get('input[type="email"]').setValue('member@example.test');
  expect(wrapper.text()).toContain('请使用企业提供的有效注册链接');
  expect(wrapper.findAll('button')[0]!.attributes('disabled')).toBeDefined();
  await wrapper.findAll('button')[0]!.trigger('click');
  expect(requestEmailChallenge).not.toHaveBeenCalled();
});

it.each(['invitationId', 'invitationToken'])('不完整邀请 %s 不会回落普通注册入口', async field => {
  vi.mocked(requestEmailChallenge).mockClear();
  vi.mocked(verifyInvitation).mockClear();
  routeState.query[field] = field === 'invitationId' ? response.challengeId : 'test-invitation';
  const wrapper = mount(RegisterView, { global: { stubs: { ArtLoginLeftPanel: true } } });
  await flushPromises();
  await wrapper.get('input[type="email"]').setValue('member@example.test');
  expect(wrapper.text()).toContain('邀请无效或已过期');
  expect(wrapper.findAll('button')[0]!.attributes('disabled')).toBeDefined();
  expect(verifyInvitation).not.toHaveBeenCalled();
  expect(requestEmailChallenge).not.toHaveBeenCalled();
  expect(replaceRoute).toHaveBeenCalledWith({ path: '/register' });
});
