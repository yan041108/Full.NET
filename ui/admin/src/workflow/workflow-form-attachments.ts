import { http } from '../api/http';
import { workflowDownloadTodoFormAttachment, workflowUploadStartFormAttachment, workflowUploadTodoFormAttachment,
  type WorkflowFormAttachmentUploadResponse } from '@fullnet/client-contracts';

export type WorkflowAttachmentUploadContext = { kind: 'start'; versionId: string }
  | { kind: 'todo'; todoId: string; expectedRevision: number; formKindKey: 'main' | 'vote' };

/** 资源与字段由当前页面绑定，上传不再固定到Host文件目录。 */
export async function uploadWorkflowFormAttachment(
  file: File,
  fieldKey: string,
  context: WorkflowAttachmentUploadContext,
  signal?: AbortSignal
): Promise<WorkflowFormAttachmentUploadResponse> {
  return context.kind === 'start'
    ? workflowUploadStartFormAttachment(http, { versionId: context.versionId, fieldKey, file }, signal)
    : workflowUploadTodoFormAttachment(http, { todoId: context.todoId, expectedRevision: context.expectedRevision,
      fieldKey, formKindKey: context.formKindKey, file }, signal);
}

/** 通过工作流实例上下文下载附件内容。 */
export async function downloadWorkflowFormAttachment(
  instanceId: string,
  fileId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return http.requestBlob(
    `/api/v1/workflow/instances/${instanceId}/form-attachments/${fileId}/content`,
    { method: 'GET' },
    signal
  );
}

/** 个人办理使用精确 Todo 路径，由服务端核对本次节点和私有选票视图。 */
export async function downloadWorkflowTodoFormAttachment(todoId: string, fileId: string, signal?: AbortSignal): Promise<Blob> {
  return workflowDownloadTodoFormAttachment(http, { todoId, fileId }, signal);
}

/** 以下载方式交付附件，不能在后台同源Blob窗口执行外来HTML/SVG等主动内容。 */
export function openWorkflowFormAttachmentBlob(blob: Blob, name = 'attachment'): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a'); link.href = url;
  link.download = name.replace(/[\\/\u0000-\u001f\u007f]/g, '_');
  document.body.append(link);
  try { link.click(); } finally { link.remove(); window.setTimeout(() => URL.revokeObjectURL(url), 1_000); }
}
