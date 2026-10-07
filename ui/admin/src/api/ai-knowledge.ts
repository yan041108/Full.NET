import {
  aiListKnowledgeBases, type AiListKnowledgeBasesParameters,
  aiGetKnowledgeBase, type AiGetKnowledgeBaseParameters,
  aiCreateKnowledgeBase, type AiCreateKnowledgeBaseParameters,
  aiUpdateKnowledgeBase, type AiUpdateKnowledgeBaseParameters,
  aiListKnowledgeDocuments, type AiListKnowledgeDocumentsParameters,
  aiGetKnowledgeDocument, type AiGetKnowledgeDocumentParameters,
  aiCreateKnowledgeDocument, type AiCreateKnowledgeDocumentParameters,
  aiUpdateKnowledgeDocument, type AiUpdateKnowledgeDocumentParameters,
  aiDeleteKnowledgeDocument, type AiDeleteKnowledgeDocumentParameters,
  aiListKnowledgeVersions, type AiListKnowledgeVersionsParameters,
  aiUploadKnowledgeVersion, type AiUploadKnowledgeVersionParameters,
  aiGetKnowledgeImport, type AiGetKnowledgeImportParameters,
  aiRetryKnowledgeImport, type AiRetryKnowledgeImportParameters,
  aiDownloadKnowledgeSource, type AiDownloadKnowledgeSourceParameters,
} from '@fullnet/client-contracts';
import { http } from './http';

// 页面适配只绑定共享 HTTP 客户端，响应验证和协议路径由生成操作负责。
export function listKnowledgeBases(parameters: AiListKnowledgeBasesParameters = {}, signal?: AbortSignal) {
  return aiListKnowledgeBases(http, parameters, signal);
}

export function getKnowledgeBase(parameters: AiGetKnowledgeBaseParameters, signal?: AbortSignal) {
  return aiGetKnowledgeBase(http, parameters, signal);
}

export function createKnowledgeBase(parameters: AiCreateKnowledgeBaseParameters, signal?: AbortSignal) {
  return aiCreateKnowledgeBase(http, parameters, signal);
}

export function updateKnowledgeBase(parameters: AiUpdateKnowledgeBaseParameters, signal?: AbortSignal) {
  return aiUpdateKnowledgeBase(http, parameters, signal);
}

export function listKnowledgeDocuments(parameters: AiListKnowledgeDocumentsParameters, signal?: AbortSignal) {
  return aiListKnowledgeDocuments(http, parameters, signal);
}

export function getKnowledgeDocument(parameters: AiGetKnowledgeDocumentParameters, signal?: AbortSignal) {
  return aiGetKnowledgeDocument(http, parameters, signal);
}

export function createKnowledgeDocument(parameters: AiCreateKnowledgeDocumentParameters, signal?: AbortSignal) {
  return aiCreateKnowledgeDocument(http, parameters, signal);
}

export function updateKnowledgeDocument(parameters: AiUpdateKnowledgeDocumentParameters, signal?: AbortSignal) {
  return aiUpdateKnowledgeDocument(http, parameters, signal);
}

export function deleteKnowledgeDocument(parameters: AiDeleteKnowledgeDocumentParameters, signal?: AbortSignal) {
  return aiDeleteKnowledgeDocument(http, parameters, signal);
}

export function listKnowledgeVersions(parameters: AiListKnowledgeVersionsParameters, signal?: AbortSignal) {
  return aiListKnowledgeVersions(http, parameters, signal);
}

export function uploadKnowledgeVersion(parameters: AiUploadKnowledgeVersionParameters, signal?: AbortSignal) {
  return aiUploadKnowledgeVersion(http, parameters, signal);
}

export function getKnowledgeImport(parameters: AiGetKnowledgeImportParameters, signal?: AbortSignal) {
  return aiGetKnowledgeImport(http, parameters, signal);
}

export function retryKnowledgeImport(parameters: AiRetryKnowledgeImportParameters, signal?: AbortSignal) {
  return aiRetryKnowledgeImport(http, parameters, signal);
}

export function downloadKnowledgeSource(parameters: AiDownloadKnowledgeSourceParameters, signal?: AbortSignal) {
  return aiDownloadKnowledgeSource(http, parameters, signal);
}
