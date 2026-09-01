import {
  isHostJobDefinition,
  isHostJobDefinitionPage,
  isHostJobExecution,
  isHostJobExecutionPage,
  isHostJobGroupList,
  JOB_HANDLER_KINDS,
  jobsClearHostJobExecutions,
  jobsCreateHostJobDefinition,
  jobsDeleteHostJobDefinition,
  jobsDisableHostJobDefinition,
  jobsGetHostJobExecution,
  jobsListHostJobDefinitions,
  jobsListHostJobExecutions,
  jobsListHostJobGroups,
  jobsTriggerHostJobDefinition,
  jobsUpdateHostJobDefinition,
  type HostJobDefinition,
  type HostJobDefinitionPage,
  type HostJobExecution,
  type HostJobExecutionListQuery,
  type HostJobExecutionPage,
  type HostJobGroup,
  type HttpJobArgs
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询 Host 作业定义列表，并对响应页做失败关闭校验。 */
export async function listHostJobDefinitions(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostJobDefinitionPage> {
  const value = await jobsListHostJobDefinitions(
    http,
    { page, pageSize },
    signal
  );
  if (!isHostJobDefinitionPage(value)) {
    throw new Error('client.invalid_host_job_definition_page');
  }

  return value;
}

/** 查询 Host 作业分组目录。 */
export async function listHostJobGroups(
  signal?: AbortSignal
): Promise<HostJobGroup[]> {
  const value = await jobsListHostJobGroups(http, {}, signal);
  if (!isHostJobGroupList(value)) {
    throw new Error('client.invalid_host_job_group_list');
  }

  return value;
}

/** 创建 Host 作业定义，并允许声明初始处理器参数与分组信息。 */
export async function createHostJobDefinition(
  jobKey: string,
  handlerKind: string,
  displayName: string,
  args?: HttpJobArgs | null,
  description?: string | null,
  groupName?: string | null,
  allowConcurrentExecutions = false,
  signal?: AbortSignal
): Promise<HostJobDefinition> {
  const value = await jobsCreateHostJobDefinition(
    http,
    {
      body: {
        jobKey,
        handlerKind,
        args: args ?? null,
        displayName,
        description: description ?? null,
        groupName: groupName ?? null,
        allowConcurrentExecutions
      }
    },
    signal
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('client.invalid_host_job_definition');
  }

  return value;
}

/** 更新 Host 作业定义，并携带版本号维持乐观并发。 */
export async function updateHostJobDefinition(
  id: string,
  displayName: string,
  description: string | null,
  handlerKind: string,
  args: HttpJobArgs | null,
  version: number,
  groupName?: string | null,
  allowConcurrentExecutions = false,
  signal?: AbortSignal
): Promise<HostJobDefinition> {
  const value = await jobsUpdateHostJobDefinition(
    http,
    {
      definitionId: id,
      body: {
        displayName,
        description,
        handlerKind,
        args,
        groupName: groupName ?? null,
        allowConcurrentExecutions,
        version
      }
    },
    signal
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('client.invalid_host_job_definition');
  }

  return value;
}

/** 禁用 Host 作业定义。 */
export async function disableHostJobDefinition(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostJobDefinition> {
  const value = await jobsDisableHostJobDefinition(
    http,
    {
      definitionId: id,
      body: { version }
    },
    signal
  );
  if (!isHostJobDefinition(value)) {
    throw new Error('client.invalid_host_job_definition');
  }

  return value;
}

/** 删除 Host 作业定义。 */
export async function deleteHostJobDefinition(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await jobsDeleteHostJobDefinition(
    http,
    {
      definitionId: id,
      body: { version }
    },
    signal
  );
}

/** 立即触发一次 Host 作业定义执行。 */
export async function triggerHostJobDefinition(
  id: string,
  signal?: AbortSignal
): Promise<HostJobExecution> {
  const value = await jobsTriggerHostJobDefinition(
    http,
    { definitionId: id },
    signal
  );
  if (!isHostJobExecution(value)) {
    throw new Error('client.invalid_host_job_execution');
  }

  return value;
}

/** 分页查询 Host 作业执行记录。 */
export async function listHostJobExecutions(
  query: HostJobExecutionListQuery = {},
  signal?: AbortSignal
): Promise<HostJobExecutionPage> {
  const value = await jobsListHostJobExecutions(
    http,
    {
      page: query.page ?? 1,
      pageSize: query.pageSize ?? 20,
      jobDefinitionId: query.jobDefinitionId,
      jobScheduleId: query.jobScheduleId,
      status: query.status,
      fromUtc: query.fromUtc,
      toUtc: query.toUtc
    },
    signal
  );
  if (!isHostJobExecutionPage(value)) {
    throw new Error('client.invalid_host_job_execution_page');
  }

  return value;
}

/** 查询单条 Host 作业执行详情。 */
export async function getHostJobExecution(
  id: string,
  signal?: AbortSignal
): Promise<HostJobExecution> {
  const value = await jobsGetHostJobExecution(
    http,
    { executionId: id },
    signal
  );
  if (!isHostJobExecution(value)) {
    throw new Error('client.invalid_host_job_execution');
  }

  return value;
}

/** 清空指定作业定义的历史执行记录。 */
export async function clearHostJobExecutions(
  jobDefinitionId: string,
  signal?: AbortSignal
): Promise<void> {
  await jobsClearHostJobExecutions(
    http,
    { jobDefinitionId },
    signal
  );
}

/** 透传作业处理器种类目录，供定义编辑页限制可选处理器并保持与服务端枚举对齐。 */
export { JOB_HANDLER_KINDS };

/** 导出作业定义、执行记录、筛选条件与处理器参数模型，供定义页与执行审计页共享同一契约。 */
export type {
  HostJobDefinition,
  HostJobDefinitionPage,
  HostJobExecution,
  HostJobExecutionListQuery,
  HostJobExecutionPage,
  HostJobGroup,
  HttpJobArgs
};
