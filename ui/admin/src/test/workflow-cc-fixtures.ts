import type { WorkflowCcResponse } from '../api/workflow-cc';
export const workflowCcRecord: WorkflowCcResponse = {
  id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001', instanceId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002', stepId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003',
  nodeKey: 'copy', businessTitle: '申请单', businessType: 'demo.enterprise_request', businessId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d004', createdAtUtc: '2026-09-04T00:00:00Z', readAtUtc: null
};
export const workflowCcReceipt = { id: workflowCcRecord.id, readAtUtc: '2026-09-04T00:01:00Z' };
