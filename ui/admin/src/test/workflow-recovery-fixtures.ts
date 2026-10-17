import type { WorkflowRecoveryTaskResponse } from '../api/workflow-recovery-tasks';
export const recoveryTask: WorkflowRecoveryTaskResponse = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411203', instanceId: '0198f36e-f7a7-7c52-9cbb-774e67411204', stepId: null,
  kindKey: 'stuck_instance', statusKey: 'failed', attemptCount: 3, revision: 2, leaseOwnerKey: null,
  leaseExpiresAtUtc: null, leaseGeneration: 1, nextAttemptAtUtc: null, lastError: 'fixture.failure',
  createdAtUtc: '2026-09-05T00:00:00Z', updatedAtUtc: '2026-09-05T00:00:01Z'
};
export const recoveryPage = (items: WorkflowRecoveryTaskResponse[] = [recoveryTask], page = 1, pageSize = 20, total = items.length) => ({ items, page, pageSize, total });
export const recoveryRetried = { ...recoveryTask, statusKey: 'pending', revision: 3, attemptCount: 0, lastError: null };
