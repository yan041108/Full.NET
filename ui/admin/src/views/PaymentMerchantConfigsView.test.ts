import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PaymentMerchantConfigsView from './PaymentMerchantConfigsView.vue';
import { listPaymentMerchantConfigs } from '../api/payment-merchant-configs';

vi.mock('../api/payment-merchant-configs', () => ({
  listPaymentMerchantConfigs: vi.fn(),
  getPaymentMerchantConfig: vi.fn(),
  createPaymentMerchantConfig: vi.fn(),
  updatePaymentMerchantConfig: vi.fn(),
  disablePaymentMerchantConfig: vi.fn()
}));

const listMock = vi.mocked(listPaymentMerchantConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(PaymentMerchantConfigsView, { global: { plugins: [pinia] } });
}

describe('PaymentMerchantConfigsView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="payment-merchant-config-create"]').exists()).toBe(false);
  });
});
