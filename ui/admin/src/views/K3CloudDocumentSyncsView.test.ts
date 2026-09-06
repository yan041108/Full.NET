import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import K3CloudDocumentSyncsView from './K3CloudDocumentSyncsView.vue';
import { listK3CloudConnectionConfigs, listK3CloudDocumentSyncs } from '../api/k3cloud';

vi.mock('../api/k3cloud', () => ({
  listK3CloudDocumentSyncs: vi.fn(),
  listK3CloudConnectionConfigs: vi.fn(),
  createK3CloudDocumentSync: vi.fn(),
  retryK3CloudDocumentSync: vi.fn()
}));

const listSyncMock = vi.mocked(listK3CloudDocumentSyncs);
const listConnectionMock = vi.mocked(listK3CloudConnectionConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(K3CloudDocumentSyncsView, { global: { plugins: [pinia] } });
}

describe('K3CloudDocumentSyncsView', () => {
  beforeEach(() => {
    listSyncMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    listConnectionMock.mockResolvedValue([]);
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="k3cloud-document-sync-create"]').exists()).toBe(false);
  });
});
