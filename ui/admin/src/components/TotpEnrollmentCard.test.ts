import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, reactive, ref } from 'vue';
import TotpEnrollmentCard from './TotpEnrollmentCard.vue';
import { beginTotpEnrollment, confirmTotpEnrollment, getTotpEnrollmentStatus } from '../api/totpEnrollment';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';

let session: { currentUser: { id: string; sessionId: string } };
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('../api/totpEnrollment', () => ({ beginTotpEnrollment: vi.fn(), confirmTotpEnrollment: vi.fn(), getTotpEnrollmentStatus: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const secret = 'JBSWY3DPEHPK3PXP';
const material = { sharedSecretBase32: secret, otpAuthUri: `otpauth://totp/Full.NET:user?secret=${secret}` };
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; }
function button(wrapper: ReturnType<typeof mount>, text: string) { return wrapper.findAll('button').find(b => b.text() === text)!; }
async function begin(wrapper: ReturnType<typeof mount>) { await flushPromises(); await button(wrapper, 'Begin enrollment').trigger('click'); await flushPromises(); }

describe('TOTP self-service enrollment', () => {
  beforeEach(() => {
    vi.resetAllMocks(); useAdminI18n().setLocale('en-US');
    session = reactive({ currentUser: { id: 'user-a', sessionId: 'session-a' } });
    vi.mocked(getTotpEnrollmentStatus).mockResolvedValue({ isEnrolled: false, isEnabled: false });
    vi.mocked(beginTotpEnrollment).mockResolvedValue(material);
    vi.mocked(confirmTotpEnrollment).mockResolvedValue({ isEnrolled: true, isEnabled: true });
  });
  it('loads status, confirms once and clears secret after success', async () => {
    const wrapper = mount(TotpEnrollmentCard); await begin(wrapper);
    expect(wrapper.get('input[readonly]').element).toHaveProperty('value', secret);
    await wrapper.get('input[inputmode="numeric"]').setValue('123456');
    const pending = deferred<{ isEnrolled: boolean; isEnabled: boolean }>();
    vi.mocked(confirmTotpEnrollment).mockReturnValue(pending.promise);
    await wrapper.get('form').trigger('submit'); await wrapper.get('form').trigger('submit');
    expect(confirmTotpEnrollment).toHaveBeenCalledTimes(1);
    pending.resolve({ isEnrolled: true, isEnabled: true }); await flushPromises();
    expect(wrapper.find('input[readonly]').exists()).toBe(false);
    expect(wrapper.text()).toContain('TOTP enabled'); expect(showSuccess).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('refuses begin for enabled accounts and retries an unavailable status', async () => {
    vi.mocked(getTotpEnrollmentStatus).mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce({ isEnrolled: true, isEnabled: true });
    const wrapper = mount(TotpEnrollmentCard); await flushPromises();
    expect(button(wrapper, 'Begin enrollment')).toBeUndefined(); expect(showProblem).toHaveBeenCalledTimes(1);
    await button(wrapper, 'Refresh status').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('TOTP enabled'); expect(button(wrapper, 'Begin enrollment')).toBeUndefined(); wrapper.unmount();
  });
  it('keeps material for a rejected code and validates six digits before sending', async () => {
    const wrapper = mount(TotpEnrollmentCard); await begin(wrapper);
    await wrapper.get('input[inputmode="numeric"]').setValue('123'); await wrapper.get('form').trigger('submit');
    expect(confirmTotpEnrollment).not.toHaveBeenCalled(); expect(showWarning).toHaveBeenCalledTimes(1);
    vi.mocked(confirmTotpEnrollment).mockRejectedValue(new Error('invalid code'));
    await wrapper.get('input[inputmode="numeric"]').setValue('123456'); await wrapper.get('form').trigger('submit'); await flushPromises();
    expect(wrapper.get('input[readonly]').element).toHaveProperty('value', secret); expect(showProblem).toHaveBeenCalledTimes(1);
    await button(wrapper, 'Hide enrollment material').trigger('click'); expect(wrapper.find('input[readonly]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['session', 'account', 'unmount'])('ignores a late secret after %s changes', async change => {
    const pending = deferred<typeof material>(); vi.mocked(beginTotpEnrollment).mockReturnValue(pending.promise);
    const wrapper = mount(TotpEnrollmentCard); await flushPromises();
    await button(wrapper, 'Begin enrollment').trigger('click');
    const signal = vi.mocked(beginTotpEnrollment).mock.calls[0]![0]!;
    if (change === 'unmount') wrapper.unmount();
    else if (change === 'session') session.currentUser.sessionId = 'session-b';
    else session.currentUser.id = 'user-b';
    await flushPromises(); expect(signal.aborted).toBe(true);
    pending.resolve(material); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled();
    if (change !== 'unmount') { expect(wrapper.find('input[readonly]').exists()).toBe(false); wrapper.unmount(); }
  });
  it('clears material when cached and refreshes after activation', async () => {
    const active = ref(true); const other = defineComponent({ render: () => h('div', 'Other') });
    const host = defineComponent({ render: () => h(KeepAlive, null, { default: () => h(active.value ? TotpEnrollmentCard : other) }) });
    const wrapper = mount(host); await begin(wrapper);
    active.value = false; await flushPromises(); active.value = true; await flushPromises();
    expect(wrapper.find('input[readonly]').exists()).toBe(false); expect(getTotpEnrollmentStatus).toHaveBeenCalledTimes(2); wrapper.unmount();
  });
  it('clears stale material when explicit refresh observes enabled status', async () => {
    const wrapper = mount(TotpEnrollmentCard); await begin(wrapper);
    vi.mocked(getTotpEnrollmentStatus).mockResolvedValue({ isEnrolled: true, isEnabled: true });
    await button(wrapper, 'Refresh status').trigger('click'); await flushPromises();
    expect(wrapper.find('input[readonly]').exists()).toBe(false); wrapper.unmount();
  });
  it('refreshes authority and clears secret after a confirmation conflict', async () => {
    const wrapper = mount(TotpEnrollmentCard); await begin(wrapper);
    vi.mocked(confirmTotpEnrollment).mockRejectedValue({ status: 409 });
    vi.mocked(getTotpEnrollmentStatus).mockResolvedValue({ isEnrolled: true, isEnabled: true });
    await wrapper.get('input[inputmode="numeric"]').setValue('123456'); await wrapper.get('form').trigger('submit'); await flushPromises();
    expect(wrapper.find('input[readonly]').exists()).toBe(false); expect(wrapper.text()).toContain('TOTP enabled'); wrapper.unmount();
  });
});
