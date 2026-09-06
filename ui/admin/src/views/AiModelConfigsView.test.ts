import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AiModelConfigsView from './AiModelConfigsView.vue';
import { listAiModelConfigs } from '../api/ai-model-configs';

vi.mock('../api/ai-model-configs', () => ({
  listAiModelConfigs: vi.fn(),
  listAiTenantQuotas: vi.fn(),
  getAiModelConfig: vi.fn(),
  createAiModelConfig: vi.fn(),
  updateAiModelConfig: vi.fn(),
  disableAiModelConfig: vi.fn(),
  testAiModelConfig: vi.fn(),
  upsertAiTenantQuota: vi.fn()
}));

const listMock = vi.mocked(listAiModelConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(AiModelConfigsView, { global: { plugins: [pinia] } });
}

describe('AiModelConfigsView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0
    });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="ai-model-config-create"]').exists()).toBe(false);
  });
});
