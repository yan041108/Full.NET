import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ElTabs } from 'element-plus';
import { createPinia } from 'pinia';
import type { AuditingDomainChangeDiffQueryResult } from '@fullnet/client-contracts';
import AuditLogDetailDrawer, { type AuditLogDetailRecord } from './AuditLogDetailDrawer.vue';
import { queryDomainChangeDiffs } from '../../api/auditing-analytics';
import { getAuditingOperationLogDetails } from '../../api/operation-logs';

vi.mock('../../api/auditing-analytics', () => ({ queryDomainChangeDiffs: vi.fn() }));
vi.mock('../../api/operation-logs', () => ({ getAuditingOperationLogDetails: vi.fn() }));
const { canMock } = vi.hoisted(() => ({ canMock: vi.fn<(code: string) => boolean>() }));
vi.mock('../../auth/permission', () => ({ usePermission: () => ({ can: canMock }) }));

const queryDiffs = vi.mocked(queryDomainChangeDiffs);
const getDetails = vi.mocked(getAuditingOperationLogDetails);

function record(id: string, traceId: string): AuditLogDetailRecord {
  return { id, occurredAtUtc: '2026-09-29T00:00:00Z', traceId, title: id, fields: [] };
}

function result(traceId: string, actionKey: string): AuditingDomainChangeDiffQueryResult {
  return {
    traceId,
    entries: [{
      auditId: '01912345-6789-7abc-8def-0123456789ab',
      moduleKey: 'orders', actionKey,
      occurredAtUtc: '2026-09-29T00:00:00Z',
      availability: 'no_diff_recorded', fields: []
    }]
  };
}

describe('日志详情差异请求', () => {
  beforeEach(() => {
    queryDiffs.mockReset();
    getDetails.mockReset();
    canMock.mockReset();
    canMock.mockReturnValue(false);
  });

  it('没有受限详情权限时不显示请求和返回页签，也不发详情请求', async () => {
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: { ...record('one', ''), supportsRestrictedDetails: true } },
      global: { plugins: [createPinia()] }, attachTo: document.body
    });
    await flushPromises();
    expect(document.body.textContent).not.toContain('请求参数');
    expect(document.body.textContent).not.toContain('返回内容');
    expect(getDetails).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it('有双重权限时按需读取受控请求摘要，切换记录取消旧请求', async () => {
    canMock.mockReturnValue(true);
    let resolveOld!: (value: Awaited<ReturnType<typeof getAuditingOperationLogDetails>>) => void;
    getDetails
      .mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce({
        id: 'new', detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
        context: { schemaVersion: 1, clientIp: null, clientPort: null,
          serverIp: null, serverPort: null, requestCaptureState: 'captured',
          requestSummary: { fromUtc: '2026-09-29T00:00:00Z', toUtc: '2026-09-29T01:00:00Z',
            statusCode: 200, succeeded: true }, responseCaptureState: 'not_applicable',
          responseSummary: null }
      });
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: { ...record('old', ''), supportsRestrictedDetails: true } },
      global: { plugins: [createPinia()] }, attachTo: document.body
    });
    await flushPromises();
    expect(getDetails).not.toHaveBeenCalled();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'request');
    await flushPromises();
    expect(getDetails).toHaveBeenCalledWith('old', expect.any(AbortSignal));

    await wrapper.setProps({ record: { ...record('new', ''), supportsRestrictedDetails: true } });
    await flushPromises();
    expect(getDetails.mock.calls[0]?.[1]?.aborted).toBe(true);
    expect(getDetails).toHaveBeenCalledWith('new', expect.any(AbortSignal));
    expect(document.body.textContent).toContain('2026-09-29T00:00:00Z');

    resolveOld({ id: 'old', detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
      context: { schemaVersion: 1, clientIp: null, clientPort: null,
        serverIp: null, serverPort: null, requestCaptureState: 'not_applicable',
        requestSummary: null, responseCaptureState: 'not_applicable', responseSummary: null } });
    await flushPromises();
    expect(document.body.textContent).toContain('2026-09-29T00:00:00Z');
    wrapper.unmount();
  });

  it('旧记录或到期详情返回 404 时显示不可用状态', async () => {
    canMock.mockReturnValue(true);
    getDetails.mockRejectedValueOnce({
      status: 404, code: 'auditing.operation_log_not_found', title: 'Not found'
    });
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: { ...record('old', ''), supportsRestrictedDetails: true } },
      global: { plugins: [createPinia()] }, attachTo: document.body
    });
    await flushPromises();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'response');
    await flushPromises();
    expect(document.body.textContent).toContain('详情未记录或已到期');
    wrapper.unmount();
  });

  it('同一记录的请求与返回页签复用已取得的受限详情', async () => {
    canMock.mockReturnValue(true);
    getDetails.mockResolvedValueOnce({
      id: 'one', detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
      context: { schemaVersion: 1, clientIp: null, clientPort: null,
        serverIp: null, serverPort: null, requestCaptureState: 'not_applicable',
        requestSummary: null, responseCaptureState: 'captured',
        responseSummary: { rowCount: 23, truncated: false, includesSensitiveFields: false } }
    });
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: { ...record('one', ''), supportsRestrictedDetails: true } },
      global: { plugins: [createPinia()] }, attachTo: document.body
    });
    await flushPromises();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'request');
    await flushPromises();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'response');
    await flushPromises();
    expect(getDetails).toHaveBeenCalledTimes(1);
    expect(document.body.textContent).toContain('23');
    wrapper.unmount();
  });

  it('切换日志后忽略旧 TraceId 的迟到结果', async () => {
    let resolveOld!: (value: AuditingDomainChangeDiffQueryResult) => void;
    queryDiffs
      .mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce(result('new-trace', 'new-action'));

    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: record('old', 'old-trace') },
      global: { plugins: [createPinia()] }, attachTo: document.body
    });
    await flushPromises();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'diff');
    await flushPromises();
    expect(queryDiffs).toHaveBeenCalledWith('old-trace', expect.any(AbortSignal));

    await wrapper.setProps({ record: record('new', 'new-trace') });
    await flushPromises();
    expect(queryDiffs.mock.calls[0]?.[1]?.aborted).toBe(true);
    expect(queryDiffs).toHaveBeenCalledWith('new-trace', expect.any(AbortSignal));
    expect(document.body.textContent).toContain('new-action');

    resolveOld(result('old-trace', 'old-action'));
    await flushPromises();
    expect(document.body.textContent).toContain('new-action');
    expect(document.body.textContent).not.toContain('old-action');
    wrapper.unmount();
  });

});
