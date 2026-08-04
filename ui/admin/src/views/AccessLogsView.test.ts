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

  it('挂载时自动拉取全部游标页并按顺序展示', async () => {
    const wrapper = mount(AccessLogsView);
    await flushPromises();

    expect(listMock).toHaveBeenNthCalledWith(1);
    expect(listMock).toHaveBeenNthCalledWith(2, { cursor: 'cursor-next' });
    expect(wrapper.text()).toContain('/first');
    expect(wrapper.text()).toContain('/second');
    expect(wrapper.text().indexOf('/first')).toBeLessThan(
      wrapper.text().indexOf('/second')
    );
    expect(wrapper.find('.el-pagination').exists()).toBe(true);
  });

  it('启用 contains 时显示 24 小时范围并用同一筛选重新加载', async () => {
    const wrapper = mount(AccessLogsView);
    await flushPromises();
    const callsAfterMount = listMock.mock.calls.length;

    const pathInput = wrapper.find('input[placeholder="路径包含"]');
    await pathInput.setValue(' /api/v1/settings ');
    await flushPromises();

    const fromInput = wrapper.findAll('input').find(input =>
      input.attributes('placeholder') === '开始时间'
    )?.element as HTMLInputElement | undefined;
    const toInput = wrapper.findAll('input').find(input =>
      input.attributes('placeholder') === '结束时间'
    )?.element as HTMLInputElement | undefined;
    expect(fromInput?.value).not.toBe('');
    expect(toInput?.value).not.toBe('');

    await wrapper.find('.art-search-bar__buttons .el-button--primary').trigger('click');
    await flushPromises();

    const searchCalls = listMock.mock.calls.slice(callsAfterMount);
    const filteredRequest = searchCalls.map(call => call[0]).find(options => options?.pathContains);
    expect(filteredRequest?.pathContains).toBe('/api/v1/settings');
    expect(Date.parse(filteredRequest?.toUtc ?? '')
      - Date.parse(filteredRequest?.fromUtc ?? '')).toBe(24 * 60 * 60 * 1000);

    const callsBeforeClear = listMock.mock.calls.length;
    await wrapper.find('.art-search-bar__buttons .el-button').trigger('click');
    await flushPromises();
    expect(fromInput?.value).toBe('');
    expect(toInput?.value).toBe('');
    await wrapper.find('.art-search-bar__buttons .el-button--primary').trigger('click');
    await flushPromises();
    expect(listMock.mock.calls.length).toBeGreaterThan(callsBeforeClear);
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
