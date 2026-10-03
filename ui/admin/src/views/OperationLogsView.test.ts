import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import { createPinia } from 'pinia';
import ArtSearchBar from '../framework/art-design/components/ArtSearchBar.vue';
import OperationLogsView from './OperationLogsView.vue';
import AuditLogDetailDrawer from './components/AuditLogDetailDrawer.vue';
import { listAuditingOperationLogs } from '../api/operation-logs';

vi.mock('../api/operation-logs', () => ({ listAuditingOperationLogs: vi.fn() }));

const listMock = vi.mocked(listAuditingOperationLogs);

function log(id: string, actionKey: string) {
  return {
    id,
    occurredAtUtc: '2026-09-29T00:00:00Z',
    actionKey,
    httpMethod: 'GET',
    requestPath: '/api/example',
    statusCode: 200,
    durationMs: 2,
    succeeded: true,
    userId: null,
    tenantId: null,
    traceId: null,
    clientIpFingerprint: null,
    permissionCode: null
  };
}

describe('操作日志服务端分页', () => {
  beforeEach(() => listMock.mockReset());

  it('按实际页码请求，展示服务端 total 和第二页数据', async () => {
    listMock
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ab', 'first')], page: 1, pageSize: 20, total: 45 })
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ac', 'second')], page: 2, pageSize: 20, total: 45 })
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ad', 'resized')], page: 1, pageSize: 50, total: 45 });

    const wrapper = mount(OperationLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    expect(listMock).toHaveBeenCalledWith(1, 20, expect.any(AbortSignal), {});
    expect(wrapper.findComponent(ElPagination).props('total')).toBe(45);
    expect(wrapper.find('.art-table-pagination').exists()).toBe(true);

    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2);
    await flushPromises();
    expect(listMock).toHaveBeenCalledWith(2, 20, expect.any(AbortSignal), {});
    expect(wrapper.text()).toContain('second');
    expect(wrapper.text()).not.toContain('first');

    wrapper.findComponent(ElPagination).vm.$emit('size-change', 50);
    await flushPromises();
    expect(listMock).toHaveBeenCalledWith(1, 50, expect.any(AbortSignal), {});
    expect(wrapper.findComponent(ElPagination).props('currentPage')).toBe(1);
    expect(wrapper.text()).toContain('resized');
    wrapper.unmount();
  });

  it('新请求先完成时忽略旧响应，并在离开页面时取消请求', async () => {
    let resolveFirst!: (value: Awaited<ReturnType<typeof listAuditingOperationLogs>>) => void;
    listMock
      .mockImplementationOnce(() => new Promise(resolve => { resolveFirst = resolve; }))
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ac', 'latest')], page: 2, pageSize: 20, total: 45 });

    const wrapper = mount(OperationLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    const firstSignal = listMock.mock.calls[0]?.[2];
    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2);
    await flushPromises();
    expect(firstSignal?.aborted).toBe(true);

    resolveFirst({ items: [log('01912345-6789-7abc-8def-0123456789ab', 'stale')], page: 1, pageSize: 20, total: 45 });
    await flushPromises();
    expect(wrapper.text()).toContain('latest');
    expect(wrapper.text()).not.toContain('stale');
    const secondSignal = listMock.mock.calls[1]?.[2];
    wrapper.unmount();
    expect(secondSignal?.aborted).toBe(true);
  });

  it('提交筛选条件时回到第一页并请求服务端筛选结果', async () => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 45 });
    const wrapper = mount(OperationLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2);
    await flushPromises();

    wrapper.findComponent(ArtSearchBar).vm.$emit('search', {
      pathContains: ' /api/orders ',
      httpMethod: 'POST',
      succeeded: 'false'
    });
    await flushPromises();

    expect(wrapper.findComponent(ElPagination).props('currentPage')).toBe(1);
    expect(listMock).toHaveBeenLastCalledWith(1, 20, expect.any(AbortSignal), {
      pathContains: '/api/orders',
      httpMethod: 'POST',
      succeeded: false,
      fromUtc: expect.any(String),
      toUtc: expect.any(String)
    });
    const filters = listMock.mock.lastCall?.[3];
    expect(Date.parse(filters?.toUtc ?? '') - Date.parse(filters?.fromUtc ?? ''))
      .toBe(24 * 60 * 60 * 1000);
    const visible = wrapper.findComponent(ArtSearchBar).props('modelValue') as Record<string, string>;
    expect(visible.fromUtc).toBeTruthy();
    expect(visible.toUtc).toBeTruthy();
    wrapper.unmount();
  });

  it('路径筛选缺少成对有效时间时不发送请求', async () => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    const wrapper = mount(OperationLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    const callsBeforeSearch = listMock.mock.calls.length;

    wrapper.findComponent(ArtSearchBar).vm.$emit('search', {
      pathContains: '/api/orders',
      fromUtc: 'not-a-date'
    });
    await flushPromises();

    expect(listMock).toHaveBeenCalledTimes(callsBeforeSearch);
    expect(wrapper.text()).toContain('client.auditing_operation_log_time_range_invalid');
    wrapper.unmount();
  });

  it('详情摘要展示列表已有的身份与安全元数据', async () => {
    const row = { ...log('01912345-6789-7abc-8def-0123456789ab', 'update'),
      userId: '01912345-6789-7abc-8def-0123456789ac',
      tenantId: '01912345-6789-7abc-8def-0123456789ad',
      clientIpFingerprint: 'fp-123', permissionCode: 'orders.update' };
    listMock.mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 });
    const wrapper = mount(OperationLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    wrapper.findComponent({ name: 'ElTable' }).vm.$emit('row-click', row);
    await flushPromises();
    const fields = wrapper.findComponent(AuditLogDetailDrawer).props('record')?.fields;
    expect(wrapper.findComponent(AuditLogDetailDrawer).props('record')).toMatchObject({
      supportsRestrictedDetails: true
    });
    expect(fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ value: row.userId }),
      expect.objectContaining({ value: row.tenantId }),
      expect.objectContaining({ value: row.clientIpFingerprint }),
      expect.objectContaining({ value: row.permissionCode })
    ]));
    wrapper.unmount();
  });
});
