import { uploadHostFile } from '../api/host-files';
import { http } from '../api/http';
import { workflowDownloadTodoFormAttachment } from '@fullnet/client-contracts';

/** 上传工作流表单附件，返回 Host 文件标识。 */
export async function uploadWorkflowFormAttachment(
  file: File,
  signal?: AbortSignal
): Promise<string> {
  const uploaded = await uploadHostFile(file, undefined, signal);
  return uploaded.id;
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

/** 打开工作流实例附件内容。 */
export function openWorkflowFormAttachmentBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const opened = window.open(url, '_blank', 'noopener,noreferrer');
  if (!opened) {
    URL.revokeObjectURL(url);
    return;
  }

  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
