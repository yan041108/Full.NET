import { beforeEach, describe, expect, it, vi } from 'vitest';
import { workflowGetRouteDecision, workflowReconcileRouteDecision } from '@fullnet/client-contracts';
import { getWorkflowRouteDecision, reconcileWorkflowRoute } from './workflow-routes';
vi.mock('@fullnet/client-contracts', async importOriginal => ({ ...await importOriginal<object>(), workflowGetRouteDecision: vi.fn(), workflowReconcileRouteDecision: vi.fn() }));
const root = '11111111-1111-4111-8111-111111111111'; const route = '22222222-2222-4222-8222-222222222222';
const work = '33333333-3333-4333-8333-333333333333';
const row = { id: route, instanceId: root, nodeKey: 'route', statusKey: 'decision_unknown', revision: 1, instanceRevision: 9, instanceStatusKey: 'suspended',
  sourceExecutionEpochId: root, targetExecutionEpochId: null, selectedTargetKey: null, nextNodeKey: null, continuationWorkId: null, normalNextNodeKey: 'end',
  targets: [{ targetKey: 'retry', nodeKey: 'review', regionKey: 'round', reenters: true, maximumIterations: 3 }], createdAtUtc: '2026-10-27T00:00:00Z', appliedAtUtc: null };
beforeEach(() => { vi.clearAllMocks(); });
describe('路由协议读取与发送边界', () => {
  it('拒绝跨实例或未闭合的后继投影', async () => {
    vi.mocked(workflowGetRouteDecision).mockResolvedValueOnce({ ...row, instanceId: work });
    await expect(getWorkflowRouteDecision(root, route)).rejects.toThrow();
    vi.mocked(workflowGetRouteDecision).mockResolvedValueOnce({ ...row, statusKey: 'applied' });
    await expect(getWorkflowRouteDecision(root, route)).rejects.toThrow();
    vi.mocked(workflowGetRouteDecision).mockResolvedValueOnce(row);
    await expect(getWorkflowRouteDecision(root, route)).resolves.toEqual(row);
  });
  it('拒绝客户端注入租户、代次或缺失修订，不发送请求', async () => {
    const body = { expectedInstanceRevision: 9, expectedRevision: 1, selectedTargetKey: null, reason: '已核对', idempotencyKey: 'fixed-intent' };
    await expect(reconcileWorkflowRoute(root, route, { ...body, tenantId: work } as typeof body)).rejects.toThrow();
    await expect(reconcileWorkflowRoute(root, route, { ...body, expectedRevision: 0 })).rejects.toThrow();
    expect(workflowReconcileRouteDecision).not.toHaveBeenCalled();
    vi.mocked(workflowReconcileRouteDecision).mockResolvedValue({ routeId: route, continuationWorkId: work, executionEpochId: root, nextNodeKey: 'end' });
    await reconcileWorkflowRoute(root, route, body);
    expect(vi.mocked(workflowReconcileRouteDecision).mock.calls[0]?.[1]).toEqual({ instanceId: root, routeId: route, body });
  });
});
