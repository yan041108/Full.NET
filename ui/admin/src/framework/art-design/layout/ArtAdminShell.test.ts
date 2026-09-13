import { afterEach, expect, it, vi } from 'vitest';
import { shallowMount } from '@vue/test-utils';
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
