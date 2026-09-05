// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 低层 HttpClient Operation。

import type { HttpClient, RequestOptions } from '../http.js';
import type {
  AccessHostDocumentShareRequest,
  AccessLogCursorPageResponse,
  AccessLogResponse,
  ActWorkflowTodoRequest,
  AddHostDocumentVersionRequest,
  AssignHostTenantPackageRequest,
  AssignOrganizationPositionLevelRequest,
  AssignOrganizationPositionUnitRequest,
  AuthorizationTreeActionResponse,
  AuthorizationTreeModuleResponse,
  AuthorizationTreePageResponse,
  BatchDeleteConfigEntriesRequest,
  BatchHostUserIdsRequest,
  BatchHostUserStatusItem,
  BatchHostUserStatusResponse,
  BatchUpdateConfigValuesRequest,
  BeginTotpEnrollmentResponse,
  CancelWorkflowInstanceRequest,
  ChangeHostJobScheduleStateRequest,
  ChangeSerialNumberRuleStatusRequest,
  CodeGenerationCatalogColumnListResponse,
  CodeGenerationCatalogColumnSyncRequest,
  CodeGenerationCatalogColumnSyncResponse,
  CodeGenerationCatalogTableResponse,
  CodeGenerationClientRouteTargetRequest,
  CodeGenerationEntityCapabilitiesRequest,
  CodeGenerationIntegrationTargetRequest,
  CodeGenerationPreviewArtifactResponse,
  CodeGenerationPreviewColumnRequest,
  CodeGenerationPreviewColumnUiRequest,
  CodeGenerationPreviewRequest,
  CodeGenerationPreviewResponse,
  CodeGenerationRelationshipRequest,
  CodeGenerationRunApplyRequest,
  CodeGenerationRunApplyResponse,
  CodeGenerationRunPreviewRequest,
  CodeGenerationRunPreviewResponse,
  CodeGenerationRunResponse,
  CodeGenerationRunRollbackChainRequest,
  CodeGenerationRunRollbackChainResponse,
  CodeGenerationRunRollbackRequest,
  CodeGenerationRunRollbackResponse,
  CodeGenerationTemplateResponse,
  ConfigEntryResponse,
  ConfigValueUpdate,
  ConfirmTotpEnrollmentRequest,
  CreateCodeGenerationTemplateRequest,
  CreateConfigEntryRequest,
  CreateDictItemRequest,
  CreateDictTypeRequest,
  CreateHostAnnouncementRequest,
  CreateHostApiKeyRequest,
  CreateHostApiKeyResponse,
  CreateHostDocumentCategoryRequest,
  CreateHostDocumentItemRequest,
  CreateHostDocumentShareRequest,
  CreateHostDocumentTagRequest,
  CreateHostJobDefinitionRequest,
  CreateHostJobScheduleRequest,
  CreateHostMenuRequest,
  CreateHostRoleRequest,
  CreateHostTenantPackageRequest,
  CreateHostUserRequest,
  CreateMyRecipientEndpointRequest,
  CreateNotificationBindingRequest,
  CreateNotificationProviderProfileRequest,
  CreateNotificationTemplateRequest,
  CreateOrganizationPositionLevelRequest,
  CreateOrganizationPositionRequest,
  CreateOrganizationUnitRequest,
  CreateOrganizationUserPositionRequest,
  CreateOrganizationUserUnitRequest,
  CreateSerialNumberRuleRequest,
  CreateWorkflowDefinitionRequest,
  CreateWorkflowFormRequest,
  CurrentUserResponse,
  DeleteCodeGenerationTemplateRequest,
  DeleteConfigEntryRequest,
  DeleteDictItemRequest,
  DeleteDictTypeRequest,
  DeleteHostDocumentCategoryRequest,
  DeleteHostDocumentItemRequest,
  DeleteHostDocumentTagRequest,
  DeleteHostJobDefinitionRequest,
  DiagnosticPolicyResponse,
  DiagnosticPolicyRuleRequest,
  DiagnosticPolicyRuleResponse,
  DictItemResponse,
  DictTypeResponse,
  DisableHostJobDefinitionRequest,
  EnumCatalogDetail,
  EnumCatalogMember,
  EnumCatalogSummary,
  ExceptionLogResponse,
  FieldProjectionDefaultVisibility,
  FieldProjectionFieldDefinition,
  FieldProjectionResourceDefinition,
  FieldProjectionSensitivity,
  GrantSuperAdministratorRequest,
  HostAnnouncementResponse,
  HostAnnouncementTargetOrganization,
  HostApiKeyResponse,
  HostDashboardActivityResponse,
  HostDashboardSummaryResponse,
  HostDocumentCategoryResponse,
  HostDocumentItemResponse,
  HostDocumentPermissionEntry,
  HostDocumentPermissionResponse,
  HostDocumentShareAccessResponse,
  HostDocumentShareResponse,
  HostDocumentStatisticsCategoryItem,
  HostDocumentStatisticsResponse,
  HostDocumentStatisticsSummaryResponse,
  HostDocumentStatisticsTypeItem,
  HostDocumentStatus,
  HostDocumentTagAssignmentResponse,
  HostDocumentTagResponse,
  HostDocumentType,
  HostDocumentVersionResponse,
  HostFileResponse,
  HostJobDefinitionResponse,
  HostJobExecutionResponse,
  HostJobGroupResponse,
  HostJobHealthBacklogSnapshot,
  HostJobHealthResponse,
  HostJobScheduleCronPreviewResponse,
  HostJobScheduleDefinitionOptionResponse,
  HostJobScheduleResponse,
  HostJobWorkerInstanceResponse,
  HostMenuPermissionOptionResponse,
  HostMenuResponse,
  HostNavigationCatalogSyncResponse,
  HostOnlineSessionResponse,
  HostRoleDataScopeResponse,
  HostRoleFieldGrantsResponse,
  HostRoleResponse,
  HostUserManagementOrganizationReferenceResponse,
  HostUserProfileResponse,
  HostUserProfileWriteRequest,
  HostUserProjectedFieldsResponse,
  HostUserResponse,
  HostUserRolesResponse,
  HttpJobArgs,
  HttpJobSecretHeaderRef,
  IFormFile,
  ImportHostUserRowResult,
  ImportHostUsersRequest,
  ImportHostUsersResponse,
  InboxMessageResponse,
  InboxUnreadCountResponse,
  JsonElement,
  LocalePreferenceResponse,
  LogFileSummary,
  LogFileTail,
  LoginRequest,
  ModuleCatalogEntryResponse,
  NotificationBindingResponse,
  NotificationBindingTargetInput,
  NotificationDeliveryAttemptResponse,
  NotificationDeliveryResponse,
  NotificationProviderConfigField,
  NotificationProviderProfileResponse,
  NotificationProviderTypeDescriptor,
  NotificationTemplateBody,
  NotificationTemplateParameterDefinition,
  NotificationTemplateParameterSchema,
  NotificationTemplateResponse,
  OperationLogResponse,
  OrganizationAssignableUserResponse,
  OrganizationPositionLevelResponse,
  OrganizationPositionResponse,
  OrganizationUnitResponse,
  OrganizationUserPositionResponse,
  OrganizationUserUnitResponse,
  OutboundCallLogResponse,
  PagedResultOfAccessLogResponse,
  PagedResultOfCodeGenerationRunResponse,
  PagedResultOfCodeGenerationTemplateResponse,
  PagedResultOfConfigEntryResponse,
  PagedResultOfDictItemResponse,
  PagedResultOfDictTypeResponse,
  PagedResultOfExceptionLogResponse,
  PagedResultOfHostAnnouncementResponse,
  PagedResultOfHostApiKeyResponse,
  PagedResultOfHostDocumentItemResponse,
  PagedResultOfHostDocumentShareResponse,
  PagedResultOfHostFileResponse,
  PagedResultOfHostJobDefinitionResponse,
  PagedResultOfHostJobExecutionResponse,
  PagedResultOfHostJobScheduleResponse,
  PagedResultOfHostMenuResponse,
  PagedResultOfHostOnlineSessionResponse,
  PagedResultOfHostRoleResponse,
  PagedResultOfHostUserResponse,
  PagedResultOfInboxMessageResponse,
  PagedResultOfNotificationBindingResponse,
  PagedResultOfNotificationDeliveryResponse,
  PagedResultOfNotificationProviderProfileResponse,
  PagedResultOfNotificationTemplateResponse,
  PagedResultOfOperationLogResponse,
  PagedResultOfOrganizationAssignableUserResponse,
  PagedResultOfOrganizationPositionLevelResponse,
  PagedResultOfOrganizationPositionResponse,
  PagedResultOfOrganizationUnitResponse,
  PagedResultOfOrganizationUserPositionResponse,
  PagedResultOfOrganizationUserUnitResponse,
  PagedResultOfOutboundCallLogResponse,
  PagedResultOfSerialNumberRuleResponse,
  PagedResultOfTenantPackageSummary,
  PagedResultOfTenantSummary,
  PagedResultOfWorkflowRecoveryTaskResponse,
  PauseWorkflowInstanceRequest,
  PreviewSerialNumberRequest,
  ProblemDetails,
  ProvisionTenantRequest,
  PublishHostAnnouncementRequest,
  PublishNotificationBindingRequest,
  PublishNotificationProviderProfileRequest,
  PublishNotificationTemplateRequest,
  PublishWorkflowDefinitionRequest,
  PublishWorkflowFormRequest,
  ReassignWorkflowInstanceRequest,
  RecipientEndpointResponse,
  ReconcileWorkflowRecoveryTaskRequest,
  RecoverWorkflowInstanceRequest,
  ReplaceHostRoleFieldGrantsRequest,
  ReplaceHostRolePermissionsRequest,
  ReplaceHostUserRolesRequest,
  ResetHostUserPasswordRequest,
  RestoreDiagnosticPolicyRequest,
  RestoreHostDocumentItemRequest,
  ResumeWorkflowInstanceRequest,
  RetryNotificationDeliveryRequest,
  RetryWorkflowRecoveryTaskRequest,
  RevokeSuperAdministratorRequest,
  SendHostInboxMessageRequest,
  SendRecipientEndpointVerificationResponse,
  SerialNumberPreviewResponse,
  SerialNumberResetInterval,
  SerialNumberRuleResponse,
  SerialNumberRuleScope,
  SetHostDocumentPermissionsRequest,
  SetNotificationProviderProfileEnabledRequest,
  StartWorkflowInstanceRequest,
  Stream,
  SuperAdministratorAuditResponse,
  SuperAdministratorChangeResponse,
  SuperAdministratorResponse,
  TenantPackageSummary,
  TenantSummary,
  TokenResponse,
  TotpEnrollmentStatusResponse,
  UpdateCodeGenerationTemplateRequest,
  UpdateConfigEntryRequest,
  UpdateDiagnosticPolicyRequest,
  UpdateDictItemRequest,
  UpdateDictTypeRequest,
  UpdateHostAnnouncementRequest,
  UpdateHostDocumentCategoryRequest,
  UpdateHostDocumentItemRequest,
  UpdateHostDocumentShareStatusRequest,
  UpdateHostDocumentTagRequest,
  UpdateHostJobDefinitionRequest,
  UpdateHostJobScheduleRequest,
  UpdateHostMenuRequest,
  UpdateHostRoleDataScopeRequest,
  UpdateHostRoleRequest,
  UpdateHostTenantPackageRequest,
  UpdateHostTenantRequest,
  UpdateHostUserRequest,
  UpdateLocaleRequest,
  UpdateNotificationBindingRequest,
  UpdateNotificationProviderProfileRequest,
  UpdateNotificationTemplateRequest,
  UpdateOrganizationPositionLevelRequest,
  UpdateOrganizationPositionRequest,
  UpdateOrganizationUnitRequest,
  UpdateOrganizationUserPositionRequest,
  UpdateOrganizationUserUnitRequest,
  UpdateSerialNumberRuleRequest,
  UpdateWorkflowDefinitionDraftRequest,
  UpdateWorkflowFormDraftRequest,
  VerifyRecipientEndpointCodeRequest,
  WorkflowCcReadResponse,
  WorkflowCcResponse,
  WorkflowDefinitionDraft,
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowExecutionLogResponse,
  WorkflowFormComponentCatalogResponse,
  WorkflowFormComponentResponse,
  WorkflowFormField,
  WorkflowFormResponse,
  WorkflowFormSchema,
  WorkflowFormSection,
  WorkflowFormVersionResponse,
  WorkflowInstanceResponse,
  WorkflowNodeDraft,
  WorkflowNodeTypeCatalogResponse,
  WorkflowNodeTypeResponse,
  WorkflowRecipientCandidatePageResponse,
  WorkflowRecipientCandidateResponse,
  WorkflowRecoveryTaskResponse,
  WorkflowTodoDetailResponse,
  WorkflowTodoResponse,
  WorkflowTodoRuntimeResponse
} from './models.generated.js';
import {
  readAccessLogCursorPageResponse,
  readBatchHostUserStatusResponse,
  readBeginTotpEnrollmentResponse,
  readCodeGenerationCatalogColumnListResponse,
  readCodeGenerationCatalogColumnSyncResponse,
  readCodeGenerationListCatalogTablesResponse,
  readCodeGenerationPreviewResponse,
  readCodeGenerationRunApplyResponse,
  readCodeGenerationRunPreviewResponse,
  readCodeGenerationRunRollbackChainResponse,
  readCodeGenerationRunRollbackResponse,
  readCodeGenerationTemplateResponse,
  readConfigEntryResponse,
  readCreateHostApiKeyResponse,
  readCurrentUserResponse,
  readDiagnosticPolicyResponse,
  readDictItemResponse,
  readDictTypeResponse,
  readDocumentHostDeleteCategoryResponse,
  readDocumentHostDeleteItemResponse,
  readDocumentHostDeleteTagResponse,
  readDocumentHostListCategoriesResponse,
  readDocumentHostListDocumentPermissionsResponse,
  readDocumentHostListItemVersionsResponse,
  readDocumentHostListTagsResponse,
  readDocumentHostPurgeRecycleBinItemResponse,
  readDocumentHostSetDocumentPermissionsResponse,
  readEnumCatalogDetail,
  readHostAnnouncementResponse,
  readHostApiKeyResponse,
  readHostDashboardSummaryResponse,
  readHostDocumentCategoryResponse,
  readHostDocumentItemResponse,
  readHostDocumentShareAccessResponse,
  readHostDocumentShareResponse,
  readHostDocumentStatisticsResponse,
  readHostDocumentTagResponse,
  readHostFileResponse,
  readHostJobDefinitionResponse,
  readHostJobExecutionResponse,
  readHostJobHealthResponse,
  readHostJobScheduleCronPreviewResponse,
  readHostJobScheduleResponse,
  readHostMenuResponse,
  readHostNavigationCatalogSyncResponse,
  readHostOnlineSessionResponse,
  readHostRoleDataScopeResponse,
  readHostRoleFieldGrantsResponse,
  readHostRoleResponse,
  readHostUserManagementOrganizationReferenceResponse,
  readHostUserResponse,
  readHostUserRolesResponse,
  readIdentityExportHostUsersResponse,
  readIdentityGetAuthorizationTreeResponse,
  readIdentityListAllHostMenusResponse,
  readIdentityListFieldProjectionCatalogResponse,
  readIdentityListHostMenuPermissionOptionsResponse,
  readIdentityListHostModulesResponse,
  readIdentityListSuperAdministratorAuditsResponse,
  readIdentityListSuperAdministratorsResponse,
  readImportHostUsersResponse,
  readInboxMessageResponse,
  readInboxUnreadCountResponse,
  readJobsListHostJobGroupsResponse,
  readJobsListHostJobScheduleDefinitionOptionsResponse,
  readLocalePreferenceResponse,
  readLogFileTail,
  readModuleCatalogEntryResponse,
  readNotificationBindingResponse,
  readNotificationDeliveryResponse,
  readNotificationProviderProfileResponse,
  readNotificationsListMyRecipientEndpointsResponse,
  readNotificationsListProviderTypesResponse,
  readNotificationTemplateResponse,
  readObservabilityListLogFilesResponse,
  readOrganizationPositionLevelResponse,
  readOrganizationPositionResponse,
  readOrganizationUnitResponse,
  readOrganizationUserPositionResponse,
  readOrganizationUserUnitResponse,
  readPagedResultOfAccessLogResponse,
  readPagedResultOfCodeGenerationRunResponse,
  readPagedResultOfCodeGenerationTemplateResponse,
  readPagedResultOfConfigEntryResponse,
  readPagedResultOfDictItemResponse,
  readPagedResultOfDictTypeResponse,
  readPagedResultOfExceptionLogResponse,
  readPagedResultOfHostAnnouncementResponse,
  readPagedResultOfHostApiKeyResponse,
  readPagedResultOfHostDocumentItemResponse,
  readPagedResultOfHostDocumentShareResponse,
  readPagedResultOfHostFileResponse,
  readPagedResultOfHostJobDefinitionResponse,
  readPagedResultOfHostJobExecutionResponse,
  readPagedResultOfHostJobScheduleResponse,
  readPagedResultOfHostMenuResponse,
  readPagedResultOfHostOnlineSessionResponse,
  readPagedResultOfHostRoleResponse,
  readPagedResultOfHostUserResponse,
  readPagedResultOfInboxMessageResponse,
  readPagedResultOfNotificationBindingResponse,
  readPagedResultOfNotificationDeliveryResponse,
  readPagedResultOfNotificationProviderProfileResponse,
  readPagedResultOfNotificationTemplateResponse,
  readPagedResultOfOperationLogResponse,
  readPagedResultOfOrganizationAssignableUserResponse,
  readPagedResultOfOrganizationPositionLevelResponse,
  readPagedResultOfOrganizationPositionResponse,
  readPagedResultOfOrganizationUnitResponse,
  readPagedResultOfOrganizationUserPositionResponse,
  readPagedResultOfOrganizationUserUnitResponse,
  readPagedResultOfOutboundCallLogResponse,
  readPagedResultOfSerialNumberRuleResponse,
  readPagedResultOfTenantPackageSummary,
  readPagedResultOfTenantSummary,
  readPagedResultOfWorkflowRecoveryTaskResponse,
  readRecipientEndpointResponse,
  readSendRecipientEndpointVerificationResponse,
  readSerialNumberPreviewResponse,
  readSerialNumberRuleResponse,
  readSettingsBatchUpdateHostConfigEntryValuesResponse,
  readSettingsListAllHostConfigEntriesResponse,
  readSettingsListAllHostDictTypesResponse,
  readSettingsListAllTenantDictTypesResponse,
  readSettingsListHostConfigEntryGroupsResponse,
  readSettingsListHostDictItemsByTypeCodeResponse,
  readSettingsListHostEnumCatalogsResponse,
  readSettingsListTenantDictItemsByTypeCodeResponse,
  readSuperAdministratorChangeResponse,
  readTenantPackageSummary,
  readTenantSummary,
  readTokenResponse,
  readTotpEnrollmentStatusResponse,
  readWorkflowCcReadResponse,
  readWorkflowDefinitionResponse,
  readWorkflowDefinitionVersionResponse,
  readWorkflowFormComponentCatalogResponse,
  readWorkflowFormResponse,
  readWorkflowFormVersionResponse,
  readWorkflowInstanceResponse,
  readWorkflowListDefinitionsResponse,
  readWorkflowListDefinitionVersionsResponse,
  readWorkflowListFormsResponse,
  readWorkflowListInstanceExecutionLogsResponse,
  readWorkflowListMyCcResponse,
  readWorkflowListMyTodosResponse,
  readWorkflowNodeTypeCatalogResponse,
  readWorkflowRecipientCandidatePageResponse,
  readWorkflowRecoveryTaskResponse,
  readWorkflowTodoDetailResponse,
  readWorkflowTodoRuntimeResponse
} from './guards.generated.js';

export type GeneratedJsonOperation<T> = (
  http: HttpClient,
  parameters: Readonly<Record<string, unknown>>,
  signal?: AbortSignal,
  options?: RequestOptions
) => Promise<T>;

export interface AuditingListHostAccessLogsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly httpMethod?: string;
  readonly statusCode?: number;
  readonly pathContains?: string;
}

export async function auditingListHostAccessLogs(
  http: HttpClient,
  parameters: AuditingListHostAccessLogsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfAccessLogResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.fromUtc !== undefined) {
    query.set('fromUtc', String(parameters.fromUtc));
  }
  if (parameters.toUtc !== undefined) {
    query.set('toUtc', String(parameters.toUtc));
  }
  if (parameters.httpMethod !== undefined) {
    query.set('httpMethod', String(parameters.httpMethod));
  }
  if (parameters.statusCode !== undefined) {
    query.set('statusCode', String(parameters.statusCode));
  }
  if (parameters.pathContains !== undefined) {
    query.set('pathContains', String(parameters.pathContains));
  }
  const path = query.size === 0 ? `/api/v1/auditing/access-logs` : `/api/v1/auditing/access-logs?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfAccessLogResponse(value);
}

export interface AuditingListHostAccessLogsByCursorParameters {
  readonly limit?: number;
  readonly cursor?: string;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly httpMethod?: string;
  readonly statusCode?: number;
  readonly pathContains?: string;
}

export async function auditingListHostAccessLogsByCursor(
  http: HttpClient,
  parameters: AuditingListHostAccessLogsByCursorParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<AccessLogCursorPageResponse> {
  const query = new URLSearchParams();
  if (parameters.limit !== undefined) {
    query.set('limit', String(parameters.limit));
  }
  if (parameters.cursor !== undefined) {
    query.set('cursor', String(parameters.cursor));
  }
  if (parameters.fromUtc !== undefined) {
    query.set('fromUtc', String(parameters.fromUtc));
  }
  if (parameters.toUtc !== undefined) {
    query.set('toUtc', String(parameters.toUtc));
  }
  if (parameters.httpMethod !== undefined) {
    query.set('httpMethod', String(parameters.httpMethod));
  }
  if (parameters.statusCode !== undefined) {
    query.set('statusCode', String(parameters.statusCode));
  }
  if (parameters.pathContains !== undefined) {
    query.set('pathContains', String(parameters.pathContains));
  }
  const path = query.size === 0 ? `/api/v1/auditing/access-logs/cursor` : `/api/v1/auditing/access-logs/cursor?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readAccessLogCursorPageResponse(value);
}

export interface AuditingListHostExceptionLogsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly exceptionTypeContains?: string;
  readonly pathContains?: string;
}

export async function auditingListHostExceptionLogs(
  http: HttpClient,
  parameters: AuditingListHostExceptionLogsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfExceptionLogResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.fromUtc !== undefined) {
    query.set('fromUtc', String(parameters.fromUtc));
  }
  if (parameters.toUtc !== undefined) {
    query.set('toUtc', String(parameters.toUtc));
  }
  if (parameters.exceptionTypeContains !== undefined) {
    query.set('exceptionTypeContains', String(parameters.exceptionTypeContains));
  }
  if (parameters.pathContains !== undefined) {
    query.set('pathContains', String(parameters.pathContains));
  }
  const path = query.size === 0 ? `/api/v1/auditing/exception-logs` : `/api/v1/auditing/exception-logs?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfExceptionLogResponse(value);
}

export interface AuditingListHostOperationLogsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly httpMethod?: string;
  readonly succeeded?: boolean;
  readonly pathContains?: string;
}

export async function auditingListHostOperationLogs(
  http: HttpClient,
  parameters: AuditingListHostOperationLogsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOperationLogResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.fromUtc !== undefined) {
    query.set('fromUtc', String(parameters.fromUtc));
  }
  if (parameters.toUtc !== undefined) {
    query.set('toUtc', String(parameters.toUtc));
  }
  if (parameters.httpMethod !== undefined) {
    query.set('httpMethod', String(parameters.httpMethod));
  }
  if (parameters.succeeded !== undefined) {
    query.set('succeeded', String(parameters.succeeded));
  }
  if (parameters.pathContains !== undefined) {
    query.set('pathContains', String(parameters.pathContains));
  }
  const path = query.size === 0 ? `/api/v1/auditing/operation-logs` : `/api/v1/auditing/operation-logs?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOperationLogResponse(value);
}

export interface AuditingListHostOutboundCallLogsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly providerKey?: string;
  readonly succeeded?: boolean;
  readonly operationContains?: string;
}

export async function auditingListHostOutboundCallLogs(
  http: HttpClient,
  parameters: AuditingListHostOutboundCallLogsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOutboundCallLogResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.fromUtc !== undefined) {
    query.set('fromUtc', String(parameters.fromUtc));
  }
  if (parameters.toUtc !== undefined) {
    query.set('toUtc', String(parameters.toUtc));
  }
  if (parameters.providerKey !== undefined) {
    query.set('providerKey', String(parameters.providerKey));
  }
  if (parameters.succeeded !== undefined) {
    query.set('succeeded', String(parameters.succeeded));
  }
  if (parameters.operationContains !== undefined) {
    query.set('operationContains', String(parameters.operationContains));
  }
  const path = query.size === 0 ? `/api/v1/auditing/outbound-call-logs` : `/api/v1/auditing/outbound-call-logs?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOutboundCallLogResponse(value);
}

export interface CodeGenerationApplyRunParameters {
  readonly body: CodeGenerationRunApplyRequest;
}

export async function codeGenerationApplyRun(
  http: HttpClient,
  parameters: CodeGenerationApplyRunParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationRunApplyResponse> {
  const path = `/api/v1/code-generation/runs/apply`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationRunApplyResponse(value);
}

export interface CodeGenerationCreateTemplateParameters {
  readonly body: CreateCodeGenerationTemplateRequest;
}

export async function codeGenerationCreateTemplate(
  http: HttpClient,
  parameters: CodeGenerationCreateTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationTemplateResponse> {
  const path = `/api/v1/code-generation/templates`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationTemplateResponse(value);
}

export interface CodeGenerationDeleteTemplateParameters {
  readonly templateId: string;
  readonly body: DeleteCodeGenerationTemplateRequest;
}

export async function codeGenerationDeleteTemplate(
  http: HttpClient,
  parameters: CodeGenerationDeleteTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/code-generation/templates/${encodeURIComponent(String(parameters.templateId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface CodeGenerationDownloadRunArtifactsParameters {
  readonly runId: string;
}

export async function codeGenerationDownloadRunArtifacts(
  http: HttpClient,
  parameters: CodeGenerationDownloadRunArtifactsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/code-generation/runs/${encodeURIComponent(String(parameters.runId))}/artifacts.zip`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface CodeGenerationGetTemplateParameters {
  readonly templateId: string;
}

export async function codeGenerationGetTemplate(
  http: HttpClient,
  parameters: CodeGenerationGetTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationTemplateResponse> {
  const path = `/api/v1/code-generation/templates/${encodeURIComponent(String(parameters.templateId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationTemplateResponse(value);
}

export interface CodeGenerationListCatalogColumnsParameters {
  readonly tableName: string;
}

export async function codeGenerationListCatalogColumns(
  http: HttpClient,
  parameters: CodeGenerationListCatalogColumnsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationCatalogColumnListResponse> {
  const path = `/api/v1/code-generation/catalog/tables/${encodeURIComponent(String(parameters.tableName))}/columns`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationCatalogColumnListResponse(value);
}

export interface CodeGenerationListCatalogTablesParameters {

}

export async function codeGenerationListCatalogTables(
  http: HttpClient,
  parameters: CodeGenerationListCatalogTablesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<CodeGenerationCatalogTableResponse>> {
  const path = `/api/v1/code-generation/catalog/tables`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationListCatalogTablesResponse(value);
}

export interface CodeGenerationListRunsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly status?: string;
}

export async function codeGenerationListRuns(
  http: HttpClient,
  parameters: CodeGenerationListRunsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfCodeGenerationRunResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.status !== undefined) {
    query.set('status', String(parameters.status));
  }
  const path = query.size === 0 ? `/api/v1/code-generation/runs` : `/api/v1/code-generation/runs?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfCodeGenerationRunResponse(value);
}

export interface CodeGenerationListTemplatesParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly name?: string;
  readonly tableName?: string;
}

export async function codeGenerationListTemplates(
  http: HttpClient,
  parameters: CodeGenerationListTemplatesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfCodeGenerationTemplateResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.name !== undefined) {
    query.set('name', String(parameters.name));
  }
  if (parameters.tableName !== undefined) {
    query.set('tableName', String(parameters.tableName));
  }
  const path = query.size === 0 ? `/api/v1/code-generation/templates` : `/api/v1/code-generation/templates?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfCodeGenerationTemplateResponse(value);
}

export interface CodeGenerationPreviewCrudParameters {
  readonly body: CodeGenerationPreviewRequest;
}

export async function codeGenerationPreviewCrud(
  http: HttpClient,
  parameters: CodeGenerationPreviewCrudParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationPreviewResponse> {
  const path = `/api/v1/code-generation/previews`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationPreviewResponse(value);
}

export interface CodeGenerationPreviewRunParameters {
  readonly body: CodeGenerationRunPreviewRequest;
}

export async function codeGenerationPreviewRun(
  http: HttpClient,
  parameters: CodeGenerationPreviewRunParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationRunPreviewResponse> {
  const path = `/api/v1/code-generation/runs/preview`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationRunPreviewResponse(value);
}

export interface CodeGenerationRollbackRunParameters {
  readonly body: CodeGenerationRunRollbackRequest;
}

export async function codeGenerationRollbackRun(
  http: HttpClient,
  parameters: CodeGenerationRollbackRunParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationRunRollbackResponse> {
  const path = `/api/v1/code-generation/runs/rollback`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationRunRollbackResponse(value);
}

export interface CodeGenerationRollbackRunChainParameters {
  readonly body: CodeGenerationRunRollbackChainRequest;
}

export async function codeGenerationRollbackRunChain(
  http: HttpClient,
  parameters: CodeGenerationRollbackRunChainParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationRunRollbackChainResponse> {
  const path = `/api/v1/code-generation/runs/rollback-chain`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationRunRollbackChainResponse(value);
}

export interface CodeGenerationSyncCatalogColumnsParameters {
  readonly body: CodeGenerationCatalogColumnSyncRequest;
}

export async function codeGenerationSyncCatalogColumns(
  http: HttpClient,
  parameters: CodeGenerationSyncCatalogColumnsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationCatalogColumnSyncResponse> {
  const path = `/api/v1/code-generation/catalog/column-sync`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationCatalogColumnSyncResponse(value);
}

export interface CodeGenerationUpdateTemplateParameters {
  readonly templateId: string;
  readonly body: UpdateCodeGenerationTemplateRequest;
}

export async function codeGenerationUpdateTemplate(
  http: HttpClient,
  parameters: CodeGenerationUpdateTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CodeGenerationTemplateResponse> {
  const path = `/api/v1/code-generation/templates/${encodeURIComponent(String(parameters.templateId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCodeGenerationTemplateResponse(value);
}

export interface DocumentHostAddItemVersionParameters {
  readonly itemId: string;
  readonly body: AddHostDocumentVersionRequest;
}

export async function documentHostAddItemVersion(
  http: HttpClient,
  parameters: DocumentHostAddItemVersionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentItemResponse> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/versions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentItemResponse(value);
}

export interface DocumentHostCreateCategoryParameters {
  readonly body: CreateHostDocumentCategoryRequest;
}

export async function documentHostCreateCategory(
  http: HttpClient,
  parameters: DocumentHostCreateCategoryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentCategoryResponse> {
  const path = `/api/v1/document/host/categories`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentCategoryResponse(value);
}

export interface DocumentHostCreateDocumentShareParameters {
  readonly body: CreateHostDocumentShareRequest;
}

export async function documentHostCreateDocumentShare(
  http: HttpClient,
  parameters: DocumentHostCreateDocumentShareParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentShareResponse> {
  const path = `/api/v1/document/host/shares`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentShareResponse(value);
}

export interface DocumentHostCreateItemParameters {
  readonly body: CreateHostDocumentItemRequest;
}

export async function documentHostCreateItem(
  http: HttpClient,
  parameters: DocumentHostCreateItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentItemResponse> {
  const path = `/api/v1/document/host/items`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentItemResponse(value);
}

export interface DocumentHostCreateTagParameters {
  readonly body: CreateHostDocumentTagRequest;
}

export async function documentHostCreateTag(
  http: HttpClient,
  parameters: DocumentHostCreateTagParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentTagResponse> {
  const path = `/api/v1/document/host/tags`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentTagResponse(value);
}

export interface DocumentHostDeleteCategoryParameters {
  readonly categoryId: string;
  readonly body: DeleteHostDocumentCategoryRequest;
}

export async function documentHostDeleteCategory(
  http: HttpClient,
  parameters: DocumentHostDeleteCategoryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<boolean> {
  const path = `/api/v1/document/host/categories/${encodeURIComponent(String(parameters.categoryId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostDeleteCategoryResponse(value);
}

export interface DocumentHostDeleteItemParameters {
  readonly itemId: string;
  readonly body: DeleteHostDocumentItemRequest;
}

export async function documentHostDeleteItem(
  http: HttpClient,
  parameters: DocumentHostDeleteItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<boolean> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostDeleteItemResponse(value);
}

export interface DocumentHostDeleteTagParameters {
  readonly tagId: string;
  readonly body: DeleteHostDocumentTagRequest;
}

export async function documentHostDeleteTag(
  http: HttpClient,
  parameters: DocumentHostDeleteTagParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<boolean> {
  const path = `/api/v1/document/host/tags/${encodeURIComponent(String(parameters.tagId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostDeleteTagResponse(value);
}

export interface DocumentHostDownloadItemContentParameters {
  readonly itemId: string;
}

export async function documentHostDownloadItemContent(
  http: HttpClient,
  parameters: DocumentHostDownloadItemContentParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/content`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface DocumentHostGetDocumentStatisticsParameters {

}

export async function documentHostGetDocumentStatistics(
  http: HttpClient,
  parameters: DocumentHostGetDocumentStatisticsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentStatisticsResponse> {
  const path = `/api/v1/document/host/statistics`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentStatisticsResponse(value);
}

export interface DocumentHostListCategoriesParameters {

}

export async function documentHostListCategories(
  http: HttpClient,
  parameters: DocumentHostListCategoriesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostDocumentCategoryResponse>> {
  const path = `/api/v1/document/host/categories`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostListCategoriesResponse(value);
}

export interface DocumentHostListDocumentPermissionsParameters {
  readonly documentId: string;
}

export async function documentHostListDocumentPermissions(
  http: HttpClient,
  parameters: DocumentHostListDocumentPermissionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostDocumentPermissionResponse>> {
  const path = `/api/v1/document/host/permissions/by-document/${encodeURIComponent(String(parameters.documentId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostListDocumentPermissionsResponse(value);
}

export interface DocumentHostListDocumentSharesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function documentHostListDocumentShares(
  http: HttpClient,
  parameters: DocumentHostListDocumentSharesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostDocumentShareResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/document/host/shares` : `/api/v1/document/host/shares?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostDocumentShareResponse(value);
}

export interface DocumentHostListItemsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function documentHostListItems(
  http: HttpClient,
  parameters: DocumentHostListItemsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostDocumentItemResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/document/host/items` : `/api/v1/document/host/items?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostDocumentItemResponse(value);
}

export interface DocumentHostListItemVersionsParameters {
  readonly itemId: string;
}

export async function documentHostListItemVersions(
  http: HttpClient,
  parameters: DocumentHostListItemVersionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostDocumentVersionResponse>> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/versions`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostListItemVersionsResponse(value);
}

export interface DocumentHostListRecycleBinItemsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function documentHostListRecycleBinItems(
  http: HttpClient,
  parameters: DocumentHostListRecycleBinItemsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostDocumentItemResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/document/host/recycle-bin` : `/api/v1/document/host/recycle-bin?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostDocumentItemResponse(value);
}

export interface DocumentHostListTagsParameters {

}

export async function documentHostListTags(
  http: HttpClient,
  parameters: DocumentHostListTagsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostDocumentTagResponse>> {
  const path = `/api/v1/document/host/tags`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostListTagsResponse(value);
}

export interface DocumentHostPreviewItemContentParameters {
  readonly itemId: string;
}

export async function documentHostPreviewItemContent(
  http: HttpClient,
  parameters: DocumentHostPreviewItemContentParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/preview`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface DocumentHostPreviewItemVersionContentParameters {
  readonly itemId: string;
  readonly versionId: string;
}

export async function documentHostPreviewItemVersionContent(
  http: HttpClient,
  parameters: DocumentHostPreviewItemVersionContentParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/versions/${encodeURIComponent(String(parameters.versionId))}/preview`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface DocumentHostPurgeRecycleBinItemParameters {
  readonly id: string;
}

export async function documentHostPurgeRecycleBinItem(
  http: HttpClient,
  parameters: DocumentHostPurgeRecycleBinItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<boolean> {
  const path = `/api/v1/document/host/recycle-bin/${encodeURIComponent(String(parameters.id))}/purge`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostPurgeRecycleBinItemResponse(value);
}

export interface DocumentHostRestoreItemParameters {
  readonly itemId: string;
  readonly body: RestoreHostDocumentItemRequest;
}

export async function documentHostRestoreItem(
  http: HttpClient,
  parameters: DocumentHostRestoreItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentItemResponse> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/restore`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentItemResponse(value);
}

export interface DocumentHostRestoreRecycleBinItemParameters {
  readonly id: string;
  readonly body: RestoreHostDocumentItemRequest;
}

export async function documentHostRestoreRecycleBinItem(
  http: HttpClient,
  parameters: DocumentHostRestoreRecycleBinItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentItemResponse> {
  const path = `/api/v1/document/host/recycle-bin/${encodeURIComponent(String(parameters.id))}/restore`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentItemResponse(value);
}

export interface DocumentHostSetDocumentPermissionsParameters {
  readonly body: SetHostDocumentPermissionsRequest;
}

export async function documentHostSetDocumentPermissions(
  http: HttpClient,
  parameters: DocumentHostSetDocumentPermissionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostDocumentPermissionResponse>> {
  const path = `/api/v1/document/host/permissions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDocumentHostSetDocumentPermissionsResponse(value);
}

export interface DocumentHostUpdateCategoryParameters {
  readonly categoryId: string;
  readonly body: UpdateHostDocumentCategoryRequest;
}

export async function documentHostUpdateCategory(
  http: HttpClient,
  parameters: DocumentHostUpdateCategoryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentCategoryResponse> {
  const path = `/api/v1/document/host/categories/${encodeURIComponent(String(parameters.categoryId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentCategoryResponse(value);
}

export interface DocumentHostUpdateDocumentShareStatusParameters {
  readonly id: string;
  readonly body: UpdateHostDocumentShareStatusRequest;
}

export async function documentHostUpdateDocumentShareStatus(
  http: HttpClient,
  parameters: DocumentHostUpdateDocumentShareStatusParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentShareResponse> {
  const path = `/api/v1/document/host/shares/${encodeURIComponent(String(parameters.id))}/status`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentShareResponse(value);
}

export interface DocumentHostUpdateItemParameters {
  readonly itemId: string;
  readonly body: UpdateHostDocumentItemRequest;
}

export async function documentHostUpdateItem(
  http: HttpClient,
  parameters: DocumentHostUpdateItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentItemResponse> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentItemResponse(value);
}

export interface DocumentHostUpdateTagParameters {
  readonly tagId: string;
  readonly body: UpdateHostDocumentTagRequest;
}

export async function documentHostUpdateTag(
  http: HttpClient,
  parameters: DocumentHostUpdateTagParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentTagResponse> {
  const path = `/api/v1/document/host/tags/${encodeURIComponent(String(parameters.tagId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentTagResponse(value);
}

export interface DocumentHostUploadItemVersionParameters {
  readonly itemId: string;
  readonly file?: IFormFile;
}

export async function documentHostUploadItemVersion(
  http: HttpClient,
  parameters: DocumentHostUploadItemVersionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentItemResponse> {
  const path = `/api/v1/document/host/items/${encodeURIComponent(String(parameters.itemId))}/versions/upload`;
  const body = new FormData();
  if (parameters.file !== undefined) {
    body.append('file', parameters.file);
  }
  const init: RequestInit = { method: 'POST', body };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentItemResponse(value);
}

export interface DocumentPublicAccessDocumentShareParameters {
  readonly shareCode: string;
  readonly body: AccessHostDocumentShareRequest;
}

export async function documentPublicAccessDocumentShare(
  http: HttpClient,
  parameters: DocumentPublicAccessDocumentShareParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDocumentShareAccessResponse> {
  const path = `/api/v1/document/public/shares/${encodeURIComponent(String(parameters.shareCode))}/access`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDocumentShareAccessResponse(value);
}

export interface FilesDeleteHostFileParameters {
  readonly fileId: string;
}

export async function filesDeleteHostFile(
  http: HttpClient,
  parameters: FilesDeleteHostFileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostFileResponse> {
  const path = `/api/v1/files/host-files/${encodeURIComponent(String(parameters.fileId))}/delete`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostFileResponse(value);
}

export interface FilesDownloadHostFileContentParameters {
  readonly fileId: string;
}

export async function filesDownloadHostFileContent(
  http: HttpClient,
  parameters: FilesDownloadHostFileContentParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/files/host-files/${encodeURIComponent(String(parameters.fileId))}/content`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface FilesGetHostFileParameters {
  readonly fileId: string;
}

export async function filesGetHostFile(
  http: HttpClient,
  parameters: FilesGetHostFileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostFileResponse> {
  const path = `/api/v1/files/host-files/${encodeURIComponent(String(parameters.fileId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostFileResponse(value);
}

export interface FilesListHostFilesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function filesListHostFiles(
  http: HttpClient,
  parameters: FilesListHostFilesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostFileResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/files/host-files` : `/api/v1/files/host-files?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostFileResponse(value);
}

export interface FilesUploadHostFileParameters {
  readonly file?: IFormFile;
}

export async function filesUploadHostFile(
  http: HttpClient,
  parameters: FilesUploadHostFileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostFileResponse> {
  const path = `/api/v1/files/host-files`;
  const body = new FormData();
  if (parameters.file !== undefined) {
    body.append('file', parameters.file);
  }
  const init: RequestInit = { method: 'POST', body };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostFileResponse(value);
}

export interface IdentityBatchDisableHostUsersParameters {
  readonly body: BatchHostUserIdsRequest;
}

export async function identityBatchDisableHostUsers(
  http: HttpClient,
  parameters: IdentityBatchDisableHostUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<BatchHostUserStatusResponse> {
  const path = `/api/v1/identity/users/batch-disable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readBatchHostUserStatusResponse(value);
}

export interface IdentityBatchEnableHostUsersParameters {
  readonly body: BatchHostUserIdsRequest;
}

export async function identityBatchEnableHostUsers(
  http: HttpClient,
  parameters: IdentityBatchEnableHostUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<BatchHostUserStatusResponse> {
  const path = `/api/v1/identity/users/batch-enable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readBatchHostUserStatusResponse(value);
}

export interface IdentityBeginTotpEnrollmentParameters {

}

export async function identityBeginTotpEnrollment(
  http: HttpClient,
  parameters: IdentityBeginTotpEnrollmentParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<BeginTotpEnrollmentResponse> {
  const path = `/api/v1/identity/me/mfa/totp/begin`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readBeginTotpEnrollmentResponse(value);
}

export interface IdentityConfirmTotpEnrollmentParameters {
  readonly body: ConfirmTotpEnrollmentRequest;
}

export async function identityConfirmTotpEnrollment(
  http: HttpClient,
  parameters: IdentityConfirmTotpEnrollmentParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TotpEnrollmentStatusResponse> {
  const path = `/api/v1/identity/me/mfa/totp/confirm`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTotpEnrollmentStatusResponse(value);
}

export interface IdentityCreateHostApiKeyParameters {
  readonly body: CreateHostApiKeyRequest;
}

export async function identityCreateHostApiKey(
  http: HttpClient,
  parameters: IdentityCreateHostApiKeyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CreateHostApiKeyResponse> {
  const path = `/api/v1/identity/api-keys`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCreateHostApiKeyResponse(value);
}

export interface IdentityCreateHostMenuParameters {
  readonly body: CreateHostMenuRequest;
}

export async function identityCreateHostMenu(
  http: HttpClient,
  parameters: IdentityCreateHostMenuParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostMenuResponse> {
  const path = `/api/v1/identity/menus`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostMenuResponse(value);
}

export interface IdentityCreateHostRoleParameters {
  readonly body: CreateHostRoleRequest;
}

export async function identityCreateHostRole(
  http: HttpClient,
  parameters: IdentityCreateHostRoleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleResponse> {
  const path = `/api/v1/identity/roles`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleResponse(value);
}

export interface IdentityCreateHostUserParameters {
  readonly body: CreateHostUserRequest;
}

export async function identityCreateHostUser(
  http: HttpClient,
  parameters: IdentityCreateHostUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserResponse> {
  const path = `/api/v1/identity/users`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserResponse(value);
}

export interface IdentityDisableHostApiKeyParameters {
  readonly apiKeyId: string;
}

export async function identityDisableHostApiKey(
  http: HttpClient,
  parameters: IdentityDisableHostApiKeyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostApiKeyResponse> {
  const path = `/api/v1/identity/api-keys/${encodeURIComponent(String(parameters.apiKeyId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostApiKeyResponse(value);
}

export interface IdentityDisableHostMenuParameters {
  readonly menuId: string;
}

export async function identityDisableHostMenu(
  http: HttpClient,
  parameters: IdentityDisableHostMenuParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostMenuResponse> {
  const path = `/api/v1/identity/menus/${encodeURIComponent(String(parameters.menuId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostMenuResponse(value);
}

export interface IdentityDisableHostRoleParameters {
  readonly roleId: string;
}

export async function identityDisableHostRole(
  http: HttpClient,
  parameters: IdentityDisableHostRoleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleResponse(value);
}

export interface IdentityDisableHostUserParameters {
  readonly userId: string;
}

export async function identityDisableHostUser(
  http: HttpClient,
  parameters: IdentityDisableHostUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserResponse(value);
}

export interface IdentityDownloadHostUserImportTemplateParameters {

}

export async function identityDownloadHostUserImportTemplate(
  http: HttpClient,
  parameters: IdentityDownloadHostUserImportTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/identity/users/import-template`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface IdentityEnableHostMenuParameters {
  readonly menuId: string;
}

export async function identityEnableHostMenu(
  http: HttpClient,
  parameters: IdentityEnableHostMenuParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostMenuResponse> {
  const path = `/api/v1/identity/menus/${encodeURIComponent(String(parameters.menuId))}/enable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostMenuResponse(value);
}

export interface IdentityEnableHostUserParameters {
  readonly userId: string;
}

export async function identityEnableHostUser(
  http: HttpClient,
  parameters: IdentityEnableHostUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}/enable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserResponse(value);
}

export interface IdentityExportHostUsersParameters {

}

export async function identityExportHostUsers(
  http: HttpClient,
  parameters: IdentityExportHostUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostUserResponse>> {
  const path = `/api/v1/identity/users/export`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityExportHostUsersResponse(value);
}

export interface IdentityExportHostUsersWorkbookParameters {

}

export async function identityExportHostUsersWorkbook(
  http: HttpClient,
  parameters: IdentityExportHostUsersWorkbookParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/identity/users/export-file`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface IdentityGetAuthorizationTreeParameters {

}

export async function identityGetAuthorizationTree(
  http: HttpClient,
  parameters: IdentityGetAuthorizationTreeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<AuthorizationTreeModuleResponse>> {
  const path = `/api/v1/identity/authorization-tree`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityGetAuthorizationTreeResponse(value);
}

export interface IdentityGetCurrentUserParameters {

}

export async function identityGetCurrentUser(
  http: HttpClient,
  parameters: IdentityGetCurrentUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CurrentUserResponse> {
  const path = `/api/v1/me`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCurrentUserResponse(value);
}

export interface IdentityGetHostMenuParameters {
  readonly menuId: string;
}

export async function identityGetHostMenu(
  http: HttpClient,
  parameters: IdentityGetHostMenuParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostMenuResponse> {
  const path = `/api/v1/identity/menus/${encodeURIComponent(String(parameters.menuId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostMenuResponse(value);
}

export interface IdentityGetHostModuleParameters {
  readonly moduleKey: string;
}

export async function identityGetHostModule(
  http: HttpClient,
  parameters: IdentityGetHostModuleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ModuleCatalogEntryResponse> {
  const path = `/api/v1/identity/modules/${encodeURIComponent(String(parameters.moduleKey))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readModuleCatalogEntryResponse(value);
}

export interface IdentityGetHostRoleParameters {
  readonly roleId: string;
}

export async function identityGetHostRole(
  http: HttpClient,
  parameters: IdentityGetHostRoleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleResponse(value);
}

export interface IdentityGetHostRoleDataScopeParameters {
  readonly roleId: string;
}

export async function identityGetHostRoleDataScope(
  http: HttpClient,
  parameters: IdentityGetHostRoleDataScopeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleDataScopeResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/data-scope`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleDataScopeResponse(value);
}

export interface IdentityGetHostRoleFieldGrantsParameters {
  readonly roleId: string;
  readonly resourceKey: string;
}

export async function identityGetHostRoleFieldGrants(
  http: HttpClient,
  parameters: IdentityGetHostRoleFieldGrantsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleFieldGrantsResponse> {
  const query = new URLSearchParams();
  query.set('resourceKey', String(parameters.resourceKey));
  const path = query.size === 0 ? `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/field-grants` : `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/field-grants?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleFieldGrantsResponse(value);
}

export interface IdentityGetHostUserParameters {
  readonly userId: string;
}

export async function identityGetHostUser(
  http: HttpClient,
  parameters: IdentityGetHostUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserResponse(value);
}

export interface IdentityGetHostUserRolesParameters {
  readonly userId: string;
}

export async function identityGetHostUserRoles(
  http: HttpClient,
  parameters: IdentityGetHostUserRolesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserRolesResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}/roles`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserRolesResponse(value);
}

export interface IdentityGetTotpEnrollmentStatusParameters {

}

export async function identityGetTotpEnrollmentStatus(
  http: HttpClient,
  parameters: IdentityGetTotpEnrollmentStatusParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TotpEnrollmentStatusResponse> {
  const path = `/api/v1/identity/me/mfa/totp`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTotpEnrollmentStatusResponse(value);
}

export interface IdentityGrantSuperAdministratorParameters {
  readonly body: GrantSuperAdministratorRequest;
}

export async function identityGrantSuperAdministrator(
  http: HttpClient,
  parameters: IdentityGrantSuperAdministratorParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SuperAdministratorChangeResponse> {
  const path = `/api/v1/identity/super-administrators/grant`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSuperAdministratorChangeResponse(value);
}

export interface IdentityImportHostUsersParameters {
  readonly body: ImportHostUsersRequest;
}

export async function identityImportHostUsers(
  http: HttpClient,
  parameters: IdentityImportHostUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ImportHostUsersResponse> {
  const path = `/api/v1/identity/users/import`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readImportHostUsersResponse(value);
}

export interface IdentityImportHostUsersWorkbookParameters {
  readonly file: IFormFile;
}

export async function identityImportHostUsersWorkbook(
  http: HttpClient,
  parameters: IdentityImportHostUsersWorkbookParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ImportHostUsersResponse> {
  const path = `/api/v1/identity/users/import-file`;
  const body = new FormData();
  body.append('file', parameters.file);
  const init: RequestInit = { method: 'POST', body };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readImportHostUsersResponse(value);
}

export interface IdentityListAllHostMenusParameters {

}

export async function identityListAllHostMenus(
  http: HttpClient,
  parameters: IdentityListAllHostMenusParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostMenuResponse>> {
  const path = `/api/v1/identity/menus/all`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityListAllHostMenusResponse(value);
}

export interface IdentityListFieldProjectionCatalogParameters {

}

export async function identityListFieldProjectionCatalog(
  http: HttpClient,
  parameters: IdentityListFieldProjectionCatalogParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<FieldProjectionResourceDefinition>> {
  const path = `/api/v1/identity/field-projections/catalog`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityListFieldProjectionCatalogResponse(value);
}

export interface IdentityListHostApiKeysParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly userId?: string;
  readonly displayNameContains?: string;
}

export async function identityListHostApiKeys(
  http: HttpClient,
  parameters: IdentityListHostApiKeysParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostApiKeyResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.userId !== undefined) {
    query.set('userId', String(parameters.userId));
  }
  if (parameters.displayNameContains !== undefined) {
    query.set('displayNameContains', String(parameters.displayNameContains));
  }
  const path = query.size === 0 ? `/api/v1/identity/api-keys` : `/api/v1/identity/api-keys?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostApiKeyResponse(value);
}

export interface IdentityListHostMenuPermissionOptionsParameters {

}

export async function identityListHostMenuPermissionOptions(
  http: HttpClient,
  parameters: IdentityListHostMenuPermissionOptionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostMenuPermissionOptionResponse>> {
  const path = `/api/v1/identity/menus/permission-options`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityListHostMenuPermissionOptionsResponse(value);
}

export interface IdentityListHostMenusParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function identityListHostMenus(
  http: HttpClient,
  parameters: IdentityListHostMenusParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostMenuResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/identity/menus` : `/api/v1/identity/menus?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostMenuResponse(value);
}

export interface IdentityListHostModulesParameters {

}

export async function identityListHostModules(
  http: HttpClient,
  parameters: IdentityListHostModulesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<ModuleCatalogEntryResponse>> {
  const path = `/api/v1/identity/modules`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityListHostModulesResponse(value);
}

export interface IdentityListHostOnlineSessionsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly usernameContains?: string;
}

export async function identityListHostOnlineSessions(
  http: HttpClient,
  parameters: IdentityListHostOnlineSessionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostOnlineSessionResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.usernameContains !== undefined) {
    query.set('usernameContains', String(parameters.usernameContains));
  }
  const path = query.size === 0 ? `/api/v1/identity/online-sessions` : `/api/v1/identity/online-sessions?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostOnlineSessionResponse(value);
}

export interface IdentityListHostRolesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function identityListHostRoles(
  http: HttpClient,
  parameters: IdentityListHostRolesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostRoleResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/identity/roles` : `/api/v1/identity/roles?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostRoleResponse(value);
}

export interface IdentityListHostUsersParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function identityListHostUsers(
  http: HttpClient,
  parameters: IdentityListHostUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostUserResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/identity/users` : `/api/v1/identity/users?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostUserResponse(value);
}

export interface IdentityListSuperAdministratorAuditsParameters {
  readonly limit?: number;
}

export async function identityListSuperAdministratorAudits(
  http: HttpClient,
  parameters: IdentityListSuperAdministratorAuditsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<SuperAdministratorAuditResponse>> {
  const query = new URLSearchParams();
  if (parameters.limit !== undefined) {
    query.set('limit', String(parameters.limit));
  }
  const path = query.size === 0 ? `/api/v1/identity/super-administrators/audits` : `/api/v1/identity/super-administrators/audits?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityListSuperAdministratorAuditsResponse(value);
}

export interface IdentityListSuperAdministratorsParameters {

}

export async function identityListSuperAdministrators(
  http: HttpClient,
  parameters: IdentityListSuperAdministratorsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<SuperAdministratorResponse>> {
  const path = `/api/v1/identity/super-administrators`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readIdentityListSuperAdministratorsResponse(value);
}

export interface IdentityLoginParameters {
  readonly body: LoginRequest;
}

export async function identityLogin(
  http: HttpClient,
  parameters: IdentityLoginParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TokenResponse> {
  const path = `/api/v1/auth/login`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTokenResponse(value);
}

export interface IdentityLogoutParameters {

}

export async function identityLogout(
  http: HttpClient,
  parameters: IdentityLogoutParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/auth/logout`;
  const init: RequestInit = { method: 'POST' };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface IdentityRefreshSessionParameters {

}

export async function identityRefreshSession(
  http: HttpClient,
  parameters: IdentityRefreshSessionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TokenResponse> {
  const path = `/api/v1/auth/refresh`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTokenResponse(value);
}

export interface IdentityReplaceHostRoleFieldGrantsParameters {
  readonly roleId: string;
  readonly body: ReplaceHostRoleFieldGrantsRequest;
}

export async function identityReplaceHostRoleFieldGrants(
  http: HttpClient,
  parameters: IdentityReplaceHostRoleFieldGrantsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleFieldGrantsResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/field-grants`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleFieldGrantsResponse(value);
}

export interface IdentityReplaceHostRolePermissionsParameters {
  readonly roleId: string;
  readonly body: ReplaceHostRolePermissionsRequest;
}

export async function identityReplaceHostRolePermissions(
  http: HttpClient,
  parameters: IdentityReplaceHostRolePermissionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/permissions`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleResponse(value);
}

export interface IdentityReplaceHostUserRolesParameters {
  readonly userId: string;
  readonly body: ReplaceHostUserRolesRequest;
}

export async function identityReplaceHostUserRoles(
  http: HttpClient,
  parameters: IdentityReplaceHostUserRolesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserRolesResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}/roles`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserRolesResponse(value);
}

export interface IdentityResetHostUserPasswordParameters {
  readonly userId: string;
  readonly body: ResetHostUserPasswordRequest;
}

export async function identityResetHostUserPassword(
  http: HttpClient,
  parameters: IdentityResetHostUserPasswordParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}/reset-password`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserResponse(value);
}

export interface IdentityRevokeHostOnlineSessionParameters {
  readonly sessionId: string;
}

export async function identityRevokeHostOnlineSession(
  http: HttpClient,
  parameters: IdentityRevokeHostOnlineSessionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostOnlineSessionResponse> {
  const path = `/api/v1/identity/online-sessions/${encodeURIComponent(String(parameters.sessionId))}/revoke`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostOnlineSessionResponse(value);
}

export interface IdentityRevokeSuperAdministratorParameters {
  readonly targetUserId: string;
  readonly body: RevokeSuperAdministratorRequest;
}

export async function identityRevokeSuperAdministrator(
  http: HttpClient,
  parameters: IdentityRevokeSuperAdministratorParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SuperAdministratorChangeResponse> {
  const path = `/api/v1/identity/super-administrators/${encodeURIComponent(String(parameters.targetUserId))}/revoke`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSuperAdministratorChangeResponse(value);
}

export interface IdentityRotateHostApiKeyParameters {
  readonly apiKeyId: string;
}

export async function identityRotateHostApiKey(
  http: HttpClient,
  parameters: IdentityRotateHostApiKeyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CreateHostApiKeyResponse> {
  const path = `/api/v1/identity/api-keys/${encodeURIComponent(String(parameters.apiKeyId))}/rotate`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readCreateHostApiKeyResponse(value);
}

export interface IdentitySyncHostMenuCatalogParameters {

}

export async function identitySyncHostMenuCatalog(
  http: HttpClient,
  parameters: IdentitySyncHostMenuCatalogParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostNavigationCatalogSyncResponse> {
  const path = `/api/v1/identity/menus/sync-catalog`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostNavigationCatalogSyncResponse(value);
}

export interface IdentityUpdateHostMenuParameters {
  readonly menuId: string;
  readonly body: UpdateHostMenuRequest;
}

export async function identityUpdateHostMenu(
  http: HttpClient,
  parameters: IdentityUpdateHostMenuParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostMenuResponse> {
  const path = `/api/v1/identity/menus/${encodeURIComponent(String(parameters.menuId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostMenuResponse(value);
}

export interface IdentityUpdateHostRoleParameters {
  readonly roleId: string;
  readonly body: UpdateHostRoleRequest;
}

export async function identityUpdateHostRole(
  http: HttpClient,
  parameters: IdentityUpdateHostRoleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleResponse(value);
}

export interface IdentityUpdateHostRoleDataScopeParameters {
  readonly roleId: string;
  readonly body: UpdateHostRoleDataScopeRequest;
}

export async function identityUpdateHostRoleDataScope(
  http: HttpClient,
  parameters: IdentityUpdateHostRoleDataScopeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostRoleDataScopeResponse> {
  const path = `/api/v1/identity/roles/${encodeURIComponent(String(parameters.roleId))}/data-scope`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostRoleDataScopeResponse(value);
}

export interface IdentityUpdateHostUserParameters {
  readonly userId: string;
  readonly body: UpdateHostUserRequest;
}

export async function identityUpdateHostUser(
  http: HttpClient,
  parameters: IdentityUpdateHostUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserResponse> {
  const path = `/api/v1/identity/users/${encodeURIComponent(String(parameters.userId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserResponse(value);
}

export interface IdentityUpdatePreferredLocaleParameters {
  readonly body: UpdateLocaleRequest;
}

export async function identityUpdatePreferredLocale(
  http: HttpClient,
  parameters: IdentityUpdatePreferredLocaleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<LocalePreferenceResponse> {
  const path = `/api/v1/me/locale`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readLocalePreferenceResponse(value);
}

export interface JobsClearHostJobExecutionsParameters {
  readonly jobDefinitionId: string;
}

export async function jobsClearHostJobExecutions(
  http: HttpClient,
  parameters: JobsClearHostJobExecutionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const query = new URLSearchParams();
  query.set('jobDefinitionId', String(parameters.jobDefinitionId));
  const path = query.size === 0 ? `/api/v1/jobs/host-executions/clear` : `/api/v1/jobs/host-executions/clear?${query.toString()}`;
  const init: RequestInit = { method: 'POST' };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface JobsCreateHostJobDefinitionParameters {
  readonly body: CreateHostJobDefinitionRequest;
}

export async function jobsCreateHostJobDefinition(
  http: HttpClient,
  parameters: JobsCreateHostJobDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobDefinitionResponse> {
  const path = `/api/v1/jobs/host-definitions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobDefinitionResponse(value);
}

export interface JobsCreateHostJobScheduleParameters {
  readonly body: CreateHostJobScheduleRequest;
}

export async function jobsCreateHostJobSchedule(
  http: HttpClient,
  parameters: JobsCreateHostJobScheduleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobScheduleResponse> {
  const path = `/api/v1/jobs/host-schedules`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobScheduleResponse(value);
}

export interface JobsDeleteHostJobDefinitionParameters {
  readonly definitionId: string;
  readonly body: DeleteHostJobDefinitionRequest;
}

export async function jobsDeleteHostJobDefinition(
  http: HttpClient,
  parameters: JobsDeleteHostJobDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/jobs/host-definitions/${encodeURIComponent(String(parameters.definitionId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface JobsDeleteHostJobScheduleParameters {
  readonly scheduleId: string;
  readonly body: ChangeHostJobScheduleStateRequest;
}

export async function jobsDeleteHostJobSchedule(
  http: HttpClient,
  parameters: JobsDeleteHostJobScheduleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/jobs/host-schedules/${encodeURIComponent(String(parameters.scheduleId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface JobsDisableHostJobDefinitionParameters {
  readonly definitionId: string;
  readonly body: DisableHostJobDefinitionRequest;
}

export async function jobsDisableHostJobDefinition(
  http: HttpClient,
  parameters: JobsDisableHostJobDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobDefinitionResponse> {
  const path = `/api/v1/jobs/host-definitions/${encodeURIComponent(String(parameters.definitionId))}/disable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobDefinitionResponse(value);
}

export interface JobsGetHostJobExecutionParameters {
  readonly executionId: string;
}

export async function jobsGetHostJobExecution(
  http: HttpClient,
  parameters: JobsGetHostJobExecutionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobExecutionResponse> {
  const path = `/api/v1/jobs/host-executions/${encodeURIComponent(String(parameters.executionId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobExecutionResponse(value);
}

export interface JobsGetHostJobHealthParameters {

}

export async function jobsGetHostJobHealth(
  http: HttpClient,
  parameters: JobsGetHostJobHealthParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobHealthResponse> {
  const path = `/api/v1/jobs/host-health`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobHealthResponse(value);
}

export interface JobsListHostJobDefinitionsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function jobsListHostJobDefinitions(
  http: HttpClient,
  parameters: JobsListHostJobDefinitionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostJobDefinitionResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/jobs/host-definitions` : `/api/v1/jobs/host-definitions?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostJobDefinitionResponse(value);
}

export interface JobsListHostJobExecutionsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly jobDefinitionId?: string;
  readonly jobScheduleId?: string;
  readonly status?: string;
  readonly fromUtc?: string;
  readonly toUtc?: string;
}

export async function jobsListHostJobExecutions(
  http: HttpClient,
  parameters: JobsListHostJobExecutionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostJobExecutionResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.jobDefinitionId !== undefined) {
    query.set('jobDefinitionId', String(parameters.jobDefinitionId));
  }
  if (parameters.jobScheduleId !== undefined) {
    query.set('jobScheduleId', String(parameters.jobScheduleId));
  }
  if (parameters.status !== undefined) {
    query.set('status', String(parameters.status));
  }
  if (parameters.fromUtc !== undefined) {
    query.set('fromUtc', String(parameters.fromUtc));
  }
  if (parameters.toUtc !== undefined) {
    query.set('toUtc', String(parameters.toUtc));
  }
  const path = query.size === 0 ? `/api/v1/jobs/host-executions` : `/api/v1/jobs/host-executions?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostJobExecutionResponse(value);
}

export interface JobsListHostJobGroupsParameters {

}

export async function jobsListHostJobGroups(
  http: HttpClient,
  parameters: JobsListHostJobGroupsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostJobGroupResponse>> {
  const path = `/api/v1/jobs/host-definitions/groups`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readJobsListHostJobGroupsResponse(value);
}

export interface JobsListHostJobScheduleDefinitionOptionsParameters {

}

export async function jobsListHostJobScheduleDefinitionOptions(
  http: HttpClient,
  parameters: JobsListHostJobScheduleDefinitionOptionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<HostJobScheduleDefinitionOptionResponse>> {
  const path = `/api/v1/jobs/host-schedules/definition-options`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readJobsListHostJobScheduleDefinitionOptionsResponse(value);
}

export interface JobsListHostJobSchedulesParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly jobDefinitionId?: string;
  readonly search?: string;
  readonly isEnabled?: boolean;
  readonly triggerKind?: string;
}

export async function jobsListHostJobSchedules(
  http: HttpClient,
  parameters: JobsListHostJobSchedulesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostJobScheduleResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.jobDefinitionId !== undefined) {
    query.set('jobDefinitionId', String(parameters.jobDefinitionId));
  }
  if (parameters.search !== undefined) {
    query.set('search', String(parameters.search));
  }
  if (parameters.isEnabled !== undefined) {
    query.set('isEnabled', String(parameters.isEnabled));
  }
  if (parameters.triggerKind !== undefined) {
    query.set('triggerKind', String(parameters.triggerKind));
  }
  const path = query.size === 0 ? `/api/v1/jobs/host-schedules` : `/api/v1/jobs/host-schedules?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostJobScheduleResponse(value);
}

export interface JobsPauseHostJobScheduleParameters {
  readonly scheduleId: string;
  readonly body: ChangeHostJobScheduleStateRequest;
}

export async function jobsPauseHostJobSchedule(
  http: HttpClient,
  parameters: JobsPauseHostJobScheduleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobScheduleResponse> {
  const path = `/api/v1/jobs/host-schedules/${encodeURIComponent(String(parameters.scheduleId))}/pause`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobScheduleResponse(value);
}

export interface JobsPreviewHostJobScheduleCronParameters {
  readonly cronExpression: string;
  readonly timeZoneId: string;
  readonly occurrenceCount?: number;
}

export async function jobsPreviewHostJobScheduleCron(
  http: HttpClient,
  parameters: JobsPreviewHostJobScheduleCronParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobScheduleCronPreviewResponse> {
  const query = new URLSearchParams();
  query.set('cronExpression', String(parameters.cronExpression));
  query.set('timeZoneId', String(parameters.timeZoneId));
  if (parameters.occurrenceCount !== undefined) {
    query.set('occurrenceCount', String(parameters.occurrenceCount));
  }
  const path = query.size === 0 ? `/api/v1/jobs/host-schedules/cron-preview` : `/api/v1/jobs/host-schedules/cron-preview?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobScheduleCronPreviewResponse(value);
}

export interface JobsResumeHostJobScheduleParameters {
  readonly scheduleId: string;
  readonly body: ChangeHostJobScheduleStateRequest;
}

export async function jobsResumeHostJobSchedule(
  http: HttpClient,
  parameters: JobsResumeHostJobScheduleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobScheduleResponse> {
  const path = `/api/v1/jobs/host-schedules/${encodeURIComponent(String(parameters.scheduleId))}/resume`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobScheduleResponse(value);
}

export interface JobsTriggerHostJobDefinitionParameters {
  readonly definitionId: string;
}

export async function jobsTriggerHostJobDefinition(
  http: HttpClient,
  parameters: JobsTriggerHostJobDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobExecutionResponse> {
  const path = `/api/v1/jobs/host-definitions/${encodeURIComponent(String(parameters.definitionId))}/trigger`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobExecutionResponse(value);
}

export interface JobsUpdateHostJobDefinitionParameters {
  readonly definitionId: string;
  readonly body: UpdateHostJobDefinitionRequest;
}

export async function jobsUpdateHostJobDefinition(
  http: HttpClient,
  parameters: JobsUpdateHostJobDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobDefinitionResponse> {
  const path = `/api/v1/jobs/host-definitions/${encodeURIComponent(String(parameters.definitionId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobDefinitionResponse(value);
}

export interface JobsUpdateHostJobScheduleParameters {
  readonly scheduleId: string;
  readonly body: UpdateHostJobScheduleRequest;
}

export async function jobsUpdateHostJobSchedule(
  http: HttpClient,
  parameters: JobsUpdateHostJobScheduleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostJobScheduleResponse> {
  const path = `/api/v1/jobs/host-schedules/${encodeURIComponent(String(parameters.scheduleId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostJobScheduleResponse(value);
}

export interface NotificationsCreateBindingParameters {
  readonly body: CreateNotificationBindingRequest;
}

export async function notificationsCreateBinding(
  http: HttpClient,
  parameters: NotificationsCreateBindingParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationBindingResponse> {
  const path = `/api/v1/notifications/bindings`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationBindingResponse(value);
}

export interface NotificationsCreateHostAnnouncementParameters {
  readonly body: CreateHostAnnouncementRequest;
}

export async function notificationsCreateHostAnnouncement(
  http: HttpClient,
  parameters: NotificationsCreateHostAnnouncementParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostAnnouncementResponse> {
  const path = `/api/v1/notifications/host-announcements`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostAnnouncementResponse(value);
}

export interface NotificationsCreateMyRecipientEndpointParameters {
  readonly body: CreateMyRecipientEndpointRequest;
}

export async function notificationsCreateMyRecipientEndpoint(
  http: HttpClient,
  parameters: NotificationsCreateMyRecipientEndpointParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<RecipientEndpointResponse> {
  const path = `/api/v1/notifications/my-recipient-endpoints`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readRecipientEndpointResponse(value);
}

export interface NotificationsCreateProviderProfileParameters {
  readonly body: CreateNotificationProviderProfileRequest;
}

export async function notificationsCreateProviderProfile(
  http: HttpClient,
  parameters: NotificationsCreateProviderProfileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationProviderProfileResponse> {
  const path = `/api/v1/notifications/provider-profiles`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationProviderProfileResponse(value);
}

export interface NotificationsCreateTemplateParameters {
  readonly body: CreateNotificationTemplateRequest;
}

export async function notificationsCreateTemplate(
  http: HttpClient,
  parameters: NotificationsCreateTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationTemplateResponse> {
  const path = `/api/v1/notifications/templates`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationTemplateResponse(value);
}

export interface NotificationsDeleteMyRecipientEndpointParameters {
  readonly endpointId: string;
}

export async function notificationsDeleteMyRecipientEndpoint(
  http: HttpClient,
  parameters: NotificationsDeleteMyRecipientEndpointParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/notifications/my-recipient-endpoints/${encodeURIComponent(String(parameters.endpointId))}`;
  const init: RequestInit = { method: 'DELETE' };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface NotificationsDisableProviderProfileParameters {
  readonly profileId: string;
  readonly body: SetNotificationProviderProfileEnabledRequest;
}

export async function notificationsDisableProviderProfile(
  http: HttpClient,
  parameters: NotificationsDisableProviderProfileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationProviderProfileResponse> {
  const path = `/api/v1/notifications/provider-profiles/${encodeURIComponent(String(parameters.profileId))}/disable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationProviderProfileResponse(value);
}

export interface NotificationsEnableProviderProfileParameters {
  readonly profileId: string;
  readonly body: SetNotificationProviderProfileEnabledRequest;
}

export async function notificationsEnableProviderProfile(
  http: HttpClient,
  parameters: NotificationsEnableProviderProfileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationProviderProfileResponse> {
  const path = `/api/v1/notifications/provider-profiles/${encodeURIComponent(String(parameters.profileId))}/enable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationProviderProfileResponse(value);
}

export interface NotificationsGetBindingParameters {
  readonly bindingId: string;
}

export async function notificationsGetBinding(
  http: HttpClient,
  parameters: NotificationsGetBindingParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationBindingResponse> {
  const path = `/api/v1/notifications/bindings/${encodeURIComponent(String(parameters.bindingId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationBindingResponse(value);
}

export interface NotificationsGetDeliveryParameters {
  readonly deliveryId: string;
}

export async function notificationsGetDelivery(
  http: HttpClient,
  parameters: NotificationsGetDeliveryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationDeliveryResponse> {
  const path = `/api/v1/notifications/deliveries/${encodeURIComponent(String(parameters.deliveryId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationDeliveryResponse(value);
}

export interface NotificationsGetMyInboxUnreadCountParameters {

}

export async function notificationsGetMyInboxUnreadCount(
  http: HttpClient,
  parameters: NotificationsGetMyInboxUnreadCountParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxUnreadCountResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/unread-count`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readInboxUnreadCountResponse(value);
}

export interface NotificationsGetProviderProfileParameters {
  readonly profileId: string;
}

export async function notificationsGetProviderProfile(
  http: HttpClient,
  parameters: NotificationsGetProviderProfileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationProviderProfileResponse> {
  const path = `/api/v1/notifications/provider-profiles/${encodeURIComponent(String(parameters.profileId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationProviderProfileResponse(value);
}

export interface NotificationsGetTemplateParameters {
  readonly templateId: string;
}

export async function notificationsGetTemplate(
  http: HttpClient,
  parameters: NotificationsGetTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationTemplateResponse> {
  const path = `/api/v1/notifications/templates/${encodeURIComponent(String(parameters.templateId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationTemplateResponse(value);
}

export interface NotificationsListBindingsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function notificationsListBindings(
  http: HttpClient,
  parameters: NotificationsListBindingsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfNotificationBindingResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/notifications/bindings` : `/api/v1/notifications/bindings?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfNotificationBindingResponse(value);
}

export interface NotificationsListDeliveriesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function notificationsListDeliveries(
  http: HttpClient,
  parameters: NotificationsListDeliveriesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfNotificationDeliveryResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/notifications/deliveries` : `/api/v1/notifications/deliveries?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfNotificationDeliveryResponse(value);
}

export interface NotificationsListHostAnnouncementsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly title?: string;
  readonly status?: string;
  readonly kind?: string;
  readonly audienceKind?: string;
}

export async function notificationsListHostAnnouncements(
  http: HttpClient,
  parameters: NotificationsListHostAnnouncementsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfHostAnnouncementResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.title !== undefined) {
    query.set('title', String(parameters.title));
  }
  if (parameters.status !== undefined) {
    query.set('status', String(parameters.status));
  }
  if (parameters.kind !== undefined) {
    query.set('kind', String(parameters.kind));
  }
  if (parameters.audienceKind !== undefined) {
    query.set('audienceKind', String(parameters.audienceKind));
  }
  const path = query.size === 0 ? `/api/v1/notifications/host-announcements` : `/api/v1/notifications/host-announcements?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfHostAnnouncementResponse(value);
}

export interface NotificationsListMyInboxMessagesParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly title?: string;
  readonly status?: string;
}

export async function notificationsListMyInboxMessages(
  http: HttpClient,
  parameters: NotificationsListMyInboxMessagesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfInboxMessageResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.title !== undefined) {
    query.set('title', String(parameters.title));
  }
  if (parameters.status !== undefined) {
    query.set('status', String(parameters.status));
  }
  const path = query.size === 0 ? `/api/v1/notifications/my-inbox-messages` : `/api/v1/notifications/my-inbox-messages?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfInboxMessageResponse(value);
}

export interface NotificationsListMyRecipientEndpointsParameters {

}

export async function notificationsListMyRecipientEndpoints(
  http: HttpClient,
  parameters: NotificationsListMyRecipientEndpointsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<RecipientEndpointResponse>> {
  const path = `/api/v1/notifications/my-recipient-endpoints`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationsListMyRecipientEndpointsResponse(value);
}

export interface NotificationsListProviderProfilesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function notificationsListProviderProfiles(
  http: HttpClient,
  parameters: NotificationsListProviderProfilesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfNotificationProviderProfileResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/notifications/provider-profiles` : `/api/v1/notifications/provider-profiles?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfNotificationProviderProfileResponse(value);
}

export interface NotificationsListProviderTypesParameters {

}

export async function notificationsListProviderTypes(
  http: HttpClient,
  parameters: NotificationsListProviderTypesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<NotificationProviderTypeDescriptor>> {
  const path = `/api/v1/notifications/provider-types`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationsListProviderTypesResponse(value);
}

export interface NotificationsListTemplatesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function notificationsListTemplates(
  http: HttpClient,
  parameters: NotificationsListTemplatesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfNotificationTemplateResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/notifications/templates` : `/api/v1/notifications/templates?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfNotificationTemplateResponse(value);
}

export interface NotificationsMarkAllMyInboxMessagesReadParameters {

}

export async function notificationsMarkAllMyInboxMessagesRead(
  http: HttpClient,
  parameters: NotificationsMarkAllMyInboxMessagesReadParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxUnreadCountResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/read-all`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readInboxUnreadCountResponse(value);
}

export interface NotificationsMarkMyInboxMessageReadParameters {
  readonly messageId: string;
}

export async function notificationsMarkMyInboxMessageRead(
  http: HttpClient,
  parameters: NotificationsMarkMyInboxMessageReadParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxMessageResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/${encodeURIComponent(String(parameters.messageId))}/read`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readInboxMessageResponse(value);
}

export interface NotificationsPublishBindingParameters {
  readonly bindingId: string;
  readonly body: PublishNotificationBindingRequest;
}

export async function notificationsPublishBinding(
  http: HttpClient,
  parameters: NotificationsPublishBindingParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationBindingResponse> {
  const path = `/api/v1/notifications/bindings/${encodeURIComponent(String(parameters.bindingId))}/publish`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationBindingResponse(value);
}

export interface NotificationsPublishHostAnnouncementParameters {
  readonly announcementId: string;
  readonly body: PublishHostAnnouncementRequest;
}

export async function notificationsPublishHostAnnouncement(
  http: HttpClient,
  parameters: NotificationsPublishHostAnnouncementParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostAnnouncementResponse> {
  const path = `/api/v1/notifications/host-announcements/${encodeURIComponent(String(parameters.announcementId))}/publish`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostAnnouncementResponse(value);
}

export interface NotificationsPublishProviderProfileParameters {
  readonly profileId: string;
  readonly body: PublishNotificationProviderProfileRequest;
}

export async function notificationsPublishProviderProfile(
  http: HttpClient,
  parameters: NotificationsPublishProviderProfileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationProviderProfileResponse> {
  const path = `/api/v1/notifications/provider-profiles/${encodeURIComponent(String(parameters.profileId))}/publish`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationProviderProfileResponse(value);
}

export interface NotificationsPublishTemplateParameters {
  readonly templateId: string;
  readonly body: PublishNotificationTemplateRequest;
}

export async function notificationsPublishTemplate(
  http: HttpClient,
  parameters: NotificationsPublishTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationTemplateResponse> {
  const path = `/api/v1/notifications/templates/${encodeURIComponent(String(parameters.templateId))}/publish`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationTemplateResponse(value);
}

export interface NotificationsRetryDeliveryParameters {
  readonly deliveryId: string;
  readonly body: RetryNotificationDeliveryRequest;
}

export async function notificationsRetryDelivery(
  http: HttpClient,
  parameters: NotificationsRetryDeliveryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationDeliveryResponse> {
  const path = `/api/v1/notifications/deliveries/${encodeURIComponent(String(parameters.deliveryId))}/retry`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationDeliveryResponse(value);
}

export interface NotificationsSendHostInboxMessageParameters {
  readonly body: SendHostInboxMessageRequest;
}

export async function notificationsSendHostInboxMessage(
  http: HttpClient,
  parameters: NotificationsSendHostInboxMessageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxMessageResponse> {
  const path = `/api/v1/notifications/host-inbox-messages`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readInboxMessageResponse(value);
}

export interface NotificationsSendMyRecipientEndpointVerificationParameters {
  readonly endpointId: string;
}

export async function notificationsSendMyRecipientEndpointVerification(
  http: HttpClient,
  parameters: NotificationsSendMyRecipientEndpointVerificationParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SendRecipientEndpointVerificationResponse> {
  const path = `/api/v1/notifications/my-recipient-endpoints/${encodeURIComponent(String(parameters.endpointId))}/verification/send`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSendRecipientEndpointVerificationResponse(value);
}

export interface NotificationsUpdateBindingParameters {
  readonly bindingId: string;
  readonly body: UpdateNotificationBindingRequest;
}

export async function notificationsUpdateBinding(
  http: HttpClient,
  parameters: NotificationsUpdateBindingParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationBindingResponse> {
  const path = `/api/v1/notifications/bindings/${encodeURIComponent(String(parameters.bindingId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationBindingResponse(value);
}

export interface NotificationsUpdateHostAnnouncementParameters {
  readonly announcementId: string;
  readonly body: UpdateHostAnnouncementRequest;
}

export async function notificationsUpdateHostAnnouncement(
  http: HttpClient,
  parameters: NotificationsUpdateHostAnnouncementParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostAnnouncementResponse> {
  const path = `/api/v1/notifications/host-announcements/${encodeURIComponent(String(parameters.announcementId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostAnnouncementResponse(value);
}

export interface NotificationsUpdateProviderProfileParameters {
  readonly profileId: string;
  readonly body: UpdateNotificationProviderProfileRequest;
}

export async function notificationsUpdateProviderProfile(
  http: HttpClient,
  parameters: NotificationsUpdateProviderProfileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationProviderProfileResponse> {
  const path = `/api/v1/notifications/provider-profiles/${encodeURIComponent(String(parameters.profileId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationProviderProfileResponse(value);
}

export interface NotificationsUpdateTemplateParameters {
  readonly templateId: string;
  readonly body: UpdateNotificationTemplateRequest;
}

export async function notificationsUpdateTemplate(
  http: HttpClient,
  parameters: NotificationsUpdateTemplateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<NotificationTemplateResponse> {
  const path = `/api/v1/notifications/templates/${encodeURIComponent(String(parameters.templateId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readNotificationTemplateResponse(value);
}

export interface NotificationsVerifyMyRecipientEndpointParameters {
  readonly endpointId: string;
  readonly body: VerifyRecipientEndpointCodeRequest;
}

export async function notificationsVerifyMyRecipientEndpoint(
  http: HttpClient,
  parameters: NotificationsVerifyMyRecipientEndpointParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<RecipientEndpointResponse> {
  const path = `/api/v1/notifications/my-recipient-endpoints/${encodeURIComponent(String(parameters.endpointId))}/verification/verify`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readRecipientEndpointResponse(value);
}

export interface ObservabilityDownloadLogFileParameters {
  readonly id: string;
}

export async function observabilityDownloadLogFile(
  http: HttpClient,
  parameters: ObservabilityDownloadLogFileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Blob> {
  const path = `/api/v1/observability/log-files/${encodeURIComponent(String(parameters.id))}/download`;
  const init: RequestInit = {
    method: 'GET',
    headers: { accept: 'application/octet-stream' }
  };
  return options === undefined
    ? await http.requestBlob(path, init, signal)
    : await http.requestBlob(path, init, signal, options);
}

export interface ObservabilityListLogFilesParameters {

}

export async function observabilityListLogFiles(
  http: HttpClient,
  parameters: ObservabilityListLogFilesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<LogFileSummary>> {
  const path = `/api/v1/observability/log-files`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readObservabilityListLogFilesResponse(value);
}

export interface ObservabilityTailLogFileParameters {
  readonly id: string;
  readonly maximumLines?: number;
  readonly maximumBytes?: number;
}

export async function observabilityTailLogFile(
  http: HttpClient,
  parameters: ObservabilityTailLogFileParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<LogFileTail> {
  const query = new URLSearchParams();
  if (parameters.maximumLines !== undefined) {
    query.set('maximumLines', String(parameters.maximumLines));
  }
  if (parameters.maximumBytes !== undefined) {
    query.set('maximumBytes', String(parameters.maximumBytes));
  }
  const path = query.size === 0 ? `/api/v1/observability/log-files/${encodeURIComponent(String(parameters.id))}/tail` : `/api/v1/observability/log-files/${encodeURIComponent(String(parameters.id))}/tail?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readLogFileTail(value);
}

export interface OrganizationAssignTenantPositionLevelParameters {
  readonly positionId: string;
  readonly body: AssignOrganizationPositionLevelRequest;
}

export async function organizationAssignTenantPositionLevel(
  http: HttpClient,
  parameters: OrganizationAssignTenantPositionLevelParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionResponse> {
  const path = `/api/v1/organization/positions/${encodeURIComponent(String(parameters.positionId))}/position-level`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionResponse(value);
}

export interface OrganizationAssignTenantPositionUnitParameters {
  readonly positionId: string;
  readonly body: AssignOrganizationPositionUnitRequest;
}

export async function organizationAssignTenantPositionUnit(
  http: HttpClient,
  parameters: OrganizationAssignTenantPositionUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionResponse> {
  const path = `/api/v1/organization/positions/${encodeURIComponent(String(parameters.positionId))}/unit`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionResponse(value);
}

export interface OrganizationCreateHostUserManagementUserPositionParameters {
  readonly tenantId: string;
  readonly body: CreateOrganizationUserPositionRequest;
}

export async function organizationCreateHostUserManagementUserPosition(
  http: HttpClient,
  parameters: OrganizationCreateHostUserManagementUserPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserPositionResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/user-positions` : `/api/v1/organization/host-user-management/user-positions?${query.toString()}`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserPositionResponse(value);
}

export interface OrganizationCreateHostUserManagementUserUnitParameters {
  readonly tenantId: string;
  readonly body: CreateOrganizationUserUnitRequest;
}

export async function organizationCreateHostUserManagementUserUnit(
  http: HttpClient,
  parameters: OrganizationCreateHostUserManagementUserUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserUnitResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/user-units` : `/api/v1/organization/host-user-management/user-units?${query.toString()}`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserUnitResponse(value);
}

export interface OrganizationCreateTenantPositionParameters {
  readonly body: CreateOrganizationPositionRequest;
}

export async function organizationCreateTenantPosition(
  http: HttpClient,
  parameters: OrganizationCreateTenantPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionResponse> {
  const path = `/api/v1/organization/positions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionResponse(value);
}

export interface OrganizationCreateTenantPositionLevelParameters {
  readonly body: CreateOrganizationPositionLevelRequest;
}

export async function organizationCreateTenantPositionLevel(
  http: HttpClient,
  parameters: OrganizationCreateTenantPositionLevelParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionLevelResponse> {
  const path = `/api/v1/organization/position-levels`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionLevelResponse(value);
}

export interface OrganizationCreateTenantUnitParameters {
  readonly body: CreateOrganizationUnitRequest;
}

export async function organizationCreateTenantUnit(
  http: HttpClient,
  parameters: OrganizationCreateTenantUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUnitResponse> {
  const path = `/api/v1/organization/units`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUnitResponse(value);
}

export interface OrganizationCreateTenantUserPositionParameters {
  readonly body: CreateOrganizationUserPositionRequest;
}

export async function organizationCreateTenantUserPosition(
  http: HttpClient,
  parameters: OrganizationCreateTenantUserPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserPositionResponse> {
  const path = `/api/v1/organization/user-positions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserPositionResponse(value);
}

export interface OrganizationCreateTenantUserUnitParameters {
  readonly body: CreateOrganizationUserUnitRequest;
}

export async function organizationCreateTenantUserUnit(
  http: HttpClient,
  parameters: OrganizationCreateTenantUserUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserUnitResponse> {
  const path = `/api/v1/organization/user-units`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserUnitResponse(value);
}

export interface OrganizationDisableHostUserManagementUserPositionParameters {
  readonly tenantId: string;
  readonly assignmentId: string;
}

export async function organizationDisableHostUserManagementUserPosition(
  http: HttpClient,
  parameters: OrganizationDisableHostUserManagementUserPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserPositionResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/user-positions/${encodeURIComponent(String(parameters.assignmentId))}/disable` : `/api/v1/organization/host-user-management/user-positions/${encodeURIComponent(String(parameters.assignmentId))}/disable?${query.toString()}`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserPositionResponse(value);
}

export interface OrganizationDisableHostUserManagementUserUnitParameters {
  readonly tenantId: string;
  readonly assignmentId: string;
}

export async function organizationDisableHostUserManagementUserUnit(
  http: HttpClient,
  parameters: OrganizationDisableHostUserManagementUserUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserUnitResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/user-units/${encodeURIComponent(String(parameters.assignmentId))}/disable` : `/api/v1/organization/host-user-management/user-units/${encodeURIComponent(String(parameters.assignmentId))}/disable?${query.toString()}`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserUnitResponse(value);
}

export interface OrganizationDisableTenantPositionParameters {
  readonly positionId: string;
}

export async function organizationDisableTenantPosition(
  http: HttpClient,
  parameters: OrganizationDisableTenantPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionResponse> {
  const path = `/api/v1/organization/positions/${encodeURIComponent(String(parameters.positionId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionResponse(value);
}

export interface OrganizationDisableTenantPositionLevelParameters {
  readonly positionLevelId: string;
}

export async function organizationDisableTenantPositionLevel(
  http: HttpClient,
  parameters: OrganizationDisableTenantPositionLevelParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionLevelResponse> {
  const path = `/api/v1/organization/position-levels/${encodeURIComponent(String(parameters.positionLevelId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionLevelResponse(value);
}

export interface OrganizationDisableTenantUnitParameters {
  readonly unitId: string;
}

export async function organizationDisableTenantUnit(
  http: HttpClient,
  parameters: OrganizationDisableTenantUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUnitResponse> {
  const path = `/api/v1/organization/units/${encodeURIComponent(String(parameters.unitId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUnitResponse(value);
}

export interface OrganizationDisableTenantUserPositionParameters {
  readonly assignmentId: string;
}

export async function organizationDisableTenantUserPosition(
  http: HttpClient,
  parameters: OrganizationDisableTenantUserPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserPositionResponse> {
  const path = `/api/v1/organization/user-positions/${encodeURIComponent(String(parameters.assignmentId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserPositionResponse(value);
}

export interface OrganizationDisableTenantUserUnitParameters {
  readonly assignmentId: string;
}

export async function organizationDisableTenantUserUnit(
  http: HttpClient,
  parameters: OrganizationDisableTenantUserUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserUnitResponse> {
  const path = `/api/v1/organization/user-units/${encodeURIComponent(String(parameters.assignmentId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserUnitResponse(value);
}

export interface OrganizationGetHostUserManagementReferenceParameters {
  readonly tenantId: string;
}

export async function organizationGetHostUserManagementReference(
  http: HttpClient,
  parameters: OrganizationGetHostUserManagementReferenceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostUserManagementOrganizationReferenceResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/reference` : `/api/v1/organization/host-user-management/reference?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostUserManagementOrganizationReferenceResponse(value);
}

export interface OrganizationGetTenantPositionParameters {
  readonly positionId: string;
}

export async function organizationGetTenantPosition(
  http: HttpClient,
  parameters: OrganizationGetTenantPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionResponse> {
  const path = `/api/v1/organization/positions/${encodeURIComponent(String(parameters.positionId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionResponse(value);
}

export interface OrganizationGetTenantPositionLevelParameters {
  readonly positionLevelId: string;
}

export async function organizationGetTenantPositionLevel(
  http: HttpClient,
  parameters: OrganizationGetTenantPositionLevelParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionLevelResponse> {
  const path = `/api/v1/organization/position-levels/${encodeURIComponent(String(parameters.positionLevelId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionLevelResponse(value);
}

export interface OrganizationGetTenantUnitParameters {
  readonly unitId: string;
}

export async function organizationGetTenantUnit(
  http: HttpClient,
  parameters: OrganizationGetTenantUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUnitResponse> {
  const path = `/api/v1/organization/units/${encodeURIComponent(String(parameters.unitId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUnitResponse(value);
}

export interface OrganizationListAssignableTenantUserPositionUsersParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function organizationListAssignableTenantUserPositionUsers(
  http: HttpClient,
  parameters: OrganizationListAssignableTenantUserPositionUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationAssignableUserResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/organization/user-positions/assignable-users` : `/api/v1/organization/user-positions/assignable-users?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationAssignableUserResponse(value);
}

export interface OrganizationListAssignableTenantUserUnitUsersParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function organizationListAssignableTenantUserUnitUsers(
  http: HttpClient,
  parameters: OrganizationListAssignableTenantUserUnitUsersParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationAssignableUserResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/organization/user-units/assignable-users` : `/api/v1/organization/user-units/assignable-users?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationAssignableUserResponse(value);
}

export interface OrganizationListTenantPositionLevelsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function organizationListTenantPositionLevels(
  http: HttpClient,
  parameters: OrganizationListTenantPositionLevelsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationPositionLevelResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/organization/position-levels` : `/api/v1/organization/position-levels?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationPositionLevelResponse(value);
}

export interface OrganizationListTenantPositionsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function organizationListTenantPositions(
  http: HttpClient,
  parameters: OrganizationListTenantPositionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationPositionResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/organization/positions` : `/api/v1/organization/positions?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationPositionResponse(value);
}

export interface OrganizationListTenantUnitsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function organizationListTenantUnits(
  http: HttpClient,
  parameters: OrganizationListTenantUnitsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationUnitResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/organization/units` : `/api/v1/organization/units?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationUnitResponse(value);
}

export interface OrganizationListTenantUserPositionsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly userId?: string;
  readonly positionId?: string;
}

export async function organizationListTenantUserPositions(
  http: HttpClient,
  parameters: OrganizationListTenantUserPositionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationUserPositionResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.userId !== undefined) {
    query.set('userId', String(parameters.userId));
  }
  if (parameters.positionId !== undefined) {
    query.set('positionId', String(parameters.positionId));
  }
  const path = query.size === 0 ? `/api/v1/organization/user-positions` : `/api/v1/organization/user-positions?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationUserPositionResponse(value);
}

export interface OrganizationListTenantUserUnitsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly userId?: string;
  readonly unitId?: string;
}

export async function organizationListTenantUserUnits(
  http: HttpClient,
  parameters: OrganizationListTenantUserUnitsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfOrganizationUserUnitResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.userId !== undefined) {
    query.set('userId', String(parameters.userId));
  }
  if (parameters.unitId !== undefined) {
    query.set('unitId', String(parameters.unitId));
  }
  const path = query.size === 0 ? `/api/v1/organization/user-units` : `/api/v1/organization/user-units?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfOrganizationUserUnitResponse(value);
}

export interface OrganizationUpdateHostUserManagementUserPositionParameters {
  readonly tenantId: string;
  readonly assignmentId: string;
  readonly body: UpdateOrganizationUserPositionRequest;
}

export async function organizationUpdateHostUserManagementUserPosition(
  http: HttpClient,
  parameters: OrganizationUpdateHostUserManagementUserPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserPositionResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/user-positions/${encodeURIComponent(String(parameters.assignmentId))}` : `/api/v1/organization/host-user-management/user-positions/${encodeURIComponent(String(parameters.assignmentId))}?${query.toString()}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserPositionResponse(value);
}

export interface OrganizationUpdateHostUserManagementUserUnitParameters {
  readonly tenantId: string;
  readonly assignmentId: string;
  readonly body: UpdateOrganizationUserUnitRequest;
}

export async function organizationUpdateHostUserManagementUserUnit(
  http: HttpClient,
  parameters: OrganizationUpdateHostUserManagementUserUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserUnitResponse> {
  const query = new URLSearchParams();
  query.set('tenantId', String(parameters.tenantId));
  const path = query.size === 0 ? `/api/v1/organization/host-user-management/user-units/${encodeURIComponent(String(parameters.assignmentId))}` : `/api/v1/organization/host-user-management/user-units/${encodeURIComponent(String(parameters.assignmentId))}?${query.toString()}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserUnitResponse(value);
}

export interface OrganizationUpdateTenantPositionParameters {
  readonly positionId: string;
  readonly body: UpdateOrganizationPositionRequest;
}

export async function organizationUpdateTenantPosition(
  http: HttpClient,
  parameters: OrganizationUpdateTenantPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionResponse> {
  const path = `/api/v1/organization/positions/${encodeURIComponent(String(parameters.positionId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionResponse(value);
}

export interface OrganizationUpdateTenantPositionLevelParameters {
  readonly positionLevelId: string;
  readonly body: UpdateOrganizationPositionLevelRequest;
}

export async function organizationUpdateTenantPositionLevel(
  http: HttpClient,
  parameters: OrganizationUpdateTenantPositionLevelParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationPositionLevelResponse> {
  const path = `/api/v1/organization/position-levels/${encodeURIComponent(String(parameters.positionLevelId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationPositionLevelResponse(value);
}

export interface OrganizationUpdateTenantUnitParameters {
  readonly unitId: string;
  readonly body: UpdateOrganizationUnitRequest;
}

export async function organizationUpdateTenantUnit(
  http: HttpClient,
  parameters: OrganizationUpdateTenantUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUnitResponse> {
  const path = `/api/v1/organization/units/${encodeURIComponent(String(parameters.unitId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUnitResponse(value);
}

export interface OrganizationUpdateTenantUserPositionParameters {
  readonly assignmentId: string;
  readonly body: UpdateOrganizationUserPositionRequest;
}

export async function organizationUpdateTenantUserPosition(
  http: HttpClient,
  parameters: OrganizationUpdateTenantUserPositionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserPositionResponse> {
  const path = `/api/v1/organization/user-positions/${encodeURIComponent(String(parameters.assignmentId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserPositionResponse(value);
}

export interface OrganizationUpdateTenantUserUnitParameters {
  readonly assignmentId: string;
  readonly body: UpdateOrganizationUserUnitRequest;
}

export async function organizationUpdateTenantUserUnit(
  http: HttpClient,
  parameters: OrganizationUpdateTenantUserUnitParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<OrganizationUserUnitResponse> {
  const path = `/api/v1/organization/user-units/${encodeURIComponent(String(parameters.assignmentId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readOrganizationUserUnitResponse(value);
}

export interface PlatformGetHostDashboardSummaryParameters {

}

export async function platformGetHostDashboardSummary(
  http: HttpClient,
  parameters: PlatformGetHostDashboardSummaryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<HostDashboardSummaryResponse> {
  const path = `/api/v1/platform/host-dashboard-summary`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readHostDashboardSummaryResponse(value);
}

export interface SerialNumbersCreateRuleParameters {
  readonly body: CreateSerialNumberRuleRequest;
}

export async function serialNumbersCreateRule(
  http: HttpClient,
  parameters: SerialNumbersCreateRuleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SerialNumberRuleResponse> {
  const path = `/api/v1/serial-numbers/rules`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSerialNumberRuleResponse(value);
}

export interface SerialNumbersDisableRuleParameters {
  readonly ruleId: string;
  readonly body: ChangeSerialNumberRuleStatusRequest;
}

export async function serialNumbersDisableRule(
  http: HttpClient,
  parameters: SerialNumbersDisableRuleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SerialNumberRuleResponse> {
  const path = `/api/v1/serial-numbers/rules/${encodeURIComponent(String(parameters.ruleId))}/disable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSerialNumberRuleResponse(value);
}

export interface SerialNumbersEnableRuleParameters {
  readonly ruleId: string;
  readonly body: ChangeSerialNumberRuleStatusRequest;
}

export async function serialNumbersEnableRule(
  http: HttpClient,
  parameters: SerialNumbersEnableRuleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SerialNumberRuleResponse> {
  const path = `/api/v1/serial-numbers/rules/${encodeURIComponent(String(parameters.ruleId))}/enable`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSerialNumberRuleResponse(value);
}

export interface SerialNumbersListRulesParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly name?: string;
  readonly key?: string;
  readonly isEnabled?: boolean;
  readonly scope?: number;
  readonly resetInterval?: number;
  readonly sortBy?: string;
  readonly sortDirection?: string;
}

export async function serialNumbersListRules(
  http: HttpClient,
  parameters: SerialNumbersListRulesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfSerialNumberRuleResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.name !== undefined) {
    query.set('name', String(parameters.name));
  }
  if (parameters.key !== undefined) {
    query.set('key', String(parameters.key));
  }
  if (parameters.isEnabled !== undefined) {
    query.set('isEnabled', String(parameters.isEnabled));
  }
  if (parameters.scope !== undefined) {
    query.set('scope', String(parameters.scope));
  }
  if (parameters.resetInterval !== undefined) {
    query.set('resetInterval', String(parameters.resetInterval));
  }
  if (parameters.sortBy !== undefined) {
    query.set('sortBy', String(parameters.sortBy));
  }
  if (parameters.sortDirection !== undefined) {
    query.set('sortDirection', String(parameters.sortDirection));
  }
  const path = query.size === 0 ? `/api/v1/serial-numbers/rules` : `/api/v1/serial-numbers/rules?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfSerialNumberRuleResponse(value);
}

export interface SerialNumbersPreviewSerialNumberParameters {
  readonly body: PreviewSerialNumberRequest;
}

export async function serialNumbersPreviewSerialNumber(
  http: HttpClient,
  parameters: SerialNumbersPreviewSerialNumberParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SerialNumberPreviewResponse> {
  const path = `/api/v1/serial-numbers/rules/preview`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSerialNumberPreviewResponse(value);
}

export interface SerialNumbersUpdateRuleParameters {
  readonly ruleId: string;
  readonly body: UpdateSerialNumberRuleRequest;
}

export async function serialNumbersUpdateRule(
  http: HttpClient,
  parameters: SerialNumbersUpdateRuleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<SerialNumberRuleResponse> {
  const path = `/api/v1/serial-numbers/rules/${encodeURIComponent(String(parameters.ruleId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSerialNumberRuleResponse(value);
}

export interface SettingsBatchDeleteHostConfigEntriesParameters {
  readonly body: BatchDeleteConfigEntriesRequest;
}

export async function settingsBatchDeleteHostConfigEntries(
  http: HttpClient,
  parameters: SettingsBatchDeleteHostConfigEntriesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/settings/config-entries/batch-delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface SettingsBatchUpdateHostConfigEntryValuesParameters {
  readonly body: BatchUpdateConfigValuesRequest;
}

export async function settingsBatchUpdateHostConfigEntryValues(
  http: HttpClient,
  parameters: SettingsBatchUpdateHostConfigEntryValuesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<boolean> {
  const path = `/api/v1/settings/config-entries/batch-update-values`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsBatchUpdateHostConfigEntryValuesResponse(value);
}

export interface SettingsCreateHostConfigEntryParameters {
  readonly body: CreateConfigEntryRequest;
}

export async function settingsCreateHostConfigEntry(
  http: HttpClient,
  parameters: SettingsCreateHostConfigEntryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ConfigEntryResponse> {
  const path = `/api/v1/settings/config-entries`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readConfigEntryResponse(value);
}

export interface SettingsCreateHostDictItemParameters {
  readonly dictTypeId: string;
  readonly body: CreateDictItemRequest;
}

export async function settingsCreateHostDictItem(
  http: HttpClient,
  parameters: SettingsCreateHostDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/items`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsCreateHostDictTypeParameters {
  readonly body: CreateDictTypeRequest;
}

export async function settingsCreateHostDictType(
  http: HttpClient,
  parameters: SettingsCreateHostDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictTypeResponse> {
  const path = `/api/v1/settings/dict-types`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictTypeResponse(value);
}

export interface SettingsCreateTenantDictItemParameters {
  readonly dictTypeId: string;
  readonly body: CreateDictItemRequest;
}

export async function settingsCreateTenantDictItem(
  http: HttpClient,
  parameters: SettingsCreateTenantDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/tenant-dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/items`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsCreateTenantDictTypeParameters {
  readonly body: CreateDictTypeRequest;
}

export async function settingsCreateTenantDictType(
  http: HttpClient,
  parameters: SettingsCreateTenantDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictTypeResponse> {
  const path = `/api/v1/settings/tenant-dict-types`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictTypeResponse(value);
}

export interface SettingsDeleteHostConfigEntryParameters {
  readonly configEntryId: string;
  readonly body: DeleteConfigEntryRequest;
}

export async function settingsDeleteHostConfigEntry(
  http: HttpClient,
  parameters: SettingsDeleteHostConfigEntryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/settings/config-entries/${encodeURIComponent(String(parameters.configEntryId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface SettingsDeleteHostDictItemParameters {
  readonly dictItemId: string;
  readonly body: DeleteDictItemRequest;
}

export async function settingsDeleteHostDictItem(
  http: HttpClient,
  parameters: SettingsDeleteHostDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/settings/dict-items/${encodeURIComponent(String(parameters.dictItemId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface SettingsDeleteHostDictTypeParameters {
  readonly dictTypeId: string;
  readonly body: DeleteDictTypeRequest;
}

export async function settingsDeleteHostDictType(
  http: HttpClient,
  parameters: SettingsDeleteHostDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/settings/dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface SettingsDeleteTenantDictItemParameters {
  readonly dictItemId: string;
  readonly body: DeleteDictItemRequest;
}

export async function settingsDeleteTenantDictItem(
  http: HttpClient,
  parameters: SettingsDeleteTenantDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/settings/tenant-dict-items/${encodeURIComponent(String(parameters.dictItemId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface SettingsDeleteTenantDictTypeParameters {
  readonly dictTypeId: string;
  readonly body: DeleteDictTypeRequest;
}

export async function settingsDeleteTenantDictType(
  http: HttpClient,
  parameters: SettingsDeleteTenantDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/settings/tenant-dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  if (options === undefined) {
    await http.request<void>(path, init, signal);
  } else {
    await http.request<void>(path, init, signal, options);
  }
}

export interface SettingsDisableHostConfigEntryParameters {
  readonly configEntryId: string;
}

export async function settingsDisableHostConfigEntry(
  http: HttpClient,
  parameters: SettingsDisableHostConfigEntryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ConfigEntryResponse> {
  const path = `/api/v1/settings/config-entries/${encodeURIComponent(String(parameters.configEntryId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readConfigEntryResponse(value);
}

export interface SettingsDisableHostDictItemParameters {
  readonly dictItemId: string;
}

export async function settingsDisableHostDictItem(
  http: HttpClient,
  parameters: SettingsDisableHostDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/dict-items/${encodeURIComponent(String(parameters.dictItemId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsDisableHostDictTypeParameters {
  readonly dictTypeId: string;
}

export async function settingsDisableHostDictType(
  http: HttpClient,
  parameters: SettingsDisableHostDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictTypeResponse> {
  const path = `/api/v1/settings/dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictTypeResponse(value);
}

export interface SettingsDisableTenantDictItemParameters {
  readonly dictItemId: string;
}

export async function settingsDisableTenantDictItem(
  http: HttpClient,
  parameters: SettingsDisableTenantDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/tenant-dict-items/${encodeURIComponent(String(parameters.dictItemId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsDisableTenantDictTypeParameters {
  readonly dictTypeId: string;
}

export async function settingsDisableTenantDictType(
  http: HttpClient,
  parameters: SettingsDisableTenantDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictTypeResponse> {
  const path = `/api/v1/settings/tenant-dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictTypeResponse(value);
}

export interface SettingsGetHostConfigEntryParameters {
  readonly configEntryId: string;
}

export async function settingsGetHostConfigEntry(
  http: HttpClient,
  parameters: SettingsGetHostConfigEntryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ConfigEntryResponse> {
  const path = `/api/v1/settings/config-entries/${encodeURIComponent(String(parameters.configEntryId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readConfigEntryResponse(value);
}

export interface SettingsGetHostConfigEntryByKeyParameters {
  readonly configKey: string;
}

export async function settingsGetHostConfigEntryByKey(
  http: HttpClient,
  parameters: SettingsGetHostConfigEntryByKeyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ConfigEntryResponse> {
  const path = `/api/v1/settings/config-entries/by-key/${encodeURIComponent(String(parameters.configKey))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readConfigEntryResponse(value);
}

export interface SettingsGetHostDiagnosticPolicyParameters {

}

export async function settingsGetHostDiagnosticPolicy(
  http: HttpClient,
  parameters: SettingsGetHostDiagnosticPolicyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DiagnosticPolicyResponse> {
  const path = `/api/v1/settings/diagnostic-policy`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDiagnosticPolicyResponse(value);
}

export interface SettingsGetHostDictItemParameters {
  readonly dictItemId: string;
}

export async function settingsGetHostDictItem(
  http: HttpClient,
  parameters: SettingsGetHostDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/dict-items/${encodeURIComponent(String(parameters.dictItemId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsGetHostEnumCatalogParameters {
  readonly catalogKey: string;
}

export async function settingsGetHostEnumCatalog(
  http: HttpClient,
  parameters: SettingsGetHostEnumCatalogParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnumCatalogDetail> {
  const path = `/api/v1/settings/enum-catalogs/${encodeURIComponent(String(parameters.catalogKey))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnumCatalogDetail(value);
}

export interface SettingsGetTenantDictItemParameters {
  readonly dictItemId: string;
}

export async function settingsGetTenantDictItem(
  http: HttpClient,
  parameters: SettingsGetTenantDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/tenant-dict-items/${encodeURIComponent(String(parameters.dictItemId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsListAllHostConfigEntriesParameters {

}

export async function settingsListAllHostConfigEntries(
  http: HttpClient,
  parameters: SettingsListAllHostConfigEntriesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<ConfigEntryResponse>> {
  const path = `/api/v1/settings/config-entries/list`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListAllHostConfigEntriesResponse(value);
}

export interface SettingsListAllHostDictTypesParameters {

}

export async function settingsListAllHostDictTypes(
  http: HttpClient,
  parameters: SettingsListAllHostDictTypesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<DictTypeResponse>> {
  const path = `/api/v1/settings/dict-types/list`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListAllHostDictTypesResponse(value);
}

export interface SettingsListAllTenantDictTypesParameters {

}

export async function settingsListAllTenantDictTypes(
  http: HttpClient,
  parameters: SettingsListAllTenantDictTypesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<DictTypeResponse>> {
  const path = `/api/v1/settings/tenant-dict-types/list`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListAllTenantDictTypesResponse(value);
}

export interface SettingsListHostConfigEntriesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function settingsListHostConfigEntries(
  http: HttpClient,
  parameters: SettingsListHostConfigEntriesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfConfigEntryResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/settings/config-entries` : `/api/v1/settings/config-entries?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfConfigEntryResponse(value);
}

export interface SettingsListHostConfigEntryGroupsParameters {

}

export async function settingsListHostConfigEntryGroups(
  http: HttpClient,
  parameters: SettingsListHostConfigEntryGroupsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<string>> {
  const path = `/api/v1/settings/config-entries/groups`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListHostConfigEntryGroupsResponse(value);
}

export interface SettingsListHostDictItemsParameters {
  readonly dictTypeId: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function settingsListHostDictItems(
  http: HttpClient,
  parameters: SettingsListHostDictItemsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfDictItemResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/settings/dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/items` : `/api/v1/settings/dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/items?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfDictItemResponse(value);
}

export interface SettingsListHostDictItemsByTypeCodeParameters {
  readonly code: string;
}

export async function settingsListHostDictItemsByTypeCode(
  http: HttpClient,
  parameters: SettingsListHostDictItemsByTypeCodeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<DictItemResponse>> {
  const path = `/api/v1/settings/dict-types/by-code/${encodeURIComponent(String(parameters.code))}/items`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListHostDictItemsByTypeCodeResponse(value);
}

export interface SettingsListHostDictTypesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function settingsListHostDictTypes(
  http: HttpClient,
  parameters: SettingsListHostDictTypesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfDictTypeResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/settings/dict-types` : `/api/v1/settings/dict-types?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfDictTypeResponse(value);
}

export interface SettingsListHostEnumCatalogsParameters {

}

export async function settingsListHostEnumCatalogs(
  http: HttpClient,
  parameters: SettingsListHostEnumCatalogsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<EnumCatalogSummary>> {
  const path = `/api/v1/settings/enum-catalogs`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListHostEnumCatalogsResponse(value);
}

export interface SettingsListTenantDictItemsParameters {
  readonly dictTypeId: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function settingsListTenantDictItems(
  http: HttpClient,
  parameters: SettingsListTenantDictItemsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfDictItemResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/settings/tenant-dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/items` : `/api/v1/settings/tenant-dict-types/${encodeURIComponent(String(parameters.dictTypeId))}/items?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfDictItemResponse(value);
}

export interface SettingsListTenantDictItemsByTypeCodeParameters {
  readonly code: string;
}

export async function settingsListTenantDictItemsByTypeCode(
  http: HttpClient,
  parameters: SettingsListTenantDictItemsByTypeCodeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<DictItemResponse>> {
  const path = `/api/v1/settings/tenant-dict-types/by-code/${encodeURIComponent(String(parameters.code))}/items`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readSettingsListTenantDictItemsByTypeCodeResponse(value);
}

export interface SettingsListTenantDictTypesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function settingsListTenantDictTypes(
  http: HttpClient,
  parameters: SettingsListTenantDictTypesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfDictTypeResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/settings/tenant-dict-types` : `/api/v1/settings/tenant-dict-types?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfDictTypeResponse(value);
}

export interface SettingsRestoreHostDiagnosticPolicyParameters {
  readonly body: RestoreDiagnosticPolicyRequest;
}

export async function settingsRestoreHostDiagnosticPolicy(
  http: HttpClient,
  parameters: SettingsRestoreHostDiagnosticPolicyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DiagnosticPolicyResponse> {
  const path = `/api/v1/settings/diagnostic-policy/restore`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDiagnosticPolicyResponse(value);
}

export interface SettingsUpdateHostConfigEntryParameters {
  readonly configEntryId: string;
  readonly body: UpdateConfigEntryRequest;
}

export async function settingsUpdateHostConfigEntry(
  http: HttpClient,
  parameters: SettingsUpdateHostConfigEntryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<ConfigEntryResponse> {
  const path = `/api/v1/settings/config-entries/${encodeURIComponent(String(parameters.configEntryId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readConfigEntryResponse(value);
}

export interface SettingsUpdateHostDiagnosticPolicyParameters {
  readonly body: UpdateDiagnosticPolicyRequest;
}

export async function settingsUpdateHostDiagnosticPolicy(
  http: HttpClient,
  parameters: SettingsUpdateHostDiagnosticPolicyParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DiagnosticPolicyResponse> {
  const path = `/api/v1/settings/diagnostic-policy`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDiagnosticPolicyResponse(value);
}

export interface SettingsUpdateHostDictItemParameters {
  readonly dictItemId: string;
  readonly body: UpdateDictItemRequest;
}

export async function settingsUpdateHostDictItem(
  http: HttpClient,
  parameters: SettingsUpdateHostDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/dict-items/${encodeURIComponent(String(parameters.dictItemId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsUpdateHostDictTypeParameters {
  readonly dictTypeId: string;
  readonly body: UpdateDictTypeRequest;
}

export async function settingsUpdateHostDictType(
  http: HttpClient,
  parameters: SettingsUpdateHostDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictTypeResponse> {
  const path = `/api/v1/settings/dict-types/${encodeURIComponent(String(parameters.dictTypeId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictTypeResponse(value);
}

export interface SettingsUpdateTenantDictItemParameters {
  readonly dictItemId: string;
  readonly body: UpdateDictItemRequest;
}

export async function settingsUpdateTenantDictItem(
  http: HttpClient,
  parameters: SettingsUpdateTenantDictItemParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictItemResponse> {
  const path = `/api/v1/settings/tenant-dict-items/${encodeURIComponent(String(parameters.dictItemId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictItemResponse(value);
}

export interface SettingsUpdateTenantDictTypeParameters {
  readonly dictTypeId: string;
  readonly body: UpdateDictTypeRequest;
}

export async function settingsUpdateTenantDictType(
  http: HttpClient,
  parameters: SettingsUpdateTenantDictTypeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<DictTypeResponse> {
  const path = `/api/v1/settings/tenant-dict-types/${encodeURIComponent(String(parameters.dictTypeId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readDictTypeResponse(value);
}

export interface TenancyAssignHostTenantPackageParameters {
  readonly tenantId: string;
  readonly body: AssignHostTenantPackageRequest;
}

export async function tenancyAssignHostTenantPackage(
  http: HttpClient,
  parameters: TenancyAssignHostTenantPackageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantSummary> {
  const path = `/api/v1/tenancy/tenants/${encodeURIComponent(String(parameters.tenantId))}/package`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantSummary(value);
}

export interface TenancyCreateHostTenantParameters {
  readonly body: ProvisionTenantRequest;
}

export async function tenancyCreateHostTenant(
  http: HttpClient,
  parameters: TenancyCreateHostTenantParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantSummary> {
  const path = `/api/v1/tenancy/tenants`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantSummary(value);
}

export interface TenancyCreateHostTenantPackageParameters {
  readonly body: CreateHostTenantPackageRequest;
}

export async function tenancyCreateHostTenantPackage(
  http: HttpClient,
  parameters: TenancyCreateHostTenantPackageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantPackageSummary> {
  const path = `/api/v1/tenancy/tenant-packages`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantPackageSummary(value);
}

export interface TenancyDisableHostTenantParameters {
  readonly tenantId: string;
}

export async function tenancyDisableHostTenant(
  http: HttpClient,
  parameters: TenancyDisableHostTenantParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantSummary> {
  const path = `/api/v1/tenancy/tenants/${encodeURIComponent(String(parameters.tenantId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantSummary(value);
}

export interface TenancyDisableHostTenantPackageParameters {
  readonly packageId: string;
}

export async function tenancyDisableHostTenantPackage(
  http: HttpClient,
  parameters: TenancyDisableHostTenantPackageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantPackageSummary> {
  const path = `/api/v1/tenancy/tenant-packages/${encodeURIComponent(String(parameters.packageId))}/disable`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantPackageSummary(value);
}

export interface TenancyGetHostTenantParameters {
  readonly tenantId: string;
}

export async function tenancyGetHostTenant(
  http: HttpClient,
  parameters: TenancyGetHostTenantParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantSummary> {
  const path = `/api/v1/tenancy/tenants/${encodeURIComponent(String(parameters.tenantId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantSummary(value);
}

export interface TenancyGetHostTenantPackageParameters {
  readonly packageId: string;
}

export async function tenancyGetHostTenantPackage(
  http: HttpClient,
  parameters: TenancyGetHostTenantPackageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantPackageSummary> {
  const path = `/api/v1/tenancy/tenant-packages/${encodeURIComponent(String(parameters.packageId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantPackageSummary(value);
}

export interface TenancyListHostTenantPackagesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function tenancyListHostTenantPackages(
  http: HttpClient,
  parameters: TenancyListHostTenantPackagesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfTenantPackageSummary> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/tenancy/tenant-packages` : `/api/v1/tenancy/tenant-packages?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfTenantPackageSummary(value);
}

export interface TenancyListHostTenantsParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function tenancyListHostTenants(
  http: HttpClient,
  parameters: TenancyListHostTenantsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfTenantSummary> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/tenancy/tenants` : `/api/v1/tenancy/tenants?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfTenantSummary(value);
}

export interface TenancyUpdateHostTenantParameters {
  readonly tenantId: string;
  readonly body: UpdateHostTenantRequest;
}

export async function tenancyUpdateHostTenant(
  http: HttpClient,
  parameters: TenancyUpdateHostTenantParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantSummary> {
  const path = `/api/v1/tenancy/tenants/${encodeURIComponent(String(parameters.tenantId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantSummary(value);
}

export interface TenancyUpdateHostTenantPackageParameters {
  readonly packageId: string;
  readonly body: UpdateHostTenantPackageRequest;
}

export async function tenancyUpdateHostTenantPackage(
  http: HttpClient,
  parameters: TenancyUpdateHostTenantPackageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TenantPackageSummary> {
  const path = `/api/v1/tenancy/tenant-packages/${encodeURIComponent(String(parameters.packageId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readTenantPackageSummary(value);
}

export interface WorkflowApproveTodoParameters {
  readonly todoId: string;
  readonly body: ActWorkflowTodoRequest;
}

export async function workflowApproveTodo(
  http: HttpClient,
  parameters: WorkflowApproveTodoParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/todos/${encodeURIComponent(String(parameters.todoId))}/approve`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowCancelInstanceParameters {
  readonly instanceId: string;
  readonly body: CancelWorkflowInstanceRequest;
}

export async function workflowCancelInstance(
  http: HttpClient,
  parameters: WorkflowCancelInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}/cancel`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowCreateDefinitionParameters {
  readonly body: CreateWorkflowDefinitionRequest;
}

export async function workflowCreateDefinition(
  http: HttpClient,
  parameters: WorkflowCreateDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowDefinitionResponse> {
  const path = `/api/v1/workflow/definitions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowDefinitionResponse(value);
}

export interface WorkflowCreateFormParameters {
  readonly body: CreateWorkflowFormRequest;
}

export async function workflowCreateForm(
  http: HttpClient,
  parameters: WorkflowCreateFormParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowFormResponse> {
  const path = `/api/v1/workflow/forms`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowFormResponse(value);
}

export interface WorkflowGetDefinitionParameters {
  readonly definitionId: string;
}

export async function workflowGetDefinition(
  http: HttpClient,
  parameters: WorkflowGetDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowDefinitionResponse> {
  const path = `/api/v1/workflow/definitions/${encodeURIComponent(String(parameters.definitionId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowDefinitionResponse(value);
}

export interface WorkflowGetFormParameters {
  readonly formId: string;
}

export async function workflowGetForm(
  http: HttpClient,
  parameters: WorkflowGetFormParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowFormResponse> {
  const path = `/api/v1/workflow/forms/${encodeURIComponent(String(parameters.formId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowFormResponse(value);
}

export interface WorkflowGetFormComponentCatalogParameters {

}

export async function workflowGetFormComponentCatalog(
  http: HttpClient,
  parameters: WorkflowGetFormComponentCatalogParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowFormComponentCatalogResponse> {
  const path = `/api/v1/workflow/forms/component-catalog`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowFormComponentCatalogResponse(value);
}

export interface WorkflowGetFormVersionParameters {
  readonly versionId: string;
}

export async function workflowGetFormVersion(
  http: HttpClient,
  parameters: WorkflowGetFormVersionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowFormVersionResponse> {
  const path = `/api/v1/workflow/form-versions/${encodeURIComponent(String(parameters.versionId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowFormVersionResponse(value);
}

export interface WorkflowGetInstanceParameters {
  readonly instanceId: string;
}

export async function workflowGetInstance(
  http: HttpClient,
  parameters: WorkflowGetInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowGetNodeTypeCatalogParameters {

}

export async function workflowGetNodeTypeCatalog(
  http: HttpClient,
  parameters: WorkflowGetNodeTypeCatalogParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowNodeTypeCatalogResponse> {
  const path = `/api/v1/workflow/definitions/node-type-catalog`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowNodeTypeCatalogResponse(value);
}

export interface WorkflowGetRecoveryTaskParameters {
  readonly taskId: string;
}

export async function workflowGetRecoveryTask(
  http: HttpClient,
  parameters: WorkflowGetRecoveryTaskParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowRecoveryTaskResponse> {
  const path = `/api/v1/workflow/recovery-tasks/${encodeURIComponent(String(parameters.taskId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowRecoveryTaskResponse(value);
}

export interface WorkflowGetTodoParameters {
  readonly todoId: string;
}

export async function workflowGetTodo(
  http: HttpClient,
  parameters: WorkflowGetTodoParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowTodoDetailResponse> {
  const path = `/api/v1/workflow/todos/${encodeURIComponent(String(parameters.todoId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowTodoDetailResponse(value);
}

export interface WorkflowGetTodoRuntimeParameters {
  readonly todoId: string;
}

export async function workflowGetTodoRuntime(
  http: HttpClient,
  parameters: WorkflowGetTodoRuntimeParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowTodoRuntimeResponse> {
  const path = `/api/v1/workflow/todos/${encodeURIComponent(String(parameters.todoId))}/runtime`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowTodoRuntimeResponse(value);
}

export interface WorkflowListDefinitionsParameters {

}

export async function workflowListDefinitions(
  http: HttpClient,
  parameters: WorkflowListDefinitionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WorkflowDefinitionResponse>> {
  const path = `/api/v1/workflow/definitions`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowListDefinitionsResponse(value);
}

export interface WorkflowListDefinitionVersionsParameters {
  readonly definitionId: string;
}

export async function workflowListDefinitionVersions(
  http: HttpClient,
  parameters: WorkflowListDefinitionVersionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WorkflowDefinitionVersionResponse>> {
  const path = `/api/v1/workflow/definitions/${encodeURIComponent(String(parameters.definitionId))}/versions`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowListDefinitionVersionsResponse(value);
}

export interface WorkflowListFormsParameters {

}

export async function workflowListForms(
  http: HttpClient,
  parameters: WorkflowListFormsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WorkflowFormResponse>> {
  const path = `/api/v1/workflow/forms`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowListFormsResponse(value);
}

export interface WorkflowListInstanceExecutionLogsParameters {
  readonly instanceId: string;
}

export async function workflowListInstanceExecutionLogs(
  http: HttpClient,
  parameters: WorkflowListInstanceExecutionLogsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WorkflowExecutionLogResponse>> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}/execution-logs`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowListInstanceExecutionLogsResponse(value);
}

export interface WorkflowListMyCcParameters {

}

export async function workflowListMyCc(
  http: HttpClient,
  parameters: WorkflowListMyCcParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WorkflowCcResponse>> {
  const path = `/api/v1/workflow/cc/mine`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowListMyCcResponse(value);
}

export interface WorkflowListMyTodosParameters {

}

export async function workflowListMyTodos(
  http: HttpClient,
  parameters: WorkflowListMyTodosParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WorkflowTodoResponse>> {
  const path = `/api/v1/workflow/todos/mine`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowListMyTodosResponse(value);
}

export interface WorkflowListRecipientCandidatesParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function workflowListRecipientCandidates(
  http: HttpClient,
  parameters: WorkflowListRecipientCandidatesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowRecipientCandidatePageResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/workflow/definitions/recipient-candidates` : `/api/v1/workflow/definitions/recipient-candidates?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowRecipientCandidatePageResponse(value);
}

export interface WorkflowListRecoveryTasksParameters {
  readonly page?: number;
  readonly pageSize?: number;
}

export async function workflowListRecoveryTasks(
  http: HttpClient,
  parameters: WorkflowListRecoveryTasksParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfWorkflowRecoveryTaskResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/workflow/recovery-tasks` : `/api/v1/workflow/recovery-tasks?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfWorkflowRecoveryTaskResponse(value);
}

export interface WorkflowMarkCcReadParameters {
  readonly ccId: string;
}

export async function workflowMarkCcRead(
  http: HttpClient,
  parameters: WorkflowMarkCcReadParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowCcReadResponse> {
  const path = `/api/v1/workflow/cc/${encodeURIComponent(String(parameters.ccId))}/read`;
  const init: RequestInit = { method: 'POST' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowCcReadResponse(value);
}

export interface WorkflowPauseInstanceParameters {
  readonly instanceId: string;
  readonly body: PauseWorkflowInstanceRequest;
}

export async function workflowPauseInstance(
  http: HttpClient,
  parameters: WorkflowPauseInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}/pause`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowPublishDefinitionParameters {
  readonly definitionId: string;
  readonly body: PublishWorkflowDefinitionRequest;
}

export async function workflowPublishDefinition(
  http: HttpClient,
  parameters: WorkflowPublishDefinitionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowDefinitionVersionResponse> {
  const path = `/api/v1/workflow/definitions/${encodeURIComponent(String(parameters.definitionId))}/publish`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowDefinitionVersionResponse(value);
}

export interface WorkflowPublishFormParameters {
  readonly formId: string;
  readonly body: PublishWorkflowFormRequest;
}

export async function workflowPublishForm(
  http: HttpClient,
  parameters: WorkflowPublishFormParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowFormVersionResponse> {
  const path = `/api/v1/workflow/forms/${encodeURIComponent(String(parameters.formId))}/publish`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowFormVersionResponse(value);
}

export interface WorkflowReassignInstanceParameters {
  readonly instanceId: string;
  readonly body: ReassignWorkflowInstanceRequest;
}

export async function workflowReassignInstance(
  http: HttpClient,
  parameters: WorkflowReassignInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}/reassign`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowReconcileRecoveryTaskParameters {
  readonly taskId: string;
  readonly body: ReconcileWorkflowRecoveryTaskRequest;
}

export async function workflowReconcileRecoveryTask(
  http: HttpClient,
  parameters: WorkflowReconcileRecoveryTaskParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowRecoveryTaskResponse> {
  const path = `/api/v1/workflow/recovery-tasks/${encodeURIComponent(String(parameters.taskId))}/reconcile`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowRecoveryTaskResponse(value);
}

export interface WorkflowRecoverInstanceParameters {
  readonly instanceId: string;
  readonly body: RecoverWorkflowInstanceRequest;
}

export async function workflowRecoverInstance(
  http: HttpClient,
  parameters: WorkflowRecoverInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}/recover`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowRejectTodoParameters {
  readonly todoId: string;
  readonly body: ActWorkflowTodoRequest;
}

export async function workflowRejectTodo(
  http: HttpClient,
  parameters: WorkflowRejectTodoParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/todos/${encodeURIComponent(String(parameters.todoId))}/reject`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowResumeInstanceParameters {
  readonly instanceId: string;
  readonly body: ResumeWorkflowInstanceRequest;
}

export async function workflowResumeInstance(
  http: HttpClient,
  parameters: WorkflowResumeInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances/${encodeURIComponent(String(parameters.instanceId))}/resume`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowRetryRecoveryTaskParameters {
  readonly taskId: string;
  readonly body: RetryWorkflowRecoveryTaskRequest;
}

export async function workflowRetryRecoveryTask(
  http: HttpClient,
  parameters: WorkflowRetryRecoveryTaskParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowRecoveryTaskResponse> {
  const path = `/api/v1/workflow/recovery-tasks/${encodeURIComponent(String(parameters.taskId))}/retry`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowRecoveryTaskResponse(value);
}

export interface WorkflowStartInstanceParameters {
  readonly body: StartWorkflowInstanceRequest;
}

export async function workflowStartInstance(
  http: HttpClient,
  parameters: WorkflowStartInstanceParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowInstanceResponse> {
  const path = `/api/v1/workflow/instances`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowInstanceResponse(value);
}

export interface WorkflowUpdateDefinitionDraftParameters {
  readonly definitionId: string;
  readonly body: UpdateWorkflowDefinitionDraftRequest;
}

export async function workflowUpdateDefinitionDraft(
  http: HttpClient,
  parameters: WorkflowUpdateDefinitionDraftParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowDefinitionResponse> {
  const path = `/api/v1/workflow/definitions/${encodeURIComponent(String(parameters.definitionId))}/draft`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowDefinitionResponse(value);
}

export interface WorkflowUpdateFormDraftParameters {
  readonly formId: string;
  readonly body: UpdateWorkflowFormDraftRequest;
}

export async function workflowUpdateFormDraft(
  http: HttpClient,
  parameters: WorkflowUpdateFormDraftParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WorkflowFormResponse> {
  const path = `/api/v1/workflow/forms/${encodeURIComponent(String(parameters.formId))}/draft`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readWorkflowFormResponse(value);
}
