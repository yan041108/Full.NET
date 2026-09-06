import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PaymentRefundsView from './PaymentRefundsView.vue';
import { listPaymentRefunds } from '../api/payment-refunds';

vi.mock('../api/payment-refunds', () => ({
  listPaymentRefunds: vi.fn(),
  getPaymentRefund: vi.fn()
}));

const listMock = vi.mocked(listPaymentRefunds);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(PaymentRefundsView, { global: { plugins: [pinia] } });
}

describe('PaymentRefundsView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('renders refund list container', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('.payment-refunds-view').exists()).toBe(true);
  });
});
