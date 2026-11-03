import {
  aiEditKnowledgeCorrection, type AiEditKnowledgeCorrectionParameters,
  aiRestoreKnowledgeCorrection, type AiRestoreKnowledgeCorrectionParameters,
  aiListKnowledgeCorrectionRevisions, type AiListKnowledgeCorrectionRevisionsParameters,
  validateKnowledgeCorrectionRevision, validateKnowledgeCorrectionHistory,
  aiPrepareKnowledgeCorrection, type AiPrepareKnowledgeCorrectionParameters,
  aiGetKnowledgeCorrectionPreparation, type AiGetKnowledgeCorrectionPreparationParameters,
  aiGetLatestKnowledgeCorrectionPreparation, type AiGetLatestKnowledgeCorrectionPreparationParameters,
  aiGetKnowledgeCorrectionWorkspace, type AiGetKnowledgeCorrectionWorkspaceParameters,
  validateKnowledgeCorrectionPreparation, validateKnowledgeCorrectionWorkspace,
  aiListKnowledgeIndexChanges, type AiListKnowledgeIndexChangesParameters,
  aiRecoverKnowledgeIndexChanges, type AiRecoverKnowledgeIndexChangesParameters,
  aiGetKnowledgeIndexReconciliation, type AiGetKnowledgeIndexReconciliationParameters,
  aiGetKnowledgeIndexStatus, type AiGetKnowledgeIndexStatusParameters,
  aiRebuildKnowledgeIndex, type AiRebuildKnowledgeIndexParameters,
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

/** 人工操作和恢复的回执绑定原 CAS、父修订及来源，未知响应不能被页面当成保存成功。 */
export async function editKnowledgeCorrection(parameters: AiEditKnowledgeCorrectionParameters, signal?: AbortSignal) {
  return validateKnowledgeCorrectionRevision(await aiEditKnowledgeCorrection(http, parameters, signal), parameters.versionId,
    { expected: parameters.body.expected, sourceId: parameters.body.expected.sourceId, reason: parameters.body.reason });
}
export async function restoreKnowledgeCorrection(parameters: AiRestoreKnowledgeCorrectionParameters, signal?: AbortSignal) {
  return validateKnowledgeCorrectionRevision(await aiRestoreKnowledgeCorrection(http, parameters, signal), parameters.versionId,
    { expected: parameters.body.expected, sourceId: parameters.body.targetSourceId, reason: parameters.body.reason });
}
export async function listKnowledgeCorrectionRevisions(parameters: AiListKnowledgeCorrectionRevisionsParameters, signal?: AbortSignal) {
  return validateKnowledgeCorrectionHistory(await aiListKnowledgeCorrectionRevisions(http, parameters, signal), parameters.versionId, parameters.beforeHeadVersion);
}

/** 工作台准备只入队；生成守卫后按原版本、任务及逐块来源不变量验收响应。 */
export async function prepareKnowledgeCorrection(parameters: AiPrepareKnowledgeCorrectionParameters, signal?: AbortSignal) {
  const result = validateKnowledgeCorrectionPreparation(await aiPrepareKnowledgeCorrection(http, parameters, signal), parameters.versionId);
  if (result.documentVersion !== parameters.body.documentVersion) throw new Error('client.invalid_knowledge_correction');
  return result;
}
export async function getKnowledgeCorrectionPreparation(parameters: AiGetKnowledgeCorrectionPreparationParameters, signal?: AbortSignal) {
  return validateKnowledgeCorrectionPreparation(await aiGetKnowledgeCorrectionPreparation(http, parameters, signal), parameters.versionId, parameters.preparationId);
}
export async function getLatestKnowledgeCorrectionPreparation(parameters: AiGetLatestKnowledgeCorrectionPreparationParameters, signal?: AbortSignal) {
  return validateKnowledgeCorrectionPreparation(await aiGetLatestKnowledgeCorrectionPreparation(http, parameters, signal), parameters.versionId);
}
export async function getKnowledgeCorrectionWorkspace(parameters: AiGetKnowledgeCorrectionWorkspaceParameters, signal?: AbortSignal) {
  return validateKnowledgeCorrectionWorkspace(await aiGetKnowledgeCorrectionWorkspace(http, parameters, signal), parameters);
}

/** 生成守卫校验线格式，业务边界再约束分页及来源身份。 */
export async function listKnowledgeIndexChanges(parameters: AiListKnowledgeIndexChangesParameters, signal?: AbortSignal) {
  const result = await aiListKnowledgeIndexChanges(http, parameters, signal);
  const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const ids = new Set<string>();
  if (result.items.length > (parameters.pageSize ?? 50) || result.items.some(row => {
    const key = row.documentId.toLowerCase();
    const invalid = !guid.test(row.documentId) || !guid.test(row.versionId) || ids.has(key) || !row.title.trim() || row.title.length > 200
      || !Number.isInteger(row.versionNumber) || row.versionNumber < 1 || !Number.isFinite(Date.parse(row.createdAtUtc)) || !Number.isFinite(Date.parse(row.updatedAtUtc));
    ids.add(key); return invalid;
  }) || (result.nextDocumentId != null && (!guid.test(result.nextDocumentId) || !result.items.length
    || result.nextDocumentId.toLowerCase() !== result.items.at(-1)!.documentId.toLowerCase()
    || result.nextDocumentId.toLowerCase() === parameters.afterDocumentId?.toLowerCase()))) throw new Error('client.invalid_knowledge_resource');
  return result;
}

export function recoverKnowledgeIndexChanges(parameters: AiRecoverKnowledgeIndexChangesParameters, signal?: AbortSignal) {
  return aiRecoverKnowledgeIndexChanges(http, parameters, signal);
}

export function getKnowledgeIndexReconciliation(parameters: AiGetKnowledgeIndexReconciliationParameters, signal?: AbortSignal) {
  return aiGetKnowledgeIndexReconciliation(http, parameters, signal);
}

export function getKnowledgeIndexStatus(parameters: AiGetKnowledgeIndexStatusParameters, signal?: AbortSignal) {
  return aiGetKnowledgeIndexStatus(http, parameters, signal);
}

export function rebuildKnowledgeIndex(parameters: AiRebuildKnowledgeIndexParameters, signal?: AbortSignal) {
  return aiRebuildKnowledgeIndex(http, parameters, signal);
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
