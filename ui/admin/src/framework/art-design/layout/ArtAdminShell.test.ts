import { afterEach, expect, it, vi } from 'vitest';
import { flushPromises, shallowMount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import ArtAdminShell from './ArtAdminShell.vue';

afterEach(() => vi.restoreAllMocks());

it('卸载壳组件时从原 MediaQueryList 移除监听器', async () => {
  const queries: MediaQueryList[] = [];
  vi.spyOn(window, 'matchMedia').mockImplementation(media => {
    const query = {
      media, matches: false, onchange: null,
      addListener: vi.fn(), removeListener: vi.fn(), dispatchEvent: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn()
    } as unknown as MediaQueryList;
    queries.push(query);
    return query;
  });
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] });
  await router.push('/');
  const wrapper = shallowMount(ArtAdminShell, {
    props: {
      navigationTree: [], translate: key => key, selectedContext: 'host', hostContextValue: 'host',
      canReadTenants: false, canSwitchTenant: false, switching: false, displayName: 'Test', roleLabel: 'Test',
      currentContextName: 'Host', availableTenants: [], notificationUnreadCount: 0,
      labels: {} as InstanceType<typeof ArtAdminShell>['$props']['labels']
    },
    global: { plugins: [router] }
  });
  wrapper.unmount();
  const subscribed = queries.filter(query => vi.mocked(query.addEventListener).mock.calls.length > 0);
  expect(subscribed.length).toBeGreaterThan(0);
  for (const query of subscribed) {
    for (const call of vi.mocked(query.addEventListener).mock.calls) {
      expect(query.removeEventListener).toHaveBeenCalledWith(...call);
    }
  }
});

it('首次打开才建立设置面板，关闭再打开保留实例', async () => {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] });
  await router.push('/');
  const wrapper = shallowMount(ArtAdminShell, {
    props: {
      navigationTree: [], translate: key => key, selectedContext: 'host', hostContextValue: 'host',
      canReadTenants: false, canSwitchTenant: false, switching: false, displayName: 'Test', roleLabel: 'Test',
      currentContextName: 'Host', availableTenants: [], notificationUnreadCount: 0,
      labels: {} as InstanceType<typeof ArtAdminShell>['$props']['labels']
    },
    global: { plugins: [router], stubs: {
      ArtSettingsPanel: { name: 'ArtSettingsPanel', props: ['open'], template: '<section data-testid="settings-probe" />' }
    } }
  });
  expect(wrapper.find('[data-testid="settings-probe"]').exists()).toBe(false);
  wrapper.findComponent({ name: 'ArtTopBar' }).vm.$emit('open-settings'); await flushPromises();
  const panel = wrapper.findComponent({ name: 'ArtSettingsPanel' }); expect(panel.props('open')).toBe(true);
  panel.vm.$emit('update:open', false); await flushPromises();
  expect(wrapper.findComponent({ name: 'ArtSettingsPanel' }).vm).toBe(panel.vm);
  expect(panel.props('open')).toBe(false);
  wrapper.findComponent({ name: 'ArtTopBar' }).vm.$emit('open-settings'); await flushPromises();
  expect(wrapper.findComponent({ name: 'ArtSettingsPanel' }).vm).toBe(panel.vm); expect(panel.props('open')).toBe(true);
  wrapper.unmount();
});
