import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import AccessLogsView from './AccessLogsView.vue';
import { listAuditingAccessLogsByCursor } from '../api/access-logs';

vi.mock('../api/access-logs', () => ({
  listAuditingAccessLogsByCursor: vi.fn()
}));

const listMock = vi.mocked(listAuditingAccessLogsByCursor);

describe('Vue 访问日志页', () => {
  beforeEach(() => {
    listMock.mockReset()
      .mockResolvedValueOnce({
        items: [createLog('01912345-6789-7abc-8def-0123456789ab', '/first')],
        nextCursor: 'cursor-next',
        hasMore: true
      })
      .mockResolvedValueOnce({
        items: [createLog('01912345-6789-7abc-8def-0123456789ac', '/second')],
        nextCursor: null,
        hasMore: false
      })
      .mockResolvedValue({
        items: [],
        nextCursor: null,
        hasMore: false
      });
  });

  it('使用游标加载并按服务端顺序追加下一批', async () => {
    const wrapper = mount(AccessLogsView);
    await flushPromises();

    expect(listMock).toHaveBeenNthCalledWith(1);
    expect(wrapper.text()).toContain('/first');

    await wrapper.get('[data-testid="access-logs-load-more"]').trigger('click');
    await flushPromises();

    expect(listMock).toHaveBeenNthCalledWith(2, {
      cursor: 'cursor-next'
    });
    expect(wrapper.findAll('.art-data-row')).toHaveLength(2);
    expect(wrapper.text().indexOf('/first')).toBeLessThan(
      wrapper.text().indexOf('/second'));
    expect(wrapper.find('[data-testid="access-logs-load-more"]').exists())
      .toBe(false);
  });

  it('启用 contains 时显示 24 小时范围并用同一筛选重新加载', async () => {
    const wrapper = mount(AccessLogsView);
    await flushPromises();

    await wrapper.get('[data-testid="access-logs-path-contains"]')
      .setValue(' /api/v1/settings ');

    const fromInput = wrapper.get(
      '[data-testid="access-logs-from-utc"]'
    ).element as HTMLInputElement;
    const toInput = wrapper.get(
      '[data-testid="access-logs-to-utc"]'
    ).element as HTMLInputElement;
    expect(fromInput.value).not.toBe('');
    expect(toInput.value).not.toBe('');

    await wrapper.get('[data-testid="access-logs-search"]').trigger('click');
    await flushPromises();

    const request = listMock.mock.calls[1][0];
    expect(request?.pathContains).toBe('/api/v1/settings');
    expect(Date.parse(request?.toUtc ?? '')
      - Date.parse(request?.fromUtc ?? '')).toBe(24 * 60 * 60 * 1000);

    await wrapper.get('[data-testid="access-logs-path-contains"]')
      .setValue('');
    expect(fromInput.value).toBe('');
    expect(toInput.value).toBe('');
    await wrapper.get('[data-testid="access-logs-search"]').trigger('click');
    await flushPromises();
    expect(listMock).toHaveBeenNthCalledWith(3);
  });
});

function createLog(id: string, requestPath: string) {
  return {
    id,
    occurredAtUtc: '2026-07-27T00:00:00Z',
    httpMethod: 'GET',
    requestPath,
    statusCode: 200,
    durationMs: 10,
    userId: null,
    tenantId: null,
    traceId: null,
    clientIpFingerprint: null,
    isAuthenticated: true
  };
}
