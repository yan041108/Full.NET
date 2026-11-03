import { readWorkflowInstanceResponse, type WorkflowInstanceResponse } from '@fullnet/client-contracts';
import type { HttpClient } from '../../api/http';
import { readWorkflowResourceTarget } from './workflow-resource-links';

/** 实例深链始终读取当前会话的权威资源；通知中的状态不作为执行许可。 */
export function createWorkflowInstanceClient(http: HttpClient) {
  return {
    async get(instanceId: string): Promise<WorkflowInstanceResponse> {
      const target = readWorkflowResourceTarget({ resourceTypeKey: 'workflow_instance', resourceId: instanceId });
      if (!target) throw new TypeError('workflow.resource.invalid');
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/instances/${target.resourceId}` }));
    }
  };
}
