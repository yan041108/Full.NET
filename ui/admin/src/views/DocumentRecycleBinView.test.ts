import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentRecycleBinView from './DocumentRecycleBinView.vue';
import { useSessionStore } from '../auth/session';
import { listRecycleBinItems } from '../api/document-recycle-bin';

vi.mock('../api/document-recycle-bin', () => ({
  listRecycleBinItems: vi.fn(),
  restoreRecycleBinItem: vi.fn(),
  purgeRecycleBinItem: vi.fn()
}));

const listMock = vi.mocked(listRecycleBinItems);

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
  return mount(DocumentRecycleBinView, { global: { plugins: [pinia] } });
}

describe('Vue 文档回收站页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
        title: 'Deleted doc',
        version: 2
      }],
      page: 1,
      pageSize: 20,
      total: 1
    } as never);
  });

  it('仅有 read 时不显示写入操作', async () => {
    const wrapper = mountWithPermissions(['document.host_recycle_bin.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-recycle-restore"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-recycle-purge"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('Deleted doc');
  });

  it('restore-only 只显示恢复按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_recycle_bin.read',
      'document.host_recycle_bin.restore'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-recycle-restore"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-recycle-purge"]').exists()).toBe(false);
  });
});
