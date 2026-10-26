import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getWorkflowConnectorCall, listWorkflowConnectors, listWorkflowConnectorCalls, reconcileWorkflowConnector } from './workflow-connectors';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019a23f0-0000-7000-8000-000000000001'; const callId = '019a23f0-0000-7000-8000-000000000002';
const call = { id: callId, instanceId: id, nodeKey: 'call', connectorKey: 'credit.check', connectorVersion: 1, modeKey: 'sync', statusKey: 'outcome_unknown', revision: 3,
  attemptCount: 1, applyAttemptCount: 0, errorKey: null, inputFormRevision: 1, appliedFormRevision: null, cancellationRequested: false, requiresReconciliation: true,
  createdAtUtc: '2026-10-26T00:00:00Z', completedAtUtc: null, attempts: [] };
const request = { expectedInstanceRevision: 9, expectedRevision: 3, actionKey: 'retry', attemptId: null, evidenceReference: 'ticket:1', reason: 'owner verified', idempotencyKey: 'intent.1' };
beforeEach(() => vi.clearAllMocks());
describe('连接器资源边界', () => {
  it('只读取当前作用域的受控目录且拒绝重复版本', async () => {
    const entry = { connectorKey: 'credit.check', connectorVersion: 1, supportsIdempotency: true, inputParameters: [], outputParameters: [] };
    vi.mocked(http.request).mockResolvedValue({ items: [entry] }); expect((await listWorkflowConnectors()).items).toHaveLength(1);
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/workflow/definitions/connectors', expect.objectContaining({ method: 'GET' }), undefined);
    vi.mocked(http.request).mockResolvedValue({ items: [entry, entry] }); await expect(listWorkflowConnectors()).rejects.toThrow();
  });
  it('读取绑定父实例、实际活动及20条分页', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [call], page: 2, pageSize: 20, total: 21 }); expect((await listWorkflowConnectorCalls(id, 2)).items).toHaveLength(1);
    vi.mocked(http.request).mockResolvedValue({ ...call, instanceId: callId }); await expect(getWorkflowConnectorCall(id, callId)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValue({ ...call, statusKey: 'invented' }); await expect(getWorkflowConnectorCall(id, callId)).rejects.toThrow();
  });
  it('对账不能夹带身份、租户、载荷或伪造尝试', async () => {
    for (const name of ['actorUserId', 'tenantId', 'responseJson']) await expect(reconcileWorkflowConnector(id, callId, { ...request, [name]: id })).rejects.toThrow();
    await expect(reconcileWorkflowConnector(id, callId, { ...request, actionKey: 'accept_result' })).rejects.toThrow();
    await expect(reconcileWorkflowConnector(id, callId, { ...request, evidenceReference: '' })).rejects.toThrow(); expect(http.request).not.toHaveBeenCalled();
  });
  it('接纳真实不可变回执，拒绝替换活动标识', async () => {
    const receipt = { id, callId, instanceId: id, actionKey: 'retry', statusKey: 'pending', revision: 4, instanceStatusKey: 'suspended', instanceRevision: 10, createdAtUtc: '2026-10-26T00:00:00Z' };
    vi.mocked(http.request).mockResolvedValue(receipt); expect(await reconcileWorkflowConnector(id, callId, request)).toEqual(receipt);
    vi.mocked(http.request).mockResolvedValue({ ...receipt, callId: id }); await expect(reconcileWorkflowConnector(id, callId, request)).rejects.toThrow();
  });
});
