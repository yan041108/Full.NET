import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PaymentOrdersView from './PaymentOrdersView.vue';
import { listPaymentOrders } from '../api/payment-orders';

vi.mock('../api/payment-orders', () => ({
  listPaymentOrders: vi.fn(),
  createPaymentOrder: vi.fn(),
  getPaymentOrder: vi.fn()
}));

const listMock = vi.mocked(listPaymentOrders);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(PaymentOrdersView, { global: { plugins: [pinia] } });
}

describe('PaymentOrdersView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="payment-order-create"]').exists()).toBe(false);
  });
});
