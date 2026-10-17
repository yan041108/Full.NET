import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, reactive, ref } from 'vue';
import SecuritySettingsView from './SecuritySettingsView.vue';
import { regenerateMyMfaRecoveryCodes } from '../api/mfaRecoveryCodes';
import { showSuccess, showProblem } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import { listOAuthUserLinks, deleteOAuthUserLink } from '../api/oauth-links';
import { listPublicOAuthProviders } from '../api/oauth-providers';
import { getTotpEnrollmentStatus } from '../api/totpEnrollment';
vi.mock('../api/totpEnrollment', () => ({
  getTotpEnrollmentStatus: vi.fn().mockResolvedValue({ isEnrolled: false, isEnabled: false }),
  beginTotpEnrollment: vi.fn(), confirmTotpEnrollment: vi.fn()
}));
const router = vi.hoisted(() => ({ replace: vi.fn() }));
let session: { currentUser: { id: string; sessionId: string; passwordChangeRequired: boolean; scope: string; actorScope: string }; changePassword: ReturnType<typeof vi.fn> };
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => router }));
vi.mock('../api/mfaRecoveryCodes', () => ({ regenerateMyMfaRecoveryCodes: vi.fn() }));
vi.mock('../api/oauth-links', () => ({ listOAuthUserLinks: vi.fn().mockResolvedValue([]), deleteOAuthUserLink: vi.fn(), buildOAuthAuthorizeUrl: vi.fn() }));
vi.mock('../api/oauth-providers', () => ({ listPublicOAuthProviders: vi.fn().mockResolvedValue([]) }));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn(), showProblem: vi.fn(), showWarning: vi.fn() }));
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason: unknown) => void; const promise = new Promise<T>((r,e)=>{resolve=r;reject=e;});return {promise,resolve,reject}; }
function regenerateButton(wrapper: ReturnType<typeof mount>) { return wrapper.findAll('button').find(b=>b.text().includes('Regenerate recovery codes'))!; }
describe('安全设置操作边界', () => {
  beforeEach(() => { vi.resetAllMocks(); vi.mocked(getTotpEnrollmentStatus).mockResolvedValue({ isEnrolled: false, isEnabled: false }); vi.mocked(listOAuthUserLinks).mockResolvedValue([]); vi.mocked(listPublicOAuthProviders).mockResolvedValue([]); useAdminI18n().setLocale('en-US'); session=reactive({currentUser:{id:'user-a',sessionId:'session-a',passwordChangeRequired:false,scope:'host',actorScope:'host'},changePassword:vi.fn()}); });
  it('offers TOTP enrollment alongside password and recovery settings', async () => {
    const wrapper = mount(SecuritySettingsView); await flushPromises(); await flushPromises();
    expect(wrapper.text()).toContain('Authenticator app');
    wrapper.unmount();
  });
  it.each(['tenant', 'forced'])('does not expose TOTP enrollment in %s context', async context => {
    if (context === 'tenant') session.currentUser.scope = 'tenant:fixture';
    else session.currentUser.passwordChangeRequired = true;
    const wrapper = mount(SecuritySettingsView); await flushPromises();
    expect(wrapper.text()).not.toContain('Authenticator app'); expect(getTotpEnrollmentStatus).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('reports unbind failure and allows another attempt', async () => {
    vi.mocked(listOAuthUserLinks).mockResolvedValue([{ id: 'old-link', providerKey: 'old', providerDisplayName: 'Provider', subject: 'subject', email: null, emailVerified: false, displayName: null, linkedAtUtc: '2026-10-07T00:00:00Z', lastUsedAtUtc: null }]);
    vi.mocked(deleteOAuthUserLink).mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce();
    const wrapper = mount(SecuritySettingsView); await flushPromises();
    const unbind = () => wrapper.findAll('button').find(b => b.text() === 'Unbind')!;
    await unbind().trigger('click'); await flushPromises(); expect(showProblem).toHaveBeenCalledTimes(1); expect(showSuccess).not.toHaveBeenCalled();
    await unbind().trigger('click'); await flushPromises(); expect(deleteOAuthUserLink).toHaveBeenCalledTimes(2); expect(showSuccess).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('reloads OAuth links on session change and ignores the older response', async () => {
    const pending = deferred<Awaited<ReturnType<typeof listOAuthUserLinks>>>();
    vi.mocked(listOAuthUserLinks).mockReturnValueOnce(pending.promise).mockResolvedValueOnce([]);
    const wrapper = mount(SecuritySettingsView); await flushPromises();
    session.currentUser.sessionId = 'session-b'; await flushPromises();
    expect(listOAuthUserLinks).toHaveBeenCalledTimes(2);
    pending.resolve([{ id: 'old-link', providerKey: 'old', providerDisplayName: 'PRIVATE-OLD-PROVIDER', subject: 'old-subject', email: null, emailVerified: false, displayName: null, linkedAtUtc: '2026-10-07T00:00:00Z', lastUsedAtUtc: null }]);
    await flushPromises(); expect(wrapper.text()).not.toContain('PRIVATE-OLD-PROVIDER'); wrapper.unmount();
  });
  it('unbinds once and suppresses success feedback after the initiating session changes', async () => {
    vi.mocked(listOAuthUserLinks).mockResolvedValue([{ id: 'old-link', providerKey: 'old', providerDisplayName: 'Provider', subject: 'subject', email: null, emailVerified: false, displayName: null, linkedAtUtc: '2026-10-07T00:00:00Z', lastUsedAtUtc: null }]);
    const pending = deferred<void>(); vi.mocked(deleteOAuthUserLink).mockReturnValue(pending.promise);
    const wrapper = mount(SecuritySettingsView); await flushPromises(); await flushPromises();
    const button = wrapper.findAll('button').find(b => b.text() === 'Unbind')!.element as HTMLButtonElement;
    button.click(); button.click(); expect(deleteOAuthUserLink).toHaveBeenCalledTimes(1);
    session.currentUser.sessionId = 'session-b'; await flushPromises();
    pending.resolve(); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('submits a password change once while pending', async () => {
    const pending=deferred<boolean>();session.changePassword.mockReturnValue(pending.promise);const wrapper=mount(SecuritySettingsView);await flushPromises();
    const inputs=wrapper.findAll('input[type="password"]');for(const [i,value] of ['Current!Password123','Changed!Password123','Changed!Password123'].entries())await inputs[i]!.setValue(value);
    const form=wrapper.get('form');await form.trigger('submit');await form.trigger('submit');expect(session.changePassword).toHaveBeenCalledTimes(1);
    pending.resolve(true);await flushPromises();wrapper.unmount();
  });
  it('regenerates recovery codes once while pending', async () => {
    const pending=deferred<{recoveryCodes:string[]}>();vi.mocked(regenerateMyMfaRecoveryCodes).mockReturnValue(pending.promise);const wrapper=mount(SecuritySettingsView);await flushPromises();
    const button=regenerateButton(wrapper).element as HTMLButtonElement;button.click();button.click();expect(regenerateMyMfaRecoveryCodes).toHaveBeenCalledTimes(1);
    pending.resolve({recoveryCodes:['recovery-fixture-code']});await flushPromises();wrapper.unmount();
  });
  it.each(['unmount','session-change'])('ignores late recovery code results after %s', async action => {
    const pending=deferred<{recoveryCodes:string[]}>();vi.mocked(regenerateMyMfaRecoveryCodes).mockReturnValue(pending.promise);const wrapper=mount(SecuritySettingsView);await flushPromises();await regenerateButton(wrapper).trigger('click');
    if(action==='unmount')wrapper.unmount();else session.currentUser.sessionId='session-b';await flushPromises();
    pending.resolve({recoveryCodes:['private-recovery-fixture']});await flushPromises();expect(showSuccess).not.toHaveBeenCalled();if(action==='session-change'){expect(wrapper.text()).not.toContain('private-recovery-fixture');wrapper.unmount();}
  });
  it('confirms password change after its own session rotation', async () => {
    session.currentUser.passwordChangeRequired = true;
    session.changePassword.mockImplementation(async () => { session.currentUser = { ...session.currentUser, sessionId: 'rotated-session', passwordChangeRequired: false }; return true; });
    const wrapper = mount(SecuritySettingsView); await flushPromises();
    for (const [index, value] of ['Current!Password123', 'Changed!Password123', 'Changed!Password123'].entries()) await wrapper.findAll('input[type="password"]')[index]!.setValue(value);
    await wrapper.get('form').trigger('submit'); await flushPromises();
    expect(showSuccess).toHaveBeenCalledWith('Password updated. Other sessions were revoked.');
    expect(router.replace).toHaveBeenCalledWith('/');
    wrapper.unmount();
  });
  it('does not hide recovery codes when the same user and session snapshot is replaced', async () => {
    vi.mocked(regenerateMyMfaRecoveryCodes).mockResolvedValue({ recoveryCodes: ['private-recovery-fixture'] });
    const wrapper = mount(SecuritySettingsView); await flushPromises(); await regenerateButton(wrapper).trigger('click'); await flushPromises();
    session.currentUser = { ...session.currentUser }; await flushPromises();
    expect(wrapper.text()).toContain('private-recovery-fixture'); wrapper.unmount();
  });
  it('clears cached codes and ignores a pending result while a KeepAlive page is inactive', async () => {
    const pending = deferred<{ recoveryCodes: string[] }>();
    vi.mocked(regenerateMyMfaRecoveryCodes).mockResolvedValueOnce({ recoveryCodes: ['private-first-fixture'] }).mockReturnValueOnce(pending.promise).mockResolvedValueOnce({ recoveryCodes: ['private-new-fixture'] });
    const active = ref(true);
    const other = defineComponent({ render: () => h('div', 'Other page') });
    const host = defineComponent({ render: () => h(KeepAlive, null, { default: () => h(active.value ? SecuritySettingsView : other) }) });
    const wrapper = mount(host); await flushPromises();
    await regenerateButton(wrapper).trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('private-first-fixture');
    active.value = false; await flushPromises(); active.value = true; await flushPromises(); expect(wrapper.text()).not.toContain('private-first-fixture');
    await regenerateButton(wrapper).trigger('click'); active.value = false; await flushPromises(); vi.mocked(showSuccess).mockClear();
    pending.resolve({ recoveryCodes: ['private-late-fixture'] }); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled();
    active.value = true; await flushPromises(); expect(wrapper.text()).not.toContain('private-late-fixture');
    await regenerateButton(wrapper).trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('private-new-fixture'); wrapper.unmount();
  });
  it('clears displayed codes on session change and allows explicit dismissal', async () => {
    vi.mocked(regenerateMyMfaRecoveryCodes).mockResolvedValue({recoveryCodes:['private-recovery-fixture']});const wrapper=mount(SecuritySettingsView);await flushPromises();await regenerateButton(wrapper).trigger('click');await flushPromises();
    expect(wrapper.text()).toContain('private-recovery-fixture');const dismiss=wrapper.findAll('button').find(b=>b.text().includes('Hide recovery codes'));expect(dismiss).toBeDefined();await dismiss!.trigger('click');expect(wrapper.text()).not.toContain('private-recovery-fixture');
    await regenerateButton(wrapper).trigger('click');await flushPromises();session.currentUser.sessionId='session-b';await flushPromises();expect(wrapper.text()).not.toContain('private-recovery-fixture');wrapper.unmount();
  });
});
