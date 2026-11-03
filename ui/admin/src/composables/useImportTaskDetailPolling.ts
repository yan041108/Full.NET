import type { ImportExportTaskDetailResponse } from '@fullnet/client-contracts';
import { useBoundedPolling } from './useBoundedPolling';

/** 导入详情只读取排队/执行状态，保留任务身份及租户的独立响应校验。 */
export function useImportTaskDetailPolling(options: {
  enabled: () => boolean;
  task: () => ImportExportTaskDetailResponse | undefined;
  read: (id: string, signal: AbortSignal) => Promise<ImportExportTaskDetailResponse>;
  accept: (task: ImportExportTaskDetailResponse) => void;
  failed: (error: unknown) => void;
}) {
  return useBoundedPolling({
    enabled: options.enabled,
    pending: () => ['queued', 'executing'].includes(options.task()?.statusKey ?? ''),
    key: () => `${options.task()?.tenantId ?? ''}|${options.task()?.id ?? ''}`,
    read: signal => options.read(options.task()!.id, signal),
    accept: result => {
      const source = options.task();
      if (!source || result.id !== source.id || result.tenantId !== source.tenantId) throw new Error('client.invalid_import_export_task_scope');
      options.accept(result);
    },
    failed: options.failed
  });
}
