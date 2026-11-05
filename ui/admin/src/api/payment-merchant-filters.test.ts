import { beforeEach, expect, it, vi } from 'vitest';
import { listPaymentMerchantConfigs } from './payment-merchant-configs';
const request = vi.hoisted(() => vi.fn()); vi.mock('./http', () => ({ request }));
beforeEach(() => { vi.resetAllMocks(); request.mockResolvedValue({ items: [], page: 2, pageSize: 20, total: 0 }); });
it('租户/渠道/名称编码并保留 false 启用筛选及取消信号', async () => {
  const signal = new AbortController().signal;
  await listPaymentMerchantConfigs({ page: 2, pageSize: 20, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf201', channelKey: 'alipay_page', nameContains: '商户 & A', isEnabled: false }, signal);
  const [path, init, passedSignal] = request.mock.lastCall!; const url = new URL(path, 'https://example.com');
  expect(url.searchParams.get('tenantId')).toBe('019bc2b1-2a40-7cc3-8992-a80de51bf201'); expect(url.searchParams.get('channelKey')).toBe('alipay_page');
  expect(url.searchParams.get('nameContains')).toBe('商户 & A'); expect(url.searchParams.get('isEnabled')).toBe('false'); expect(init).toEqual({ method: 'GET' }); expect(passedSignal).toBe(signal);
});
it('全部状态不发送启用/租户/渠道或名称条件', async () => {
  await listPaymentMerchantConfigs(); expect(request.mock.lastCall![0]).toBe('/api/v1/payments/merchant-configs?page=1&pageSize=20');
});
