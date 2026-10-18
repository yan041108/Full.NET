import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ElTabs } from 'element-plus';
import { createPinia, setActivePinia } from 'pinia';
import AuditLogDetailDrawer from './AuditLogDetailDrawer.vue';
import { useSessionStore } from '../../auth/session';
import { getAuditingOperationLogDetails } from '../../api/operation-logs';

vi.mock('../../api/operation-logs', () => ({ getAuditingOperationLogDetails: vi.fn() }));
const getDetails = vi.mocked(getAuditingOperationLogDetails);
const id = '01912345-6789-7abc-8def-0123456789ab';

function user(permissions: string[]) {
  return {
    id, username: 'reviewer', displayName: '审查员', tenantId: null,
    actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions, sessionId: id,
    preferredLocale: 'zh-CN' as const, profileVersion: 1
  };
}

describe('受限操作详情实时权限边界', () => {
  beforeEach(() => getDetails.mockReset());

  it('只有详情权限而没有普通读取权限时不展示页签或发请求', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.state = 'authenticated';
    session.currentUser = user(['auditing.operations.details.read']);
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: {
        id, occurredAtUtc: '2026-09-29T00:00:00Z', title: 'operation',
        supportsDiff: false, supportsRestrictedDetails: true, fields: []
      } },
      global: { plugins: [pinia] }, attachTo: document.body
    });
    await flushPromises();
    expect(document.body.textContent).not.toContain('请求参数');
    expect(getDetails).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it('会话撤销详情权限时移除已展示的受限字段', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.state = 'authenticated';
    session.currentUser = user(['auditing.operations.read', 'auditing.operations.details.read']);
    getDetails.mockResolvedValueOnce({
      id, detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
      context: { schemaVersion: 1, clientIp: '203.0.113.42', clientPort: 54321,
        serverIp: null, serverPort: null, requestCaptureState: 'not_applicable',
        requestSummary: null, responseCaptureState: 'not_applicable', responseSummary: null }
    });
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: {
        id, occurredAtUtc: '2026-09-29T00:00:00Z', title: 'operation',
        supportsDiff: false, supportsRestrictedDetails: true, fields: []
      } },
      global: { plugins: [pinia] }, attachTo: document.body
    });
    await flushPromises();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'request');
    await flushPromises();
    expect(document.body.textContent).toContain('203.0.113.42');

    session.state = 'authenticated';
    session.currentUser = user(['auditing.operations.read']);
    await flushPromises();
    expect(document.body.textContent).not.toContain('203.0.113.42');
    expect(document.body.textContent).not.toContain('请求参数');
    wrapper.unmount();
  });

  it('在途详情请求遇到权限撤销时取消且忽略迟到数据', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.state = 'authenticated';
    session.currentUser = user(['auditing.operations.read', 'auditing.operations.details.read']);
    let resolve!: (value: Awaited<ReturnType<typeof getAuditingOperationLogDetails>>) => void;
    getDetails.mockImplementationOnce(() => new Promise(value => { resolve = value; }));
    const wrapper = mount(AuditLogDetailDrawer, {
      props: { modelValue: true, record: {
        id, occurredAtUtc: '2026-09-29T00:00:00Z', title: 'operation',
        supportsDiff: false, supportsRestrictedDetails: true, fields: []
      } },
      global: { plugins: [pinia] }, attachTo: document.body
    });
    await flushPromises();
    wrapper.findComponent(ElTabs).vm.$emit('update:modelValue', 'request');
    await flushPromises();
    const signal = getDetails.mock.calls[0]?.[1];
    expect(signal?.aborted).toBe(false);

    session.state = 'authenticated';
    session.currentUser = user(['auditing.operations.read']);
    await flushPromises();
    expect(signal?.aborted).toBe(true);
    resolve({ id, detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
      context: { schemaVersion: 1, clientIp: '203.0.113.42', clientPort: null,
        serverIp: null, serverPort: null, requestCaptureState: 'not_applicable',
        requestSummary: null, responseCaptureState: 'not_applicable', responseSummary: null } });
    await flushPromises();
    expect(document.body.textContent).not.toContain('203.0.113.42');
    wrapper.unmount();
  });
});
