import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import PersonalShortcuts from './PersonalShortcuts.vue';
const wrappers: VueWrapper[] = [];
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
async function render() {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/orders', name: 'orders', component: { template: '<div />' } }, { path: '/other', name: 'other', component: { template: '<div />' } }] });
  await router.push('/orders'); await router.isReady();
  const wrapper = mount(PersonalShortcuts, { props: { scope: { userId: 'one', tenantId: 'a' }, switching: false,
    navigation: [{ routeName: 'orders', title: 'Orders' }, { routeName: 'other', title: 'Other' }] }, global: { plugins: [router] } }); wrappers.push(wrapper);
  return { wrapper, router };
}
describe('个人常用入口', () => {
  it('当前白名单路由可收藏、恢复和按名称跳转', async () => {
    const { wrapper, router } = await render(); await wrapper.get('[data-testid="shortcut-toggle-current"]').trigger('click');
    await router.push('/other'); await wrapper.get('[data-shortcut-route="orders"]').trigger('click'); await flushPromises(); expect(router.currentRoute.value.name).toBe('orders');
    expect(wrapper.text()).toContain('取消当前收藏');
  });
  it('撤权立即隐藏目标；切换上下文不显示旧账号或租户的收藏', async () => {
    const { wrapper } = await render(); await wrapper.get('[data-testid="shortcut-toggle-current"]').trigger('click');
    await wrapper.setProps({ navigation: [] }); expect(wrapper.find('[data-shortcut-route]').exists()).toBe(false);
    await wrapper.setProps({ navigation: [{ routeName: 'orders', title: 'Orders' }], scope: { userId: 'one', tenantId: 'b' } });
    expect(wrapper.find('[data-shortcut-route]').exists()).toBe(false);
    await wrapper.setProps({ scope: { userId: 'one', tenantId: 'a' }, switching: true }); expect(wrapper.find('[data-shortcut-route]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="shortcut-toggle-current"]').attributes('disabled')).toBeDefined();
  });
  it('存储不可用时明确提示，不虚构持久化成功', async () => {
    const { wrapper } = await render(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    await wrapper.get('[data-testid="shortcut-toggle-current"]').trigger('click'); expect(wrapper.get('[role="alert"]').text()).toContain('无法保存');
    expect(wrapper.find('[data-shortcut-route]').exists()).toBe(false);
  });
});
