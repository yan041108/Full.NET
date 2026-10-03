import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import { createPinia } from 'pinia';
import ArtSearchBar from '../framework/art-design/components/ArtSearchBar.vue';
import OutboundCallLogsView from './OutboundCallLogsView.vue';
import AuditLogDetailDrawer from './components/AuditLogDetailDrawer.vue';
import { listAuditingOutboundCallLogs } from '../api/outbound-call-logs';

vi.mock('../api/outbound-call-logs', () => ({ listAuditingOutboundCallLogs: vi.fn() }));

const listMock = vi.mocked(listAuditingOutboundCallLogs);

function log(id: string, operationKey: string) {
  return {
    id,
    occurredAtUtc: '2026-09-29T00:00:00Z',
    providerKey: 'example',
    operationKey,
    destinationHostCategory: 'internal',
    statusCode: 200,
    succeeded: true,
    durationMs: 2,
    retryCount: 0,
    traceId: null,
    safeErrorCode: null,
    tenantId: null,
    userId: null
  };
}

describe('外呼日志服务端分页', () => {
  beforeEach(() => listMock.mockReset());

  it('翻页时请求后续数据并保留服务端总数', async () => {
    listMock
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ab', 'first')], page: 1, pageSize: 20, total: 45 })
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ac', 'second')], page: 2, pageSize: 20, total: 45 });

    const wrapper = mount(OutboundCallLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    expect(wrapper.findComponent(ElPagination).props('total')).toBe(45);
    expect(wrapper.find('.art-table-pagination').exists()).toBe(true);
    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2);
    await flushPromises();

    expect(listMock).toHaveBeenCalledWith(2, 20, expect.any(AbortSignal), {});
    expect(wrapper.text()).toContain('second');
    expect(wrapper.text()).not.toContain('first');
    wrapper.unmount();
  });

  it('外呼操作筛选补可见时间窗并从第一页重新请求', async () => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 45 });
    const wrapper = mount(OutboundCallLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2);
    await flushPromises();

    wrapper.findComponent(ArtSearchBar).vm.$emit('search', {
      operationContains: ' send ', providerKey: 'smtp', succeeded: 'false'
    });
    await flushPromises();

    expect(wrapper.findComponent(ElPagination).props('currentPage')).toBe(1);
    expect(listMock).toHaveBeenLastCalledWith(1, 20, expect.any(AbortSignal), {
      operationContains: 'send', providerKey: 'smtp', succeeded: false,
      fromUtc: expect.any(String), toUtc: expect.any(String)
    });
    const filters = listMock.mock.lastCall?.[3];
    expect(Date.parse(filters?.toUtc ?? '') - Date.parse(filters?.fromUtc ?? ''))
      .toBe(24 * 60 * 60 * 1000);
    const visible = wrapper.findComponent(ArtSearchBar).props('modelValue') as Record<string, string>;
    expect(visible.fromUtc).toBeTruthy();
    expect(visible.toUtc).toBeTruthy();
    wrapper.unmount();
  });

  it('详情摘要展示安全元数据且禁用领域差异查询', async () => {
    const row = { ...log('01912345-6789-7abc-8def-0123456789ab', 'send'),
      retryCount: 2, safeErrorCode: 'timeout', traceId: 'trace-123' };
    listMock.mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 });
    const wrapper = mount(OutboundCallLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    wrapper.findComponent({ name: 'ElTable' }).vm.$emit('row-click', row);
    await flushPromises();
    const record = wrapper.findComponent(AuditLogDetailDrawer).props('record');
    expect(record?.supportsDiff).toBe(false);
    expect(record?.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ value: row.destinationHostCategory }),
      expect.objectContaining({ value: row.retryCount }),
      expect.objectContaining({ value: row.safeErrorCode })
    ]));
    wrapper.unmount();
  });
});
