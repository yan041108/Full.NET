import {
  aiGetKnowledgeIndexReconciliation, type AiGetKnowledgeIndexReconciliationParameters,
  aiGetKnowledgeIndexStatus, type AiGetKnowledgeIndexStatusParameters,
  aiEnqueueKnowledgeIndex, type AiEnqueueKnowledgeIndexParameters,
  aiRetryKnowledgeIndex, type AiRetryKnowledgeIndexParameters,
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
  aiUpdateKnowledgePolicy, type AiUpdateKnowledgePolicyParameters,
  aiGetKnowledgeMembers, type AiGetKnowledgeMembersParameters,
  aiSetKnowledgeMembers, type AiSetKnowledgeMembersParameters,
  aiGetKnowledgeDocumentMembers, type AiGetKnowledgeDocumentMembersParameters,
  aiSetKnowledgeDocumentMembers, type AiSetKnowledgeDocumentMembersParameters,
  aiGetKnowledgeRoles, type AiGetKnowledgeRolesParameters,
  aiSetKnowledgeRoles, type AiSetKnowledgeRolesParameters,
  aiGetKnowledgeDocumentRoles, type AiGetKnowledgeDocumentRolesParameters,
  aiSetKnowledgeDocumentRoles, type AiSetKnowledgeDocumentRolesParameters,
  aiGetKnowledgeOrganizations, type AiGetKnowledgeOrganizationsParameters,
  aiSetKnowledgeOrganizations, type AiSetKnowledgeOrganizationsParameters,
  aiGetKnowledgeDocumentOrganizations, type AiGetKnowledgeDocumentOrganizationsParameters,
  aiSetKnowledgeDocumentOrganizations, type AiSetKnowledgeDocumentOrganizationsParameters,
  aiListKnowledgeRoleCandidates, type AiListKnowledgeRoleCandidatesParameters,
  aiListKnowledgeDocumentRoleCandidates, type AiListKnowledgeDocumentRoleCandidatesParameters,
  aiListKnowledgeOrganizationCandidates, type AiListKnowledgeOrganizationCandidatesParameters,
  aiListKnowledgeDocumentOrganizationCandidates, type AiListKnowledgeDocumentOrganizationCandidatesParameters,
} from '@fullnet/client-contracts';
import { http } from './http';

export function getKnowledgeIndexReconciliation(parameters: AiGetKnowledgeIndexReconciliationParameters, signal?: AbortSignal) {
  return aiGetKnowledgeIndexReconciliation(http, parameters, signal);
}

export function getKnowledgeIndexStatus(parameters: AiGetKnowledgeIndexStatusParameters, signal?: AbortSignal) {
  return aiGetKnowledgeIndexStatus(http, parameters, signal);
}

export function enqueueKnowledgeIndex(parameters: AiEnqueueKnowledgeIndexParameters, signal?: AbortSignal) {
  return aiEnqueueKnowledgeIndex(http, parameters, signal);
}

export function retryKnowledgeIndex(parameters: AiRetryKnowledgeIndexParameters, signal?: AbortSignal) {
  return aiRetryKnowledgeIndex(http, parameters, signal);
}

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

export function updateKnowledgePolicy(parameters: AiUpdateKnowledgePolicyParameters, signal?: AbortSignal) {
  return aiUpdateKnowledgePolicy(http, parameters, signal);
}

export function getKnowledgeMembers(parameters: AiGetKnowledgeMembersParameters, signal?: AbortSignal) {
  return aiGetKnowledgeMembers(http, parameters, signal);
}

export function setKnowledgeMembers(parameters: AiSetKnowledgeMembersParameters, signal?: AbortSignal) {
  return aiSetKnowledgeMembers(http, parameters, signal);
}

export function getKnowledgeDocumentMembers(parameters: AiGetKnowledgeDocumentMembersParameters, signal?: AbortSignal) {
  return aiGetKnowledgeDocumentMembers(http, parameters, signal);
}

export function setKnowledgeDocumentMembers(parameters: AiSetKnowledgeDocumentMembersParameters, signal?: AbortSignal) {
  return aiSetKnowledgeDocumentMembers(http, parameters, signal);
}

export function getKnowledgeRoles(parameters: AiGetKnowledgeRolesParameters, signal?: AbortSignal) {
  return aiGetKnowledgeRoles(http, parameters, signal);
}

export function setKnowledgeRoles(parameters: AiSetKnowledgeRolesParameters, signal?: AbortSignal) {
  return aiSetKnowledgeRoles(http, parameters, signal);
}

export function getKnowledgeDocumentRoles(parameters: AiGetKnowledgeDocumentRolesParameters, signal?: AbortSignal) {
  return aiGetKnowledgeDocumentRoles(http, parameters, signal);
}

export function setKnowledgeDocumentRoles(parameters: AiSetKnowledgeDocumentRolesParameters, signal?: AbortSignal) {
  return aiSetKnowledgeDocumentRoles(http, parameters, signal);
}

export function getKnowledgeOrganizations(parameters: AiGetKnowledgeOrganizationsParameters, signal?: AbortSignal) {
  return aiGetKnowledgeOrganizations(http, parameters, signal);
}

export function setKnowledgeOrganizations(parameters: AiSetKnowledgeOrganizationsParameters, signal?: AbortSignal) {
  return aiSetKnowledgeOrganizations(http, parameters, signal);
}

export function getKnowledgeDocumentOrganizations(parameters: AiGetKnowledgeDocumentOrganizationsParameters, signal?: AbortSignal) {
  return aiGetKnowledgeDocumentOrganizations(http, parameters, signal);
}

export function setKnowledgeDocumentOrganizations(parameters: AiSetKnowledgeDocumentOrganizationsParameters, signal?: AbortSignal) {
  return aiSetKnowledgeDocumentOrganizations(http, parameters, signal);
}

export function listKnowledgeRoleCandidates(parameters: AiListKnowledgeRoleCandidatesParameters, signal?: AbortSignal) {
  return aiListKnowledgeRoleCandidates(http, parameters, signal);
}

export function listKnowledgeDocumentRoleCandidates(parameters: AiListKnowledgeDocumentRoleCandidatesParameters, signal?: AbortSignal) {
  return aiListKnowledgeDocumentRoleCandidates(http, parameters, signal);
}

export function listKnowledgeOrganizationCandidates(parameters: AiListKnowledgeOrganizationCandidatesParameters, signal?: AbortSignal) {
  return aiListKnowledgeOrganizationCandidates(http, parameters, signal);
}

export function listKnowledgeDocumentOrganizationCandidates(parameters: AiListKnowledgeDocumentOrganizationCandidatesParameters, signal?: AbortSignal) {
  return aiListKnowledgeDocumentOrganizationCandidates(http, parameters, signal);
}
