import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import { createMemoryHistory } from 'vue-router';
import { createPinia, setActivePinia } from 'pinia';
import { ElConfigProvider, ElOption } from 'element-plus';
import App from './App.vue';
import { createAppRouter } from './router';
import { useSessionStore } from './auth/session';
import { useAdminI18n } from './i18n/adminI18n';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

function createAuthenticatedPinia() {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.$patch({
    state: 'authenticated',
    currentUser: {
      id: 'user-id', username: 'admin', displayName: '系统管理员',
      tenantId: null, actorScope: 'host', scope: 'host',
      isSuperAdministrator: true,
      permissions: ['platform.dashboard.read', 'tenancy.tenants.read'],
      sessionId: 'session-id', preferredLocale: 'zh-CN', profileVersion: 1
    },
    navigation: [{
      id: 'overview', parentId: null, routeName: 'overview', path: '/',
      componentKey: 'overview', title: 'SERVER CONTROLLED TITLE',
      caption: 'SERVER CONTROLLED CAPTION',
      icon: 'dashboard', order: 10,
      requiredPermission: 'platform.dashboard.read', children: []
    }, {
      id: 'tenant-context', parentId: null, routeName: 'tenant-context',
      path: '/tenant-context', componentKey: 'tenant-context',
      title: '租户上下文', caption: '进入租户或返回 Host',
      icon: 'building', order: 20,
      requiredPermission: 'tenancy.tenants.read', children: []
    }],
    availableTenants: [{
      id: tenantId,
      identifier: 'acme',
      name: 'Acme Corporation',
      domain: 'acme.localhost'
    }]
  });
  return pinia;
}

describe('Vue 管理端壳层', () => {
  beforeEach(() => {
    localStorage.clear();
    useAdminI18n().setLocale('zh-CN');
  });

  it('展示服务端导航、Host 上下文和可用租户', async () => {
    const pinia = createAuthenticatedPinia();
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/');
    await router.isReady();

    const wrapper = mount(App, {
      global: { plugins: [pinia, router] }
    });

    expect(wrapper.findComponent(ElConfigProvider).exists()).toBe(true);
    expect(wrapper.text()).toContain('Full.NET');
    expect(wrapper.findAllComponents(ElOption).some(option =>
      option.props('label') === 'Full.NET Host'
    )).toBe(true);
    expect(wrapper.findAllComponents(ElOption).some(option =>
      option.props('label') === 'Acme Corporation'
    )).toBe(true);
    expect(wrapper.text()).toContain('工作台');
    expect(wrapper.text()).toContain('租户上下文');
    expect(wrapper.text()).toContain('系统管理员');
    expect(wrapper.text()).not.toContain('身份权限');
  });

  it('403 路由呈现权限错误页', async () => {
    const pinia = createAuthenticatedPinia();
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/403');
    await router.isReady();

    const wrapper = mount(App, {
      global: { plugins: [pinia, router] }
    });

    expect(wrapper.text()).toContain('没有访问权限');
    expect(wrapper.text()).toContain('403');
  }, 10_000);

  it('切换语言后更新可信导航、文档语义和页面标题', async () => {
    const pinia = createAuthenticatedPinia();
    const session = useSessionStore();
    session.changeLocale = async locale => {
      useAdminI18n().setLocale(locale);
    };
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/');
    await router.isReady();
    const wrapper = mount(App, {
      attachTo: document.body,
      global: { plugins: [pinia, router] }
    });

    expect(wrapper.get('.skip-link').attributes('href')).toBe('#main-content');
    await wrapper.get('.skip-link').trigger('click');
    expect(document.activeElement).toBe(wrapper.get('#main-content').element);
    expect(wrapper.get('nav .art-sidebar__link.is-active').text()).toContain('工作台');
    expect(wrapper.get('[data-route-heading]').attributes('tabindex')).toBe('-1');

    await wrapper.get('[data-testid="shell-locale-trigger"]').trigger('click');
    await nextTick();
    const englishItem = [...document.querySelectorAll('.el-dropdown-menu__item')]
      .find(item => item.textContent?.includes('English'));
    expect(englishItem).toBeTruthy();
    await (englishItem as HTMLElement).click();
    await vi.waitFor(() => expect(useAdminI18n().locale.value).toBe('en-US'));

    expect(wrapper.text()).toContain('Overview');
    expect(wrapper.text()).toContain('Tenant context');
    expect(wrapper.text()).not.toContain('SERVER CONTROLLED TITLE');
    expect(document.documentElement.lang).toBe('en-US');
    expect(document.title).toBe('Overview · Full.NET');
    wrapper.unmount();
  });
});
