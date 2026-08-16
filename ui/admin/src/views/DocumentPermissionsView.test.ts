import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentPermissionsView from './DocumentPermissionsView.vue';
import { useSessionStore } from '../auth/session';

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(DocumentPermissionsView, { global: { plugins: [pinia] } });
}

describe('Vue 文档权限页', () => {
  it('仅有 read 时不显示保存按钮', () => {
    const wrapper = mountWithPermissions(['document.host_permissions.read']);
    expect(wrapper.find('[data-testid="document-permissions-load"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-permissions-save"]').exists()).toBe(false);
  });

  it('set-only 显示保存按钮', () => {
    const wrapper = mountWithPermissions(['document.host_permissions.set']);
    expect(wrapper.find('[data-testid="document-permissions-save"]').exists()).toBe(true);
  });
});
