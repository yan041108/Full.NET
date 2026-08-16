import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentSharesView from './DocumentSharesView.vue';
import { useSessionStore } from '../auth/session';
import { listDocumentShares } from '../api/document-shares';

vi.mock('../api/document-shares', () => ({
  listDocumentShares: vi.fn(),
  createDocumentShare: vi.fn(),
  updateDocumentShareStatus: vi.fn()
}));

const listMock = vi.mocked(listDocumentShares);

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
  return mount(DocumentSharesView, { global: { plugins: [pinia] } });
}

describe('Vue 文档分享页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
        documentId: '0198f36e-f7a7-7c52-9cbb-774e67411206',
        shareCode: 'abc123',
        accessCount: 0,
        isEnabled: true,
        version: 1,
        hasPassword: false
      }],
      page: 1,
      pageSize: 20,
      total: 1
    } as never);
  });

  it('仅有 read 时不显示写入操作', async () => {
    const wrapper = mountWithPermissions(['document.host_shares.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-share-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-share-toggle"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('abc123');
  });

  it('create-only 只显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_shares.read', 'document.host_shares.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-share-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-share-toggle"]').exists()).toBe(false);
  });
});
