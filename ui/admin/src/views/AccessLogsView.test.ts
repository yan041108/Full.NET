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
      });
  });

  it('使用游标加载并按服务端顺序追加下一批', async () => {
    const wrapper = mount(AccessLogsView);
    await flushPromises();

    expect(listMock).toHaveBeenNthCalledWith(1);
    expect(wrapper.text()).toContain('/first');

    await wrapper.get('[data-testid="access-logs-load-more"]').trigger('click');
    await flushPromises();

    expect(listMock).toHaveBeenNthCalledWith(2, 'cursor-next');
    expect(wrapper.findAll('.art-data-row')).toHaveLength(2);
    expect(wrapper.text().indexOf('/first')).toBeLessThan(
      wrapper.text().indexOf('/second'));
    expect(wrapper.find('[data-testid="access-logs-load-more"]').exists())
      .toBe(false);
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
