import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentPermissionsView from './DocumentPermissionsView.vue';
import { useSessionStore } from '../auth/session';
import { listDocumentItems } from '../api/host-document-items';

vi.mock('../api/host-document-items', () => ({
  listDocumentItems: vi.fn()
}));

vi.mock('../api/document-permissions', () => ({
  getDocumentPermissionsByDocument: vi.fn().mockResolvedValue([]),
  setDocumentPermissions: vi.fn()
}));

const listMock = vi.mocked(listDocumentItems);

const samplePage = {
  items: [{
    id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
    title: 'Spec',
    documentNo: 'DOC-1',
    categoryName: null,
    createdAtUtc: '2026-01-01T00:00:00Z'
  }],
  page: 1,
  pageSize: 20,
  total: 1
};

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
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(DocumentPermissionsView, { global: { plugins: [pinia] } });
}

describe('Vue 文档权限页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue(samplePage as never);
  });

  it('仅有 read 时不显示保存按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_documents.read',
      'document.host_permissions.read'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-permissions-set"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-permissions-save"]').exists()).toBe(false);
  });

  it('set-only 在打开弹窗后显示保存按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_documents.read',
      'document.host_permissions.read',
      'document.host_permissions.set'
    ]);
    await flushPromises();
    await wrapper.find('[data-testid="document-permissions-set"]').trigger('click');
    await flushPromises();
    expect(document.body.querySelector('[data-testid="document-permissions-save"]')).not.toBeNull();
  });
});
