import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import K3CloudConnectionConfigsView from './K3CloudConnectionConfigsView.vue';
import { listK3CloudConnectionConfigs } from '../api/k3cloud';

vi.mock('../api/k3cloud', () => ({
  listK3CloudConnectionConfigs: vi.fn(),
  createK3CloudConnectionConfig: vi.fn(),
  updateK3CloudConnectionConfig: vi.fn(),
  testK3CloudConnectionConfig: vi.fn()
}));

const listMock = vi.mocked(listK3CloudConnectionConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(K3CloudConnectionConfigsView, { global: { plugins: [pinia] } });
}

describe('K3CloudConnectionConfigsView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue([]);
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="k3cloud-connection-create"]').exists()).toBe(false);
  });
});
