import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { showError, showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import RecoverPasswordView from './RecoverPasswordView.vue';
import RegisterView from './RegisterView.vue';
import { confirmRecoverPassword, recoverPassword, requestEmailChallenge, registerAccount, verifyInvitation } from '../api/public-auth';

vi.mock('../api/public-auth', () => ({ confirmRecoverPassword: vi.fn(), recoverPassword: vi.fn(), requestEmailChallenge: vi.fn(), registerAccount: vi.fn(), verifyInvitation: vi.fn() }));
vi.mock('vue-router', () => ({ useRouter: () => ({ replace: vi.fn() }), useRoute: () => ({ query: {}, path: '/register' }) }));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn(), showError: vi.fn(), showWarning: vi.fn(), showProblem: vi.fn() }));
const response = { challengeId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60', expiresAtUtc: '2099-01-01T00:00:00Z' };
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason: unknown) => void; const promise = new Promise<T>((r, e) => { resolve = r; reject = e; }); return { resolve, reject, promise }; }
function page(component: typeof RecoverPasswordView | typeof RegisterView) { return mount(component, { global: { plugins: [createPinia()], stubs: { ArtLoginLeftPanel: true } } }); }
async function click(wrapper: VueWrapper, text: string) { const button = wrapper.findAll('button').find(item => item.text().includes(text)); expect(button).toBeDefined(); await button!.trigger('click'); }

describe('账号验证码页面请求状态', () => {
  beforeEach(() => { vi.resetAllMocks(); sessionStorage.clear(); useAdminI18n().setLocale('en-US'); });
  it('shows an invitation verification failure through the shared feedback', async () => {
    const error = new Error('invitation-provider-detail');
    sessionStorage.setItem('fullnet.registration.invitation', JSON.stringify({ invitationId: response.challengeId, invitationToken: 'test-token' }));
    vi.mocked(verifyInvitation).mockRejectedValueOnce(error);
    const wrapper = page(RegisterView); await flushPromises(); wrapper.unmount();
    expect(showProblem).toHaveBeenCalledWith(error, 'Invitation is invalid or expired.');
  });
  it.each([['recovery', RecoverPasswordView], ['registration', RegisterView]] as const)('%s sends once while a request is pending', async (kind, component) => {
    const pending = deferred<typeof response>();
    const send = kind === 'recovery' ? vi.mocked(recoverPassword) : vi.mocked(requestEmailChallenge);
    send.mockReturnValue(pending.promise);
    const wrapper = page(component); await wrapper.get('input[type="email"]').setValue('user@example.test');
    await click(wrapper, 'Send code'); await click(wrapper, 'Send code');
    const calls = send.mock.calls.length; pending.resolve(response); await flushPromises(); wrapper.unmount(); expect(calls).toBe(1);
  });
  it('empty recovery fields never call the confirmation API', async () => {
    vi.mocked(confirmRecoverPassword).mockResolvedValueOnce(undefined);
    const wrapper = page(RecoverPasswordView); await wrapper.get('form').trigger('submit'); await flushPromises();
    expect(confirmRecoverPassword).not.toHaveBeenCalled(); expect(showWarning).toHaveBeenCalled(); wrapper.unmount();
  });
  it('an old email response cannot authorize the edited target', async () => {
    const pending = deferred<typeof response>(); vi.mocked(recoverPassword).mockReturnValueOnce(pending.promise);
    const wrapper = page(RecoverPasswordView); await wrapper.get('input[type="email"]').setValue('first@example.test');
    await click(wrapper, 'Send code'); await wrapper.get('input[type="email"]').setValue('second@example.test');
    pending.resolve(response); await flushPromises();
    await wrapper.findAll('input').find(item => item.attributes('placeholder') === 'Recovery code')!.setValue('123456');
    await wrapper.get('input[type="password"]').setValue('Valid!Password123');
    await click(wrapper, 'Update password'); await flushPromises(); expect(confirmRecoverPassword).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('registration invalidates verification after the target email changes', async () => {
    vi.mocked(requestEmailChallenge).mockResolvedValueOnce(response); vi.mocked(registerAccount).mockResolvedValueOnce({ userId: response.challengeId });
    const wrapper = page(RegisterView); await wrapper.get('input[type="email"]').setValue('first@example.test');
    await wrapper.get('input[type="password"]').setValue('Valid!Password123');
    await wrapper.findAll('input').find(item => item.attributes('placeholder') === 'Display name')!.setValue('Example');
    await click(wrapper, 'Send code'); await flushPromises();
    await wrapper.findAll('input').find(item => item.attributes('placeholder') === 'Email verification code')!.setValue('123456');
    await wrapper.get('input[type="email"]').setValue('second@example.test');
    await click(wrapper, 'Register'); await flushPromises(); expect(registerAccount).not.toHaveBeenCalled(); wrapper.unmount();
  });

  it.each([['recovery', RecoverPasswordView], ['registration', RegisterView]] as const)('%s reports a safe send failure and permits a retry', async (kind, component) => {
    const send = kind === 'recovery' ? vi.mocked(recoverPassword) : vi.mocked(requestEmailChallenge);
    send.mockRejectedValueOnce(new Error('sensitive-provider-detail')).mockResolvedValueOnce(response);
    const wrapper = page(component); await wrapper.get('input[type="email"]').setValue('user@example.test');
    await click(wrapper, 'Send code'); await flushPromises();
    expect(showProblem).toHaveBeenCalledWith(expect.any(Error), 'The verification request failed. Try again later.');
    expect(showError).not.toHaveBeenCalled();
    await click(wrapper, 'Send code'); await flushPromises(); expect(send).toHaveBeenCalledTimes(2); wrapper.unmount();
  });
  it.each([['recovery', RecoverPasswordView], ['registration', RegisterView]] as const)('%s ignores a delivery response after unmount', async (kind, component) => {
    const pending = deferred<typeof response>(); const send = kind === 'recovery' ? vi.mocked(recoverPassword) : vi.mocked(requestEmailChallenge);
    send.mockReturnValueOnce(pending.promise); const wrapper = page(component);
    await wrapper.get('input[type="email"]').setValue('user@example.test'); await click(wrapper, 'Send code'); wrapper.unmount();
    pending.resolve(response); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled(); expect(showProblem).not.toHaveBeenCalled();
  });
  it('uses bilingual labels and input autocomplete without changing machine values', async () => {
    useAdminI18n().setLocale('zh-CN'); const wrapper = page(RecoverPasswordView);
    expect(wrapper.get('h1').text()).toBe('恢复密码'); expect(wrapper.get('input[name="email"]').attributes('aria-label')).toBe('邮箱');
    expect(wrapper.get('input[name="challengeCode"]').attributes('autocomplete')).toBe('one-time-code');
    await wrapper.get('input[name="email"]').setValue('user@example.test'); useAdminI18n().setLocale('en-US'); await flushPromises();
    expect(wrapper.get('h1').text()).toBe('Recover password'); expect((wrapper.get('input[name="email"]').element as HTMLInputElement).value).toBe('user@example.test'); wrapper.unmount();
  });
  it.each([
    ['recovery', RecoverPasswordView, 'success'], ['recovery', RecoverPasswordView, 'failure'],
    ['registration', RegisterView, 'success'], ['registration', RegisterView, 'failure']
  ] as const)('%s ignores an old response after email changes back', async (kind, component, outcome) => {
    const pending = deferred<typeof response>();
    const send = kind === 'recovery' ? vi.mocked(recoverPassword) : vi.mocked(requestEmailChallenge);
    send.mockReturnValueOnce(pending.promise);
    const wrapper = page(component); const emailInput = wrapper.get('input[type="email"]');
    await emailInput.setValue('first@example.test'); await click(wrapper, 'Send code');
    // 邮箱改回原值仍属于新一轮输入，不能只比较最终字符串。
    await emailInput.setValue('second@example.test');
    await emailInput.setValue('first@example.test');
    if (outcome === 'success') pending.resolve(response); else pending.reject(new Error('old-request'));
    await flushPromises();
    const successCalls = vi.mocked(showSuccess).mock.calls.length;
    const errorCalls = vi.mocked(showProblem).mock.calls.length;
    await wrapper.get('input[name="challengeCode"]').setValue('123456');
    await wrapper.get('input[type="password"]').setValue('Valid!Password123');
    if (kind === 'registration') await wrapper.get('input[name="displayName"]').setValue('Example');
    await click(wrapper, kind === 'recovery' ? 'Update password' : 'Register'); await flushPromises();
    wrapper.unmount();
    expect(successCalls).toBe(0); expect(errorCalls).toBe(0);
    expect(kind === 'recovery' ? confirmRecoverPassword : registerAccount).not.toHaveBeenCalled();
  });
});


describe('账号自助表单与邀请边界', () => {
  beforeEach(() => { vi.resetAllMocks(); sessionStorage.clear(); useAdminI18n().setLocale('en-US'); });
  it.each([null, [], { invitationId: response.challengeId, invitationToken: 123 }])('clears malformed cached invitation %j', async value => {
    sessionStorage.setItem('fullnet.registration.invitation', JSON.stringify(value));
    const wrapper = page(RegisterView); await flushPromises(); wrapper.unmount();
    expect(verifyInvitation).not.toHaveBeenCalled();
    expect(sessionStorage.getItem('fullnet.registration.invitation')).toBeNull();
  });
  it('blocks challenge requests while invitation verification is pending', async () => {
    sessionStorage.setItem('fullnet.registration.invitation', JSON.stringify({ invitationId: response.challengeId, invitationToken: 'test-token' }));
    const pending = deferred<Awaited<ReturnType<typeof verifyInvitation>>>(); vi.mocked(verifyInvitation).mockReturnValue(pending.promise);
    const wrapper = page(RegisterView); await flushPromises();
    await wrapper.get('input[name="email"]').setValue('changed@example.test');
    await click(wrapper, 'Send code');
    expect(requestEmailChallenge).not.toHaveBeenCalled();
    pending.resolve({ invitationId: response.challengeId, tenantId: response.challengeId, registrationWayId: response.challengeId, email: 'invite@example.test', expiresAtUtc: response.expiresAtUtc });
    await flushPromises();
    expect(wrapper.get('input[name="email"]').attributes('readonly')).toBeDefined(); wrapper.unmount();
  });
  it('blocks submissions after rejected invitation verification', async () => {
    sessionStorage.setItem('fullnet.registration.invitation', JSON.stringify({ invitationId: response.challengeId, invitationToken: 'test-token' }));
    vi.mocked(verifyInvitation).mockRejectedValue(new Error('expired'));
    const wrapper = page(RegisterView); await flushPromises();
    await wrapper.get('input[name="email"]').setValue('invite@example.test');
    await click(wrapper, 'Send code');
    expect(requestEmailChallenge).not.toHaveBeenCalled(); expect(sessionStorage.getItem('fullnet.registration.invitation')).toBeNull(); wrapper.unmount();
  });
  it.each([['registration', RegisterView], ['recovery', RecoverPasswordView]] as const)('%s submits through a native form and rejects a weak password', async (kind, component) => {
    vi.mocked(requestEmailChallenge).mockResolvedValue(response); vi.mocked(recoverPassword).mockResolvedValue(response);
    const wrapper = page(component);
    await wrapper.get('input[name="email"]').setValue('user@example.test'); await click(wrapper, 'Send code'); await flushPromises();
    await wrapper.get('input[name="challengeCode"]').setValue('123456'); await wrapper.get('input[type="password"]').setValue('weak');
    if (kind === 'registration') await wrapper.get('input[name="displayName"]').setValue('User');
    const form = wrapper.find('form'); expect(form.exists()).toBe(true); await form.trigger('submit'); await flushPromises();
    expect(kind === 'registration' ? registerAccount : confirmRecoverPassword).not.toHaveBeenCalled(); wrapper.unmount();
  });
});
