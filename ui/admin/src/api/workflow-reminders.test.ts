import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listWorkflowReminderTargets, requestWorkflowReminder } from './workflow-reminders';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '01912345-6789-7abc-8def-0123456789ab';
const body = { stepId: id, expectedInstanceRevision: 1, expectedStepRevision: 2, idempotencyKey: 'request-one' };
beforeEach(() => vi.clearAllMocks());
describe('人工催办资源边界', () => {
  it('禁止客户端夹带接收人、租户或身份', async () => {
    for (const extra of ['recipientUserId', 'tenantId', 'actorUserId']) await expect(requestWorkflowReminder(id, { ...body, [extra]: id })).rejects.toThrow();
    expect(http.request).not.toHaveBeenCalled();
  });
  it('只接受匹配实际节点和并发修订的受理回执', async () => {
    const receipt = { requestId: id, stepId: id, instanceRevision: 1, stepRevision: 2, manualSequence: 1, recipientCount: 2, acceptedAtUtc: '2026-10-25T00:00:00Z' };
    vi.mocked(http.request).mockResolvedValue(receipt);
    expect(await requestWorkflowReminder(id, body)).toEqual(receipt);
    vi.mocked(http.request).mockResolvedValue({ ...receipt, stepRevision: 3 });
    await expect(requestWorkflowReminder(id, body)).rejects.toThrow();
  });
  it('拒绝重复节点和错误限额投影', async () => {
    const item = { stepId: id, nodeKey: 'review', nodeTypeKey: 'human.task', stepRevision: 1, activeTodoCount: 2, manualCount: 0, lastRequestedAtUtc: null, canRemind: true };
    vi.mocked(http.request).mockResolvedValue({ instanceRevision: 1, items: [item] });
    expect((await listWorkflowReminderTargets(id)).items).toHaveLength(1);
    vi.mocked(http.request).mockResolvedValue({ instanceRevision: 1, items: [item, item] });
    await expect(listWorkflowReminderTargets(id)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValue({ instanceRevision: 1, items: [{ ...item, manualCount: 20, lastRequestedAtUtc: '2026-10-25T00:00:00Z' }] });
    await expect(listWorkflowReminderTargets(id)).rejects.toThrow();
  });
});
