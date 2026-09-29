import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import { createPinia } from 'pinia';
import ArtSearchBar from '../framework/art-design/components/ArtSearchBar.vue';
import ExceptionLogsView from './ExceptionLogsView.vue';
import AuditLogDetailDrawer from './components/AuditLogDetailDrawer.vue';
import { listAuditingExceptionLogs } from '../api/exception-logs';

vi.mock('../api/exception-logs', () => ({ listAuditingExceptionLogs: vi.fn() }));

const listMock = vi.mocked(listAuditingExceptionLogs);

function log(id: string, message: string) {
  return {
    id,
    occurredAtUtc: '2026-09-29T00:00:00Z',
    exceptionType: 'System.Exception',
    message,
    stackTrace: null,
    httpMethod: null,
    requestPath: null,
    userId: null,
    tenantId: null,
    traceId: null,
    clientIpFingerprint: null
  };
}

describe('异常日志服务端分页', () => {
  beforeEach(() => listMock.mockReset());

  it('翻页时请求后续数据并保留服务端总数', async () => {
    listMock
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ab', 'first')], page: 1, pageSize: 20, total: 45 })
      .mockResolvedValueOnce({ items: [log('01912345-6789-7abc-8def-0123456789ac', 'second')], page: 2, pageSize: 20, total: 45 });

    const wrapper = mount(ExceptionLogsView, { global: { plugins: [createPinia()] } });
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

  it('异常类型筛选补可见时间窗并从第一页重新请求', async () => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 45 });
    const wrapper = mount(ExceptionLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    wrapper.findComponent(ElPagination).vm.$emit('current-change', 2);
    await flushPromises();

    wrapper.findComponent(ArtSearchBar).vm.$emit('search', { exceptionTypeContains: ' InvalidOperation ' });
    await flushPromises();

    expect(wrapper.findComponent(ElPagination).props('currentPage')).toBe(1);
    expect(listMock).toHaveBeenLastCalledWith(1, 20, expect.any(AbortSignal), {
      exceptionTypeContains: 'InvalidOperation',
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

  it('详情摘要展示身份与 IP 指纹，不额外展开异常堆栈', async () => {
    const row = { ...log('01912345-6789-7abc-8def-0123456789ab', 'failed'),
      userId: '01912345-6789-7abc-8def-0123456789ac',
      tenantId: '01912345-6789-7abc-8def-0123456789ad',
      clientIpFingerprint: 'fp-123', stackTrace: 'secret-stack' };
    listMock.mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 });
    const wrapper = mount(ExceptionLogsView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    wrapper.findComponent({ name: 'ElTable' }).vm.$emit('row-click', row);
    await flushPromises();
    const fields = wrapper.findComponent(AuditLogDetailDrawer).props('record')?.fields;
    expect(fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ value: row.userId }),
      expect.objectContaining({ value: row.tenantId }),
      expect.objectContaining({ value: row.clientIpFingerprint })
    ]));
    expect(fields?.map(field => field.value)).not.toContain('secret-stack');
    wrapper.unmount();
  });
});
