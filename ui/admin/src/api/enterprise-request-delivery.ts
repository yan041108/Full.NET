import {
  enterpriseRequestListAttachments, enterpriseRequestUploadAttachment, enterpriseRequestDeleteAttachment,
  enterpriseRequestDownloadAttachment, enterpriseRequestListReports, enterpriseRequestCreateReport,
  enterpriseRequestCancelReport, enterpriseRequestDownloadReport, enterpriseRequestPrintDocument,
  enterpriseRequestGetEnterpriseRequest
} from '@fullnet/client-contracts';
import { http } from './http';

// 文件、版本和任务身份使用生成的协议与响应守卫，不接受客户端覆盖主体或租户。
export const deliveryRequest = (enterpriseRequestId: string, signal?: AbortSignal) => enterpriseRequestGetEnterpriseRequest(http, { enterpriseRequestId }, signal);
export const deliveryAttachments = (requestId: string, page: number, signal?: AbortSignal) => enterpriseRequestListAttachments(http, { requestId, page, pageSize: 20 }, signal);
export const deliveryUpload = (requestId: string, idempotencyKey: string, requestVersion: string, file: File, signal?: AbortSignal) => enterpriseRequestUploadAttachment(http, { requestId, idempotencyKey, requestVersion, file }, signal);
export const deliveryDelete = (requestId: string, attachmentId: string, requestVersion: string, attachmentVersion: string, signal?: AbortSignal) => enterpriseRequestDeleteAttachment(http, { requestId, attachmentId, body: { requestVersion, attachmentVersion } }, signal);
export const deliveryDownloadAttachment = (requestId: string, attachmentId: string, signal?: AbortSignal) => enterpriseRequestDownloadAttachment(http, { requestId, attachmentId }, signal);
export const deliveryReports = (requestId: string, page: number, signal?: AbortSignal) => enterpriseRequestListReports(http, { requestId, page, pageSize: 20 }, signal);
export const deliveryCreateReport = (requestId: string, idempotencyKey: string, requestVersion: string, signal?: AbortSignal) => enterpriseRequestCreateReport(http, { requestId, body: { idempotencyKey, requestVersion } }, signal);
export const deliveryCancelReport = (requestId: string, reportId: string, version: string, signal?: AbortSignal) => enterpriseRequestCancelReport(http, { requestId, reportId, body: { version } }, signal);
export const deliveryDownloadReport = (requestId: string, reportId: string, signal?: AbortSignal) => enterpriseRequestDownloadReport(http, { requestId, reportId }, signal);
export const deliveryPrint = (requestId: string, signal?: AbortSignal) => enterpriseRequestPrintDocument(http, { requestId }, signal);
