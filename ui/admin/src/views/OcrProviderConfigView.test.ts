import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OcrProviderConfigView from './OcrProviderConfigView.vue';
import { getOcrProviderConfig } from '../api/ocr';

vi.mock('../api/ocr', () => ({
  getOcrProviderConfig: vi.fn(),
  updateOcrProviderConfig: vi.fn(),
  testOcrProviderConfig: vi.fn(),
  PADDLE_OCR_ID_CARD_PROVIDER_KEY: 'paddle_ocr_id_card'
}));

const getMock = vi.mocked(getOcrProviderConfig);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(OcrProviderConfigView, { global: { plugins: [pinia] } });
}

describe('OcrProviderConfigView', () => {
  beforeEach(() => {
    getMock.mockResolvedValue({
      id: '018fcd80-0000-7000-8000-000000000080',
      providerKey: 'paddle_ocr_id_card',
      name: 'PaddleOCR',
      baseUrl: 'http://localhost:8080',
      hasApiKey: false,
      isEnabled: false,
      lastTestedAtUtc: null,
      lastTestStatusKey: null,
      lastTestMessage: null,
      createdAtUtc: '2026-09-07T00:00:00.000Z',
      updatedAtUtc: null,
      version: 1
    });
  });

  it('hides save action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="ocr-provider-save"]').exists()).toBe(false);
  });
});
