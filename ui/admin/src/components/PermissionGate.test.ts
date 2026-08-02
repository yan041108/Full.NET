import { beforeEach, describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import PermissionGate from './PermissionGate.vue';
import { useSessionStore } from '../auth/session';

const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';

describe('Vue PermissionGate', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('无精确权限时不渲染默认插槽', () => {
    const session = useSessionStore();
    session.currentUser = authenticatedUser(['identity.users.read']);

    const wrapper = mount(PermissionGate, {
      props: { code: 'identity.users.reset_password' },
      slots: { default: '<button>reset</button>' }
    });

    expect(wrapper.find('button').exists()).toBe(false);
  });

  it('拥有精确权限时渲染默认插槽', () => {
    const session = useSessionStore();
    session.currentUser = authenticatedUser(['identity.users.reset_password']);

    const wrapper = mount(PermissionGate, {
      props: { code: 'identity.users.reset_password' },
      slots: { default: '<button>reset</button>' }
    });

    expect(wrapper.find('button').exists()).toBe(true);
  });

  it('权限撤销后移除已渲染内容', async () => {
    const session = useSessionStore();
    session.currentUser = authenticatedUser(['identity.users.reset_password']);

    const wrapper = mount(PermissionGate, {
      props: { code: 'identity.users.reset_password' },
      slots: { default: '<button>reset</button>' }
    });
    expect(wrapper.find('button').exists()).toBe(true);

    session.currentUser = authenticatedUser([]);
    await wrapper.vm.$nextTick();

    expect(wrapper.find('button').exists()).toBe(false);
  });
});

function authenticatedUser(permissions: string[]) {
  return {
    id: userId,
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host' as const,
    scope: 'host' as const,
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN' as const,
    profileVersion: 1
  };
}