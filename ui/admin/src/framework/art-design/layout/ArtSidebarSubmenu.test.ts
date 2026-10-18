import { afterEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, markRaw } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { ElMenu } from 'element-plus';
import ArtSidebarSubmenu from './ArtSidebarSubmenu.vue';
import type { ShellNavigationTreeItem } from '../adapters/fullNetShellAdapter';

const wrappers: VueWrapper[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  vi.restoreAllMocks();
});

async function mountMenu(denyProduct = false) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: ['/', '/catalog/products', '/403'].map(path => ({
      path, component: { template: '<div />' }
    }))
  });
  if (denyProduct) router.beforeEach(to => to.path === '/catalog/products' ? '/403' : true);
  await router.push('/');
  const item: ShellNavigationTreeItem = {
    id: 'products', path: '/catalog/products', routeName: 'products',
    componentKey: 'products', title: 'Products', caption: 'Products',
    icon: markRaw(defineComponent({ render: () => h('span') })),
    isAffix: false, children: []
  };
  const wrapper = mount(defineComponent({
    render: () => h(ElMenu, {}, { default: () => h(ArtSidebarSubmenu, { items: [item] }) })
  }), { global: { plugins: [router] } });
  wrappers.push(wrapper);
  return { wrapper, router, push: vi.spyOn(router, 'push') };
}

it('叶子链接一次点击只提交一次路由导航', async () => {
  const { wrapper, router, push } = await mountMenu();
  await wrapper.get('a').trigger('click');
  await flushPromises();
  expect(push).toHaveBeenCalledTimes(1);
  expect(router.currentRoute.value.path).toBe('/catalog/products');
});

it('键盘激活产生的链接点击只提交一次导航', async () => {
  const { wrapper, router, push } = await mountMenu();
  wrapper.get('a').element.dispatchEvent(new MouseEvent('click', {
    bubbles: true, cancelable: true, detail: 0
  }));
  await flushPromises();
  expect(push).toHaveBeenCalledTimes(1);
  expect(router.currentRoute.value.path).toBe('/catalog/products');
});

it('菜单项自身的点击仍可导航', async () => {
  const { wrapper, router, push } = await mountMenu();
  await wrapper.get('.el-menu-item').trigger('click');
  await flushPromises();
  expect(push).toHaveBeenCalledTimes(1);
  expect(router.currentRoute.value.path).toBe('/catalog/products');
});

it.each(['ctrlKey', 'metaKey'] as const)('%s 链接点击保留浏览器处理且不切换当前路由', async key => {
  const { wrapper, router, push } = await mountMenu();
  await wrapper.get('a').trigger('click', { [key]: true });
  await flushPromises();
  expect(push).not.toHaveBeenCalled();
  expect(router.currentRoute.value.path).toBe('/');
});

it('链接导航继续经过路由守卫', async () => {
  const { wrapper, router, push } = await mountMenu(true);
  await wrapper.get('a').trigger('click');
  await flushPromises();
  expect(push).toHaveBeenCalledTimes(1);
  expect(router.currentRoute.value.path).toBe('/403');
});
