import {
  documentHostCreateDocumentPreviewTask,
  documentHostDownloadDocumentPreviewTaskContent,
  documentHostGetDocumentPreviewTask,
  documentHostListDocumentPreviewTasks,
  isCreateHostDocumentPreviewTaskRequest,
  isHostDocumentPreviewTaskPage,
  isHostDocumentPreviewTaskResponse,
  type CreateHostDocumentPreviewTaskRequest,
  type HostDocumentPreviewTaskPage,
  type HostDocumentPreviewTaskResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { openDocumentBlob } from './host-document-items';

/** 分页查询 Office 预览转换任务。 */
export async function listDocumentPreviewTasks(
  page = 1,
  pageSize = 20,
  documentItemId?: string,
  signal?: AbortSignal
): Promise<HostDocumentPreviewTaskPage> {
  const value = await documentHostListDocumentPreviewTasks(
    http,
    {
      page,
      pageSize,
      documentItemId
    },
    signal
  );
  if (!isHostDocumentPreviewTaskPage(value)) {
    throw new Error('client.invalid_document_preview_task_page');
  }
  return value;
}

/** 创建 Office 预览转换任务。 */
export async function createDocumentPreviewTask(
  req: CreateHostDocumentPreviewTaskRequest,
  signal?: AbortSignal
): Promise<HostDocumentPreviewTaskResponse> {
  if (!isCreateHostDocumentPreviewTaskRequest(req)) {
    throw new Error('client.invalid_create_document_preview_task_request');
  }
  const value = await documentHostCreateDocumentPreviewTask(http, {
    body: { ...req, versionId: req.versionId ?? null }
  }, signal);
  if (!isHostDocumentPreviewTaskResponse(value)) {
    throw new Error('client.invalid_document_preview_task');
  }
  return value;
}

/** 读取单条预览转换任务详情。 */
export async function getDocumentPreviewTask(
  taskId: string,
  signal?: AbortSignal
): Promise<HostDocumentPreviewTaskResponse> {
  const value = await documentHostGetDocumentPreviewTask(http, { taskId }, signal);
  if (!isHostDocumentPreviewTaskResponse(value)) {
    throw new Error('client.invalid_document_preview_task');
  }
  return value;
}

/** 下载并打开已成功转换的 PDF 预览内容。 */
export async function openDocumentPreviewTaskContent(
  taskId: string,
  signal?: AbortSignal
): Promise<void> {
  const blob = await documentHostDownloadDocumentPreviewTaskContent(http, { taskId }, signal);
  openDocumentBlob(blob);
}

export type {
  CreateHostDocumentPreviewTaskRequest,
  HostDocumentPreviewTaskPage,
  HostDocumentPreviewTaskResponse
};
