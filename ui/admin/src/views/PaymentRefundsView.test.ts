import { flushPromises, mount } from '@vue/test-utils';
import type { PaymentRefundListItem } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
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
  const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf209', sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf210',
    tenantId: null, actorScope: 'host', scope: 'host', permissions: ['payments.refunds.read'] } as NonNullable<typeof session.currentUser>;
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
    wrapper.unmount();
  });
  it('退款记录使用精确分展示，刷新重新读取并拒绝失真金额', async () => {
    const row = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf201', orderId: '019bc2b1-2a40-7cc3-8992-a80de51bf202', tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf203',
      outRefundNo: 'refund', refundStateKey: 'processing', amountMinor: '9223372036854775806', currency: 'CNY' } as PaymentRefundListItem;
    listMock.mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 });
    const wrapper = mountView(); await flushPromises();
    expect(wrapper.get('.el-table__body-wrapper').text()).toContain('92233720368547758.06');
    listMock.mockResolvedValue({ items: [{ ...row, amountMinor: '9223372036854775808' }], page: 1, pageSize: 20, total: 1 });
    wrapper.getComponent({ name: 'ArtTableHeader' }).vm.$emit('refresh'); await flushPromises();
    expect(wrapper.get('.el-table__body-wrapper').text()).toContain('金额无法精确显示'); wrapper.unmount();
  });
});
