// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 运行时响应守卫。

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
  ReturnWorkflowTodoRequest,
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
  WorkflowTodoReturnTargetResponse,
  WorkflowTodoRuntimeResponse
} from './models.generated.js';

export function readAccessHostDocumentShareRequest(value: unknown): AccessHostDocumentShareRequest {
  if (!(isAccessHostDocumentShareRequest(value))) {
    throw new Error('client.invalid_access_host_document_share_request');
  }
  return value;
}

function isAccessHostDocumentShareRequest(value: unknown): value is AccessHostDocumentShareRequest {
  return isRecord(value) && (value["password"] === undefined || ((value["password"] === null) || (typeof value["password"] === 'string')));
}

export function readAccessLogCursorPageResponse(value: unknown): AccessLogCursorPageResponse {
  if (!(isAccessLogCursorPageResponse(value))) {
    throw new Error('client.invalid_access_log_cursor_page_response');
  }
  return value;
}

function isAccessLogCursorPageResponse(value: unknown): value is AccessLogCursorPageResponse {
  return isRecord(value) && (typeof value["hasMore"] === 'boolean') && (Array.isArray(value["items"]) && value["items"].every(item14 => isAccessLogResponse(item14))) && ((value["nextCursor"] === null) || (typeof value["nextCursor"] === 'string'));
}

export function readAccessLogResponse(value: unknown): AccessLogResponse {
  if (!(isAccessLogResponse(value))) {
    throw new Error('client.invalid_access_log_response');
  }
  return value;
}

function isAccessLogResponse(value: unknown): value is AccessLogResponse {
  return isRecord(value) && ((value["clientIpFingerprint"] === null) || (typeof value["clientIpFingerprint"] === 'string')) && (typeof value["durationMs"] === 'number' && Number.isInteger(value["durationMs"])) && (typeof value["httpMethod"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isAuthenticated"] === 'boolean') && (typeof value["occurredAtUtc"] === 'string') && (typeof value["requestPath"] === 'string') && (typeof value["statusCode"] === 'number' && Number.isInteger(value["statusCode"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readActWorkflowTodoRequest(value: unknown): ActWorkflowTodoRequest {
  if (!(isActWorkflowTodoRequest(value))) {
    throw new Error('client.invalid_act_workflow_todo_request');
  }
  return value;
}

function isActWorkflowTodoRequest(value: unknown): value is ActWorkflowTodoRequest {
  return isRecord(value) && ((value["comment"] === null) || (typeof value["comment"] === 'string')) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (isJsonElement(value["fieldPatch"])) && (typeof value["idempotencyKey"] === 'string');
}

export function readAddHostDocumentVersionRequest(value: unknown): AddHostDocumentVersionRequest {
  if (!(isAddHostDocumentVersionRequest(value))) {
    throw new Error('client.invalid_add_host_document_version_request');
  }
  return value;
}

function isAddHostDocumentVersionRequest(value: unknown): value is AddHostDocumentVersionRequest {
  return isRecord(value) && ((value["changeDescription"] === null) || (typeof value["changeDescription"] === 'string')) && (typeof value["fileId"] === 'string' && guidPattern.test(value["fileId"]));
}

export function readAssignHostTenantPackageRequest(value: unknown): AssignHostTenantPackageRequest {
  if (!(isAssignHostTenantPackageRequest(value))) {
    throw new Error('client.invalid_assign_host_tenant_package_request');
  }
  return value;
}

function isAssignHostTenantPackageRequest(value: unknown): value is AssignHostTenantPackageRequest {
  return isRecord(value) && ((value["tenantPackageId"] === null) || (typeof value["tenantPackageId"] === 'string' && guidPattern.test(value["tenantPackageId"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readAssignOrganizationPositionLevelRequest(value: unknown): AssignOrganizationPositionLevelRequest {
  if (!(isAssignOrganizationPositionLevelRequest(value))) {
    throw new Error('client.invalid_assign_organization_position_level_request');
  }
  return value;
}

function isAssignOrganizationPositionLevelRequest(value: unknown): value is AssignOrganizationPositionLevelRequest {
  return isRecord(value) && ((value["positionLevelId"] === null) || (typeof value["positionLevelId"] === 'string' && guidPattern.test(value["positionLevelId"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readAssignOrganizationPositionUnitRequest(value: unknown): AssignOrganizationPositionUnitRequest {
  if (!(isAssignOrganizationPositionUnitRequest(value))) {
    throw new Error('client.invalid_assign_organization_position_unit_request');
  }
  return value;
}

function isAssignOrganizationPositionUnitRequest(value: unknown): value is AssignOrganizationPositionUnitRequest {
  return isRecord(value) && ((value["unitId"] === null) || (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readAuthorizationTreeActionResponse(value: unknown): AuthorizationTreeActionResponse {
  if (!(isAuthorizationTreeActionResponse(value))) {
    throw new Error('client.invalid_authorization_tree_action_response');
  }
  return value;
}

function isAuthorizationTreeActionResponse(value: unknown): value is AuthorizationTreeActionResponse {
  return isRecord(value) && (typeof value["id"] === 'string') && (typeof value["name"] === 'string') && (typeof value["order"] === 'number' && Number.isInteger(value["order"])) && (typeof value["permissionCode"] === 'string');
}

export function readAuthorizationTreeModuleResponse(value: unknown): AuthorizationTreeModuleResponse {
  if (!(isAuthorizationTreeModuleResponse(value))) {
    throw new Error('client.invalid_authorization_tree_module_response');
  }
  return value;
}

function isAuthorizationTreeModuleResponse(value: unknown): value is AuthorizationTreeModuleResponse {
  return isRecord(value) && (typeof value["id"] === 'string') && (typeof value["order"] === 'number' && Number.isInteger(value["order"])) && (Array.isArray(value["pages"]) && value["pages"].every(item14 => isAuthorizationTreePageResponse(item14))) && (typeof value["title"] === 'string');
}

export function readAuthorizationTreePageResponse(value: unknown): AuthorizationTreePageResponse {
  if (!(isAuthorizationTreePageResponse(value))) {
    throw new Error('client.invalid_authorization_tree_page_response');
  }
  return value;
}

function isAuthorizationTreePageResponse(value: unknown): value is AuthorizationTreePageResponse {
  return isRecord(value) && (Array.isArray(value["actions"]) && value["actions"].every(item16 => isAuthorizationTreeActionResponse(item16))) && (Array.isArray(value["children"]) && value["children"].every(item17 => isAuthorizationTreePageResponse(item17))) && (typeof value["id"] === 'string') && (typeof value["order"] === 'number' && Number.isInteger(value["order"])) && (typeof value["permissionCode"] === 'string') && (typeof value["title"] === 'string');
}

export function readBatchDeleteConfigEntriesRequest(value: unknown): BatchDeleteConfigEntriesRequest {
  if (!(isBatchDeleteConfigEntriesRequest(value))) {
    throw new Error('client.invalid_batch_delete_config_entries_request');
  }
  return value;
}

function isBatchDeleteConfigEntriesRequest(value: unknown): value is BatchDeleteConfigEntriesRequest {
  return isRecord(value) && (Array.isArray(value["ids"]) && value["ids"].every(item12 => typeof item12 === 'string' && guidPattern.test(item12)));
}

export function readBatchHostUserIdsRequest(value: unknown): BatchHostUserIdsRequest {
  if (!(isBatchHostUserIdsRequest(value))) {
    throw new Error('client.invalid_batch_host_user_ids_request');
  }
  return value;
}

function isBatchHostUserIdsRequest(value: unknown): value is BatchHostUserIdsRequest {
  return isRecord(value) && (Array.isArray(value["userIds"]) && value["userIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16)));
}

export function readBatchHostUserStatusItem(value: unknown): BatchHostUserStatusItem {
  if (!(isBatchHostUserStatusItem(value))) {
    throw new Error('client.invalid_batch_host_user_status_item');
  }
  return value;
}

function isBatchHostUserStatusItem(value: unknown): value is BatchHostUserStatusItem {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["message"] === null) || (typeof value["message"] === 'string')) && (typeof value["succeeded"] === 'boolean') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"]));
}

export function readBatchHostUserStatusResponse(value: unknown): BatchHostUserStatusResponse {
  if (!(isBatchHostUserStatusResponse(value))) {
    throw new Error('client.invalid_batch_host_user_status_response');
  }
  return value;
}

function isBatchHostUserStatusResponse(value: unknown): value is BatchHostUserStatusResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isBatchHostUserStatusItem(item16))) && (typeof value["succeededCount"] === 'number' && Number.isInteger(value["succeededCount"]));
}

export function readBatchUpdateConfigValuesRequest(value: unknown): BatchUpdateConfigValuesRequest {
  if (!(isBatchUpdateConfigValuesRequest(value))) {
    throw new Error('client.invalid_batch_update_config_values_request');
  }
  return value;
}

function isBatchUpdateConfigValuesRequest(value: unknown): value is BatchUpdateConfigValuesRequest {
  return isRecord(value) && (Array.isArray(value["updates"]) && value["updates"].every(item16 => isConfigValueUpdate(item16)));
}

export function readBeginTotpEnrollmentResponse(value: unknown): BeginTotpEnrollmentResponse {
  if (!(isBeginTotpEnrollmentResponse(value))) {
    throw new Error('client.invalid_begin_totp_enrollment_response');
  }
  return value;
}

function isBeginTotpEnrollmentResponse(value: unknown): value is BeginTotpEnrollmentResponse {
  return isRecord(value) && (typeof value["otpAuthUri"] === 'string') && (typeof value["sharedSecretBase32"] === 'string');
}

export function readCancelWorkflowInstanceRequest(value: unknown): CancelWorkflowInstanceRequest {
  if (!(isCancelWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_cancel_workflow_instance_request');
  }
  return value;
}

function isCancelWorkflowInstanceRequest(value: unknown): value is CancelWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readChangeHostJobScheduleStateRequest(value: unknown): ChangeHostJobScheduleStateRequest {
  if (!(isChangeHostJobScheduleStateRequest(value))) {
    throw new Error('client.invalid_change_host_job_schedule_state_request');
  }
  return value;
}

function isChangeHostJobScheduleStateRequest(value: unknown): value is ChangeHostJobScheduleStateRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readChangeSerialNumberRuleStatusRequest(value: unknown): ChangeSerialNumberRuleStatusRequest {
  if (!(isChangeSerialNumberRuleStatusRequest(value))) {
    throw new Error('client.invalid_change_serial_number_rule_status_request');
  }
  return value;
}

function isChangeSerialNumberRuleStatusRequest(value: unknown): value is ChangeSerialNumberRuleStatusRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readCodeGenerationCatalogColumnListResponse(value: unknown): CodeGenerationCatalogColumnListResponse {
  if (!(isCodeGenerationCatalogColumnListResponse(value))) {
    throw new Error('client.invalid_code_generation_catalog_column_list_response');
  }
  return value;
}

function isCodeGenerationCatalogColumnListResponse(value: unknown): value is CodeGenerationCatalogColumnListResponse {
  return isRecord(value) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (Array.isArray(value["skippedColumnNames"]) && value["skippedColumnNames"].every(item27 => typeof item27 === 'string')) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogColumnSyncRequest(value: unknown): CodeGenerationCatalogColumnSyncRequest {
  if (!(isCodeGenerationCatalogColumnSyncRequest(value))) {
    throw new Error('client.invalid_code_generation_catalog_column_sync_request');
  }
  return value;
}

function isCodeGenerationCatalogColumnSyncRequest(value: unknown): value is CodeGenerationCatalogColumnSyncRequest {
  return isRecord(value) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogColumnSyncResponse(value: unknown): CodeGenerationCatalogColumnSyncResponse {
  if (!(isCodeGenerationCatalogColumnSyncResponse(value))) {
    throw new Error('client.invalid_code_generation_catalog_column_sync_response');
  }
  return value;
}

function isCodeGenerationCatalogColumnSyncResponse(value: unknown): value is CodeGenerationCatalogColumnSyncResponse {
  return isRecord(value) && (Array.isArray(value["addedColumnNames"]) && value["addedColumnNames"].every(item25 => typeof item25 === 'string')) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (Array.isArray(value["removedColumnNames"]) && value["removedColumnNames"].every(item27 => typeof item27 === 'string')) && (Array.isArray(value["skippedColumnNames"]) && value["skippedColumnNames"].every(item27 => typeof item27 === 'string')) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogTableResponse(value: unknown): CodeGenerationCatalogTableResponse {
  if (!(isCodeGenerationCatalogTableResponse(value))) {
    throw new Error('client.invalid_code_generation_catalog_table_response');
  }
  return value;
}

function isCodeGenerationCatalogTableResponse(value: unknown): value is CodeGenerationCatalogTableResponse {
  return isRecord(value) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationClientRouteTargetRequest(value: unknown): CodeGenerationClientRouteTargetRequest {
  if (!(isCodeGenerationClientRouteTargetRequest(value))) {
    throw new Error('client.invalid_code_generation_client_route_target_request');
  }
  return value;
}

function isCodeGenerationClientRouteTargetRequest(value: unknown): value is CodeGenerationClientRouteTargetRequest {
  return isRecord(value) && (value["layuiControllerExport"] === undefined || ((value["layuiControllerExport"] === null) || (typeof value["layuiControllerExport"] === 'string'))) && (value["layuiControllerPath"] === undefined || ((value["layuiControllerPath"] === null) || (typeof value["layuiControllerPath"] === 'string'))) && (typeof value["routePath"] === 'string') && (typeof value["vueComponentPath"] === 'string') && (typeof value["vueRouteName"] === 'string');
}

export function readCodeGenerationEntityCapabilitiesRequest(value: unknown): CodeGenerationEntityCapabilitiesRequest {
  if (!(isCodeGenerationEntityCapabilitiesRequest(value))) {
    throw new Error('client.invalid_code_generation_entity_capabilities_request');
  }
  return value;
}

function isCodeGenerationEntityCapabilitiesRequest(value: unknown): value is CodeGenerationEntityCapabilitiesRequest {
  return isRecord(value) && (typeof value["deleteMode"] === 'string') && (typeof value["hasCreatedAudit"] === 'boolean') && (typeof value["hasDeletedAudit"] === 'boolean') && (typeof value["hasUpdatedAudit"] === 'boolean') && (typeof value["hasVersion"] === 'boolean') && (typeof value["ownershipMode"] === 'string');
}

export function readCodeGenerationIntegrationTargetRequest(value: unknown): CodeGenerationIntegrationTargetRequest {
  if (!(isCodeGenerationIntegrationTargetRequest(value))) {
    throw new Error('client.invalid_code_generation_integration_target_request');
  }
  return value;
}

function isCodeGenerationIntegrationTargetRequest(value: unknown): value is CodeGenerationIntegrationTargetRequest {
  return isRecord(value) && (value["authorizationContributorPath"] === undefined || ((value["authorizationContributorPath"] === null) || (typeof value["authorizationContributorPath"] === 'string'))) && (value["clientRoute"] === undefined || ((value["clientRoute"] === null) || (isCodeGenerationClientRouteTargetRequest(value["clientRoute"])))) && (typeof value["compositionCatalogPath"] === 'string') && (typeof value["compositionProjectPath"] === 'string') && (value["layuiRouterPath"] === undefined || ((value["layuiRouterPath"] === null) || (typeof value["layuiRouterPath"] === 'string'))) && (typeof value["moduleEntryPointPath"] === 'string') && (typeof value["moduleName"] === 'string') && (typeof value["moduleProjectPath"] === 'string') && (typeof value["vueRouterPath"] === 'string');
}

export function readCodeGenerationPreviewArtifactResponse(value: unknown): CodeGenerationPreviewArtifactResponse {
  if (!(isCodeGenerationPreviewArtifactResponse(value))) {
    throw new Error('client.invalid_code_generation_preview_artifact_response');
  }
  return value;
}

function isCodeGenerationPreviewArtifactResponse(value: unknown): value is CodeGenerationPreviewArtifactResponse {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["kind"] === 'string') && (typeof value["path"] === 'string') && (typeof value["sha256"] === 'string');
}

export function readCodeGenerationPreviewColumnRequest(value: unknown): CodeGenerationPreviewColumnRequest {
  if (!(isCodeGenerationPreviewColumnRequest(value))) {
    throw new Error('client.invalid_code_generation_preview_column_request');
  }
  return value;
}

function isCodeGenerationPreviewColumnRequest(value: unknown): value is CodeGenerationPreviewColumnRequest {
  return isRecord(value) && (typeof value["clrPropertyName"] === 'string') && (typeof value["databaseName"] === 'string') && (typeof value["isNullable"] === 'boolean') && (typeof value["jsonPropertyName"] === 'string') && ((value["maxLength"] === null) || (typeof value["maxLength"] === 'number' && Number.isInteger(value["maxLength"]))) && ((value["numericPrecision"] === null) || (typeof value["numericPrecision"] === 'number' && Number.isInteger(value["numericPrecision"]))) && ((value["numericScale"] === null) || (typeof value["numericScale"] === 'number' && Number.isInteger(value["numericScale"]))) && (typeof value["scalarType"] === 'string') && (value["ui"] === undefined || ((value["ui"] === null) || (isCodeGenerationPreviewColumnUiRequest(value["ui"]))));
}

export function readCodeGenerationPreviewColumnUiRequest(value: unknown): CodeGenerationPreviewColumnUiRequest {
  if (!(isCodeGenerationPreviewColumnUiRequest(value))) {
    throw new Error('client.invalid_code_generation_preview_column_ui_request');
  }
  return value;
}

function isCodeGenerationPreviewColumnUiRequest(value: unknown): value is CodeGenerationPreviewColumnUiRequest {
  return isRecord(value) && (typeof value["controlKind"] === 'string') && (typeof value["includeInCreate"] === 'boolean') && (typeof value["includeInImportExport"] === 'boolean') && (typeof value["includeInUpdate"] === 'boolean') && (typeof value["queryable"] === 'boolean') && (typeof value["queryKind"] === 'string') && (typeof value["required"] === 'boolean') && (typeof value["showInList"] === 'boolean') && (typeof value["sortable"] === 'boolean') && (typeof value["unique"] === 'boolean');
}

export function readCodeGenerationPreviewRequest(value: unknown): CodeGenerationPreviewRequest {
  if (!(isCodeGenerationPreviewRequest(value))) {
    throw new Error('client.invalid_code_generation_preview_request');
  }
  return value;
}

function isCodeGenerationPreviewRequest(value: unknown): value is CodeGenerationPreviewRequest {
  return isRecord(value) && (typeof value["apiResourceName"] === 'string') && (typeof value["clrTypeName"] === 'string') && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (typeof value["databaseTableName"] === 'string') && (typeof value["dataScope"] === 'string') && (value["entityCapabilities"] === undefined || ((value["entityCapabilities"] === null) || (isCodeGenerationEntityCapabilitiesRequest(value["entityCapabilities"])))) && (typeof value["entityKey"] === 'string') && (value["hasVersion"] === undefined || ((value["hasVersion"] === null) || (typeof value["hasVersion"] === 'boolean'))) && (typeof value["moduleKey"] === 'string') && (typeof value["ownerKey"] === 'string') && (typeof value["permissionResourceName"] === 'string') && (value["relationships"] === undefined || ((value["relationships"] === null) || (Array.isArray(value["relationships"]) && value["relationships"].every(item22 => isCodeGenerationRelationshipRequest(item22))))) && (typeof value["rootNamespace"] === 'string') && (value["scene"] === undefined || ((value["scene"] === null) || (typeof value["scene"] === 'string')));
}

export function readCodeGenerationPreviewResponse(value: unknown): CodeGenerationPreviewResponse {
  if (!(isCodeGenerationPreviewResponse(value))) {
    throw new Error('client.invalid_code_generation_preview_response');
  }
  return value;
}

function isCodeGenerationPreviewResponse(value: unknown): value is CodeGenerationPreviewResponse {
  return isRecord(value) && (Array.isArray(value["artifacts"]) && value["artifacts"].every(item18 => isCodeGenerationPreviewArtifactResponse(item18))) && (value["createPermission"] === undefined || ((value["createPermission"] === null) || (typeof value["createPermission"] === 'string'))) && (typeof value["databaseTableName"] === 'string') && (value["disablePermission"] === undefined || ((value["disablePermission"] === null) || (typeof value["disablePermission"] === 'string'))) && (typeof value["readPermission"] === 'string') && (value["updatePermission"] === undefined || ((value["updatePermission"] === null) || (typeof value["updatePermission"] === 'string'))) && (typeof value["writePermission"] === 'string');
}

export function readCodeGenerationRelationshipRequest(value: unknown): CodeGenerationRelationshipRequest {
  if (!(isCodeGenerationRelationshipRequest(value))) {
    throw new Error('client.invalid_code_generation_relationship_request');
  }
  return value;
}

function isCodeGenerationRelationshipRequest(value: unknown): value is CodeGenerationRelationshipRequest {
  return isRecord(value) && (value["cascadeDelete"] === undefined || ((value["cascadeDelete"] === null) || (typeof value["cascadeDelete"] === 'boolean'))) && (value["compositeKeyColumnNames"] === undefined || ((value["compositeKeyColumnNames"] === null) || (Array.isArray(value["compositeKeyColumnNames"]) && value["compositeKeyColumnNames"].every(item32 => typeof item32 === 'string')))) && (typeof value["dependentColumnName"] === 'string') && (typeof value["dependentDataScope"] === 'string') && (typeof value["dependentEntityKey"] === 'string') && (typeof value["principalColumnName"] === 'string') && (typeof value["principalDataScope"] === 'string') && (typeof value["principalEntityKey"] === 'string');
}

export function readCodeGenerationRunApplyRequest(value: unknown): CodeGenerationRunApplyRequest {
  if (!(isCodeGenerationRunApplyRequest(value))) {
    throw new Error('client.invalid_code_generation_run_apply_request');
  }
  return value;
}

function isCodeGenerationRunApplyRequest(value: unknown): value is CodeGenerationRunApplyRequest {
  return isRecord(value) && (value["integrationTarget"] === undefined || ((value["integrationTarget"] === null) || (isCodeGenerationIntegrationTargetRequest(value["integrationTarget"])))) && (typeof value["previewRunId"] === 'string' && guidPattern.test(value["previewRunId"]));
}

export function readCodeGenerationRunApplyResponse(value: unknown): CodeGenerationRunApplyResponse {
  if (!(isCodeGenerationRunApplyResponse(value))) {
    throw new Error('client.invalid_code_generation_run_apply_response');
  }
  return value;
}

function isCodeGenerationRunApplyResponse(value: unknown): value is CodeGenerationRunApplyResponse {
  return isRecord(value) && (typeof value["artifactCount"] === 'number' && Number.isInteger(value["artifactCount"])) && (typeof value["changedArtifactCount"] === 'number' && Number.isInteger(value["changedArtifactCount"])) && (typeof value["manifestSha256"] === 'string') && (typeof value["previewRunId"] === 'string' && guidPattern.test(value["previewRunId"])) && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]));
}

export function readCodeGenerationRunPreviewRequest(value: unknown): CodeGenerationRunPreviewRequest {
  if (!(isCodeGenerationRunPreviewRequest(value))) {
    throw new Error('client.invalid_code_generation_run_preview_request');
  }
  return value;
}

function isCodeGenerationRunPreviewRequest(value: unknown): value is CodeGenerationRunPreviewRequest {
  return isRecord(value) && ((value["schema"] === null) || (isCodeGenerationPreviewRequest(value["schema"]))) && ((value["templateId"] === null) || (typeof value["templateId"] === 'string' && guidPattern.test(value["templateId"]))) && ((value["templateVersion"] === null) || (typeof value["templateVersion"] === 'number' && Number.isInteger(value["templateVersion"])));
}

export function readCodeGenerationRunPreviewResponse(value: unknown): CodeGenerationRunPreviewResponse {
  if (!(isCodeGenerationRunPreviewResponse(value))) {
    throw new Error('client.invalid_code_generation_run_preview_response');
  }
  return value;
}

function isCodeGenerationRunPreviewResponse(value: unknown): value is CodeGenerationRunPreviewResponse {
  return isRecord(value) && (isCodeGenerationPreviewResponse(value["preview"])) && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]));
}

export function readCodeGenerationRunResponse(value: unknown): CodeGenerationRunResponse {
  if (!(isCodeGenerationRunResponse(value))) {
    throw new Error('client.invalid_code_generation_run_response');
  }
  return value;
}

function isCodeGenerationRunResponse(value: unknown): value is CodeGenerationRunResponse {
  return isRecord(value) && (typeof value["artifactCount"] === 'number' && Number.isInteger(value["artifactCount"])) && ((value["entityKey"] === null) || (typeof value["entityKey"] === 'string')) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["finishedAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["manifestSha256"] === null) || (typeof value["manifestSha256"] === 'string')) && ((value["moduleKey"] === null) || (typeof value["moduleKey"] === 'string')) && (typeof value["operationKind"] === 'string') && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && ((value["schemaSha256"] === null) || (typeof value["schemaSha256"] === 'string')) && ((value["sourceApplyRunId"] === null) || (typeof value["sourceApplyRunId"] === 'string' && guidPattern.test(value["sourceApplyRunId"]))) && (typeof value["startedAtUtc"] === 'string') && (typeof value["status"] === 'string') && ((value["templateId"] === null) || (typeof value["templateId"] === 'string' && guidPattern.test(value["templateId"]))) && ((value["templateVersion"] === null) || (typeof value["templateVersion"] === 'number' && Number.isInteger(value["templateVersion"])));
}

export function readCodeGenerationRunRollbackChainRequest(value: unknown): CodeGenerationRunRollbackChainRequest {
  if (!(isCodeGenerationRunRollbackChainRequest(value))) {
    throw new Error('client.invalid_code_generation_run_rollback_chain_request');
  }
  return value;
}

function isCodeGenerationRunRollbackChainRequest(value: unknown): value is CodeGenerationRunRollbackChainRequest {
  return isRecord(value) && (Array.isArray(value["applyRunIds"]) && value["applyRunIds"].every(item20 => typeof item20 === 'string' && guidPattern.test(item20)));
}

export function readCodeGenerationRunRollbackChainResponse(value: unknown): CodeGenerationRunRollbackChainResponse {
  if (!(isCodeGenerationRunRollbackChainResponse(value))) {
    throw new Error('client.invalid_code_generation_run_rollback_chain_response');
  }
  return value;
}

function isCodeGenerationRunRollbackChainResponse(value: unknown): value is CodeGenerationRunRollbackChainResponse {
  return isRecord(value) && (Array.isArray(value["rollbacks"]) && value["rollbacks"].every(item18 => isCodeGenerationRunRollbackResponse(item18)));
}

export function readCodeGenerationRunRollbackRequest(value: unknown): CodeGenerationRunRollbackRequest {
  if (!(isCodeGenerationRunRollbackRequest(value))) {
    throw new Error('client.invalid_code_generation_run_rollback_request');
  }
  return value;
}

function isCodeGenerationRunRollbackRequest(value: unknown): value is CodeGenerationRunRollbackRequest {
  return isRecord(value) && (typeof value["applyRunId"] === 'string' && guidPattern.test(value["applyRunId"]));
}

export function readCodeGenerationRunRollbackResponse(value: unknown): CodeGenerationRunRollbackResponse {
  if (!(isCodeGenerationRunRollbackResponse(value))) {
    throw new Error('client.invalid_code_generation_run_rollback_response');
  }
  return value;
}

function isCodeGenerationRunRollbackResponse(value: unknown): value is CodeGenerationRunRollbackResponse {
  return isRecord(value) && (typeof value["applyRunId"] === 'string' && guidPattern.test(value["applyRunId"])) && (typeof value["artifactCount"] === 'number' && Number.isInteger(value["artifactCount"])) && (typeof value["changedArtifactCount"] === 'number' && Number.isInteger(value["changedArtifactCount"])) && (typeof value["manifestSha256"] === 'string') && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]));
}

export function readCodeGenerationTemplateResponse(value: unknown): CodeGenerationTemplateResponse {
  if (!(isCodeGenerationTemplateResponse(value))) {
    throw new Error('client.invalid_code_generation_template_response');
  }
  return value;
}

function isCodeGenerationTemplateResponse(value: unknown): value is CodeGenerationTemplateResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && (isCodeGenerationPreviewRequest(value["schema"])) && (typeof value["schemaSha256"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readConfigEntryResponse(value: unknown): ConfigEntryResponse {
  if (!(isConfigEntryResponse(value))) {
    throw new Error('client.invalid_config_entry_response');
  }
  return value;
}

function isConfigEntryResponse(value: unknown): value is ConfigEntryResponse {
  return isRecord(value) && (typeof value["configKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["hasValue"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["valueKind"] === 'string' && ["string", "boolean", "integer", "decimal", "json", "secret"].includes(value["valueKind"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readConfigValueUpdate(value: unknown): ConfigValueUpdate {
  if (!(isConfigValueUpdate(value))) {
    throw new Error('client.invalid_config_value_update');
  }
  return value;
}

function isConfigValueUpdate(value: unknown): value is ConfigValueUpdate {
  return isRecord(value) && (typeof value["configKey"] === 'string') && (typeof value["value"] === 'string');
}

export function readConfirmTotpEnrollmentRequest(value: unknown): ConfirmTotpEnrollmentRequest {
  if (!(isConfirmTotpEnrollmentRequest(value))) {
    throw new Error('client.invalid_confirm_totp_enrollment_request');
  }
  return value;
}

function isConfirmTotpEnrollmentRequest(value: unknown): value is ConfirmTotpEnrollmentRequest {
  return isRecord(value) && (typeof value["totpCode"] === 'string');
}

export function readCreateCodeGenerationTemplateRequest(value: unknown): CreateCodeGenerationTemplateRequest {
  if (!(isCreateCodeGenerationTemplateRequest(value))) {
    throw new Error('client.invalid_create_code_generation_template_request');
  }
  return value;
}

function isCreateCodeGenerationTemplateRequest(value: unknown): value is CreateCodeGenerationTemplateRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string') && (isCodeGenerationPreviewRequest(value["schema"]));
}

export function readCreateConfigEntryRequest(value: unknown): CreateConfigEntryRequest {
  if (!(isCreateConfigEntryRequest(value))) {
    throw new Error('client.invalid_create_config_entry_request');
  }
  return value;
}

function isCreateConfigEntryRequest(value: unknown): value is CreateConfigEntryRequest {
  return isRecord(value) && (typeof value["configKey"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["valueKind"] === 'string' && ["string", "boolean", "integer", "decimal", "json", "secret"].includes(value["valueKind"]));
}

export function readCreateDictItemRequest(value: unknown): CreateDictItemRequest {
  if (!(isCreateDictItemRequest(value))) {
    throw new Error('client.invalid_create_dict_item_request');
  }
  return value;
}

function isCreateDictItemRequest(value: unknown): value is CreateDictItemRequest {
  return isRecord(value) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["label"] === 'string') && (typeof value["value"] === 'string');
}

export function readCreateDictTypeRequest(value: unknown): CreateDictTypeRequest {
  if (!(isCreateDictTypeRequest(value))) {
    throw new Error('client.invalid_create_dict_type_request');
  }
  return value;
}

function isCreateDictTypeRequest(value: unknown): value is CreateDictTypeRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string');
}

export function readCreateHostAnnouncementRequest(value: unknown): CreateHostAnnouncementRequest {
  if (!(isCreateHostAnnouncementRequest(value))) {
    throw new Error('client.invalid_create_host_announcement_request');
  }
  return value;
}

function isCreateHostAnnouncementRequest(value: unknown): value is CreateHostAnnouncementRequest {
  return isRecord(value) && (value["audienceKind"] === undefined || ((value["audienceKind"] === null) || (typeof value["audienceKind"] === 'string'))) && (typeof value["content"] === 'string') && (value["kind"] === undefined || ((value["kind"] === null) || (typeof value["kind"] === 'string'))) && (value["targetOrganizations"] === undefined || ((value["targetOrganizations"] === null) || (Array.isArray(value["targetOrganizations"]) && value["targetOrganizations"].every(item28 => isHostAnnouncementTargetOrganization(item28))))) && (value["targetUserIds"] === undefined || ((value["targetUserIds"] === null) || (Array.isArray(value["targetUserIds"]) && value["targetUserIds"].every(item22 => typeof item22 === 'string' && guidPattern.test(item22))))) && (typeof value["title"] === 'string');
}

export function readCreateHostApiKeyRequest(value: unknown): CreateHostApiKeyRequest {
  if (!(isCreateHostApiKeyRequest(value))) {
    throw new Error('client.invalid_create_host_api_key_request');
  }
  return value;
}

function isCreateHostApiKeyRequest(value: unknown): value is CreateHostApiKeyRequest {
  return isRecord(value) && (typeof value["displayName"] === 'string') && ((value["expiresAtUtc"] === null) || (typeof value["expiresAtUtc"] === 'string')) && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => typeof item20 === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"]));
}

export function readCreateHostApiKeyResponse(value: unknown): CreateHostApiKeyResponse {
  if (!(isCreateHostApiKeyResponse(value))) {
    throw new Error('client.invalid_create_host_api_key_response');
  }
  return value;
}

function isCreateHostApiKeyResponse(value: unknown): value is CreateHostApiKeyResponse {
  return isRecord(value) && (isHostApiKeyResponse(value["key"])) && (typeof value["secret"] === 'string');
}

export function readCreateHostDocumentCategoryRequest(value: unknown): CreateHostDocumentCategoryRequest {
  if (!(isCreateHostDocumentCategoryRequest(value))) {
    throw new Error('client.invalid_create_host_document_category_request');
  }
  return value;
}

function isCreateHostDocumentCategoryRequest(value: unknown): value is CreateHostDocumentCategoryRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isInteger(value["sortOrder"]));
}

export function readCreateHostDocumentItemRequest(value: unknown): CreateHostDocumentItemRequest {
  if (!(isCreateHostDocumentItemRequest(value))) {
    throw new Error('client.invalid_create_host_document_item_request');
  }
  return value;
}

function isCreateHostDocumentItemRequest(value: unknown): value is CreateHostDocumentItemRequest {
  return isRecord(value) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (isHostDocumentType(value["documentType"])) && (typeof value["sort"] === 'number' && Number.isInteger(value["sort"])) && (isHostDocumentStatus(value["status"])) && ((value["tagIds"] === null) || (Array.isArray(value["tagIds"]) && value["tagIds"].every(item15 => typeof item15 === 'string' && guidPattern.test(item15)))) && ((value["thumbnail"] === null) || (typeof value["thumbnail"] === 'string')) && (typeof value["title"] === 'string');
}

export function readCreateHostDocumentShareRequest(value: unknown): CreateHostDocumentShareRequest {
  if (!(isCreateHostDocumentShareRequest(value))) {
    throw new Error('client.invalid_create_host_document_share_request');
  }
  return value;
}

function isCreateHostDocumentShareRequest(value: unknown): value is CreateHostDocumentShareRequest {
  return isRecord(value) && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && (value["maxAccessCount"] === undefined || ((value["maxAccessCount"] === null) || (typeof value["maxAccessCount"] === 'number' && Number.isInteger(value["maxAccessCount"])))) && (value["password"] === undefined || ((value["password"] === null) || (typeof value["password"] === 'string'))) && (typeof value["validDays"] === 'number' && Number.isInteger(value["validDays"]));
}

export function readCreateHostDocumentTagRequest(value: unknown): CreateHostDocumentTagRequest {
  if (!(isCreateHostDocumentTagRequest(value))) {
    throw new Error('client.invalid_create_host_document_tag_request');
  }
  return value;
}

function isCreateHostDocumentTagRequest(value: unknown): value is CreateHostDocumentTagRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["name"] === 'string');
}

export function readCreateHostJobDefinitionRequest(value: unknown): CreateHostJobDefinitionRequest {
  if (!(isCreateHostJobDefinitionRequest(value))) {
    throw new Error('client.invalid_create_host_job_definition_request');
  }
  return value;
}

function isCreateHostJobDefinitionRequest(value: unknown): value is CreateHostJobDefinitionRequest {
  return isRecord(value) && (value["allowConcurrentExecutions"] === undefined || (typeof value["allowConcurrentExecutions"] === 'boolean')) && ((value["args"] === null) || (isHttpJobArgs(value["args"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["handlerKind"] === 'string') && (typeof value["jobKey"] === 'string');
}

export function readCreateHostJobScheduleRequest(value: unknown): CreateHostJobScheduleRequest {
  if (!(isCreateHostJobScheduleRequest(value))) {
    throw new Error('client.invalid_create_host_job_schedule_request');
  }
  return value;
}

function isCreateHostJobScheduleRequest(value: unknown): value is CreateHostJobScheduleRequest {
  return isRecord(value) && ((value["args"] === null) || (typeof value["args"] === 'string')) && ((value["cronExpression"] === null) || (typeof value["cronExpression"] === 'string')) && ((value["endTime"] === null) || (typeof value["endTime"] === 'string')) && (typeof value["jobDefinitionId"] === 'string' && guidPattern.test(value["jobDefinitionId"])) && (typeof value["misfirePolicy"] === 'string') && ((value["oneTimeAtUtc"] === null) || (typeof value["oneTimeAtUtc"] === 'string')) && ((value["startTime"] === null) || (typeof value["startTime"] === 'string')) && (typeof value["timeZoneId"] === 'string') && (typeof value["triggerKind"] === 'string');
}

export function readCreateHostMenuRequest(value: unknown): CreateHostMenuRequest {
  if (!(isCreateHostMenuRequest(value))) {
    throw new Error('client.invalid_create_host_menu_request');
  }
  return value;
}

function isCreateHostMenuRequest(value: unknown): value is CreateHostMenuRequest {
  return isRecord(value) && (typeof value["caption"] === 'string') && (typeof value["componentKey"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["icon"] === 'string') && (value["isAffix"] === undefined || (typeof value["isAffix"] === 'boolean')) && (value["isEmbedded"] === undefined || (typeof value["isEmbedded"] === 'boolean')) && (value["isHidden"] === undefined || (typeof value["isHidden"] === 'boolean')) && (value["isKeepAlive"] === undefined || (typeof value["isKeepAlive"] === 'boolean')) && (value["linkUrl"] === undefined || ((value["linkUrl"] === null) || (typeof value["linkUrl"] === 'string'))) && (value["menuType"] === undefined || (typeof value["menuType"] === 'string')) && ((value["parentId"] === null) || (typeof value["parentId"] === 'string')) && (typeof value["path"] === 'string') && (value["redirect"] === undefined || ((value["redirect"] === null) || (typeof value["redirect"] === 'string'))) && (value["remark"] === undefined || ((value["remark"] === null) || (typeof value["remark"] === 'string'))) && (typeof value["requiredPermission"] === 'string') && (typeof value["routeName"] === 'string') && (typeof value["title"] === 'string');
}

export function readCreateHostRoleRequest(value: unknown): CreateHostRoleRequest {
  if (!(isCreateHostRoleRequest(value))) {
    throw new Error('client.invalid_create_host_role_request');
  }
  return value;
}

function isCreateHostRoleRequest(value: unknown): value is CreateHostRoleRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["name"] === 'string');
}

export function readCreateHostTenantPackageRequest(value: unknown): CreateHostTenantPackageRequest {
  if (!(isCreateHostTenantPackageRequest(value))) {
    throw new Error('client.invalid_create_host_tenant_package_request');
  }
  return value;
}

function isCreateHostTenantPackageRequest(value: unknown): value is CreateHostTenantPackageRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string');
}

export function readCreateHostUserRequest(value: unknown): CreateHostUserRequest {
  if (!(isCreateHostUserRequest(value))) {
    throw new Error('client.invalid_create_host_user_request');
  }
  return value;
}

function isCreateHostUserRequest(value: unknown): value is CreateHostUserRequest {
  return isRecord(value) && (value["accountType"] === undefined || ((value["accountType"] === null) || (typeof value["accountType"] === 'string'))) && (typeof value["displayName"] === 'string') && (typeof value["password"] === 'string') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"])))) && (typeof value["username"] === 'string');
}

export function readCreateMyRecipientEndpointRequest(value: unknown): CreateMyRecipientEndpointRequest {
  if (!(isCreateMyRecipientEndpointRequest(value))) {
    throw new Error('client.invalid_create_my_recipient_endpoint_request');
  }
  return value;
}

function isCreateMyRecipientEndpointRequest(value: unknown): value is CreateMyRecipientEndpointRequest {
  return isRecord(value) && (typeof value["endpointKindKey"] === 'string') && (typeof value["providerProfileVersionId"] === 'string' && guidPattern.test(value["providerProfileVersionId"])) && (typeof value["rawValue"] === 'string');
}

export function readCreateNotificationBindingRequest(value: unknown): CreateNotificationBindingRequest {
  if (!(isCreateNotificationBindingRequest(value))) {
    throw new Error('client.invalid_create_notification_binding_request');
  }
  return value;
}

function isCreateNotificationBindingRequest(value: unknown): value is CreateNotificationBindingRequest {
  return isRecord(value) && (typeof value["bindingKey"] === 'string') && (typeof value["channelKey"] === 'string') && (typeof value["dispatchModeKey"] === 'string') && (typeof value["producerKey"] === 'string') && (typeof value["sceneKey"] === 'string') && (Array.isArray(value["targets"]) && value["targets"].every(item16 => isNotificationBindingTargetInput(item16)));
}

export function readCreateNotificationProviderProfileRequest(value: unknown): CreateNotificationProviderProfileRequest {
  if (!(isCreateNotificationProviderProfileRequest(value))) {
    throw new Error('client.invalid_create_notification_provider_profile_request');
  }
  return value;
}

function isCreateNotificationProviderProfileRequest(value: unknown): value is CreateNotificationProviderProfileRequest {
  return isRecord(value) && (isJsonElement(value["nonSecretConfig"])) && (typeof value["profileKey"] === 'string') && (typeof value["providerTypeKey"] === 'string') && ((value["secretReference"] === null) || (typeof value["secretReference"] === 'string'));
}

export function readCreateNotificationTemplateRequest(value: unknown): CreateNotificationTemplateRequest {
  if (!(isCreateNotificationTemplateRequest(value))) {
    throw new Error('client.invalid_create_notification_template_request');
  }
  return value;
}

function isCreateNotificationTemplateRequest(value: unknown): value is CreateNotificationTemplateRequest {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["contentCategoryKey"] === 'string') && (isNotificationTemplateBody(value["draftBody"])) && (typeof value["draftSubject"] === 'string') && (isNotificationTemplateParameterSchema(value["parameterSchema"])) && (typeof value["templateKey"] === 'string');
}

export function readCreateOrganizationPositionLevelRequest(value: unknown): CreateOrganizationPositionLevelRequest {
  if (!(isCreateOrganizationPositionLevelRequest(value))) {
    throw new Error('client.invalid_create_organization_position_level_request');
  }
  return value;
}

function isCreateOrganizationPositionLevelRequest(value: unknown): value is CreateOrganizationPositionLevelRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string');
}

export function readCreateOrganizationPositionRequest(value: unknown): CreateOrganizationPositionRequest {
  if (!(isCreateOrganizationPositionRequest(value))) {
    throw new Error('client.invalid_create_organization_position_request');
  }
  return value;
}

function isCreateOrganizationPositionRequest(value: unknown): value is CreateOrganizationPositionRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string');
}

export function readCreateOrganizationUnitRequest(value: unknown): CreateOrganizationUnitRequest {
  if (!(isCreateOrganizationUnitRequest(value))) {
    throw new Error('client.invalid_create_organization_unit_request');
  }
  return value;
}

function isCreateOrganizationUnitRequest(value: unknown): value is CreateOrganizationUnitRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string'));
}

export function readCreateOrganizationUserPositionRequest(value: unknown): CreateOrganizationUserPositionRequest {
  if (!(isCreateOrganizationUserPositionRequest(value))) {
    throw new Error('client.invalid_create_organization_user_position_request');
  }
  return value;
}

function isCreateOrganizationUserPositionRequest(value: unknown): value is CreateOrganizationUserPositionRequest {
  return isRecord(value) && (typeof value["isPrimary"] === 'boolean') && (typeof value["positionId"] === 'string' && guidPattern.test(value["positionId"])) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"]));
}

export function readCreateOrganizationUserUnitRequest(value: unknown): CreateOrganizationUserUnitRequest {
  if (!(isCreateOrganizationUserUnitRequest(value))) {
    throw new Error('client.invalid_create_organization_user_unit_request');
  }
  return value;
}

function isCreateOrganizationUserUnitRequest(value: unknown): value is CreateOrganizationUserUnitRequest {
  return isRecord(value) && (typeof value["isPrimary"] === 'boolean') && (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"])) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"]));
}

export function readCreateSerialNumberRuleRequest(value: unknown): CreateSerialNumberRuleRequest {
  if (!(isCreateSerialNumberRuleRequest(value))) {
    throw new Error('client.invalid_create_serial_number_rule_request');
  }
  return value;
}

function isCreateSerialNumberRuleRequest(value: unknown): value is CreateSerialNumberRuleRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maximumValue"] === 'number' && Number.isInteger(value["maximumValue"])) && (typeof value["minimumValue"] === 'number' && Number.isInteger(value["minimumValue"])) && (typeof value["pattern"] === 'string') && (isSerialNumberResetInterval(value["resetInterval"])) && (typeof value["ruleKey"] === 'string') && (isSerialNumberRuleScope(value["scope"]));
}

export function readCreateWorkflowDefinitionRequest(value: unknown): CreateWorkflowDefinitionRequest {
  if (!(isCreateWorkflowDefinitionRequest(value))) {
    throw new Error('client.invalid_create_workflow_definition_request');
  }
  return value;
}

function isCreateWorkflowDefinitionRequest(value: unknown): value is CreateWorkflowDefinitionRequest {
  return isRecord(value) && (typeof value["definitionKey"] === 'string') && (isWorkflowDefinitionDraft(value["draft"]));
}

export function readCreateWorkflowFormRequest(value: unknown): CreateWorkflowFormRequest {
  if (!(isCreateWorkflowFormRequest(value))) {
    throw new Error('client.invalid_create_workflow_form_request');
  }
  return value;
}

function isCreateWorkflowFormRequest(value: unknown): value is CreateWorkflowFormRequest {
  return isRecord(value) && (isWorkflowFormSchema(value["draft"])) && (typeof value["formKey"] === 'string');
}

export function readCurrentUserResponse(value: unknown): CurrentUserResponse {
  if (!(isCurrentUserResponse(value))) {
    throw new Error('client.invalid_current_user_response');
  }
  return value;
}

function isCurrentUserResponse(value: unknown): value is CurrentUserResponse {
  return isRecord(value) && (typeof value["actorScope"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isSuperAdministrator"] === 'boolean') && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => typeof item20 === 'string')) && (typeof value["preferredLocale"] === 'string') && (typeof value["profileVersion"] === 'number' && Number.isInteger(value["profileVersion"])) && (typeof value["scope"] === 'string') && (typeof value["sessionId"] === 'string' && guidPattern.test(value["sessionId"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["username"] === 'string');
}

export function readDeleteCodeGenerationTemplateRequest(value: unknown): DeleteCodeGenerationTemplateRequest {
  if (!(isDeleteCodeGenerationTemplateRequest(value))) {
    throw new Error('client.invalid_delete_code_generation_template_request');
  }
  return value;
}

function isDeleteCodeGenerationTemplateRequest(value: unknown): value is DeleteCodeGenerationTemplateRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteConfigEntryRequest(value: unknown): DeleteConfigEntryRequest {
  if (!(isDeleteConfigEntryRequest(value))) {
    throw new Error('client.invalid_delete_config_entry_request');
  }
  return value;
}

function isDeleteConfigEntryRequest(value: unknown): value is DeleteConfigEntryRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteDictItemRequest(value: unknown): DeleteDictItemRequest {
  if (!(isDeleteDictItemRequest(value))) {
    throw new Error('client.invalid_delete_dict_item_request');
  }
  return value;
}

function isDeleteDictItemRequest(value: unknown): value is DeleteDictItemRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteDictTypeRequest(value: unknown): DeleteDictTypeRequest {
  if (!(isDeleteDictTypeRequest(value))) {
    throw new Error('client.invalid_delete_dict_type_request');
  }
  return value;
}

function isDeleteDictTypeRequest(value: unknown): value is DeleteDictTypeRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteHostDocumentCategoryRequest(value: unknown): DeleteHostDocumentCategoryRequest {
  if (!(isDeleteHostDocumentCategoryRequest(value))) {
    throw new Error('client.invalid_delete_host_document_category_request');
  }
  return value;
}

function isDeleteHostDocumentCategoryRequest(value: unknown): value is DeleteHostDocumentCategoryRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteHostDocumentItemRequest(value: unknown): DeleteHostDocumentItemRequest {
  if (!(isDeleteHostDocumentItemRequest(value))) {
    throw new Error('client.invalid_delete_host_document_item_request');
  }
  return value;
}

function isDeleteHostDocumentItemRequest(value: unknown): value is DeleteHostDocumentItemRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteHostDocumentTagRequest(value: unknown): DeleteHostDocumentTagRequest {
  if (!(isDeleteHostDocumentTagRequest(value))) {
    throw new Error('client.invalid_delete_host_document_tag_request');
  }
  return value;
}

function isDeleteHostDocumentTagRequest(value: unknown): value is DeleteHostDocumentTagRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDeleteHostJobDefinitionRequest(value: unknown): DeleteHostJobDefinitionRequest {
  if (!(isDeleteHostJobDefinitionRequest(value))) {
    throw new Error('client.invalid_delete_host_job_definition_request');
  }
  return value;
}

function isDeleteHostJobDefinitionRequest(value: unknown): value is DeleteHostJobDefinitionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDiagnosticPolicyResponse(value: unknown): DiagnosticPolicyResponse {
  if (!(isDiagnosticPolicyResponse(value))) {
    throw new Error('client.invalid_diagnostic_policy_response');
  }
  return value;
}

function isDiagnosticPolicyResponse(value: unknown): value is DiagnosticPolicyResponse {
  return isRecord(value) && (Array.isArray(value["activeRules"]) && value["activeRules"].every(item20 => isDiagnosticPolicyRuleResponse(item20))) && (typeof value["configEntryVersion"] === 'number' && Number.isInteger(value["configEntryVersion"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["loadedAtUtc"] === 'string') && (typeof value["pressureState"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDiagnosticPolicyRuleRequest(value: unknown): DiagnosticPolicyRuleRequest {
  if (!(isDiagnosticPolicyRuleRequest(value))) {
    throw new Error('client.invalid_diagnostic_policy_rule_request');
  }
  return value;
}

function isDiagnosticPolicyRuleRequest(value: unknown): value is DiagnosticPolicyRuleRequest {
  return isRecord(value) && ((value["bestEffortCapacityOverride"] === null) || (typeof value["bestEffortCapacityOverride"] === 'number' && Number.isInteger(value["bestEffortCapacityOverride"]))) && (typeof value["expiresAtUtc"] === 'string') && ((value["maxRequestPayloadBytesOverride"] === null) || (typeof value["maxRequestPayloadBytesOverride"] === 'number' && Number.isInteger(value["maxRequestPayloadBytesOverride"]))) && ((value["maxResponsePayloadBytesOverride"] === null) || (typeof value["maxResponsePayloadBytesOverride"] === 'number' && Number.isInteger(value["maxResponsePayloadBytesOverride"]))) && (typeof value["scopeKind"] === 'string') && (typeof value["scopeValue"] === 'string') && ((value["successSampleRateOverride"] === null) || (typeof value["successSampleRateOverride"] === 'number' && Number.isFinite(value["successSampleRateOverride"])) || (typeof value["successSampleRateOverride"] === 'string'));
}

export function readDiagnosticPolicyRuleResponse(value: unknown): DiagnosticPolicyRuleResponse {
  if (!(isDiagnosticPolicyRuleResponse(value))) {
    throw new Error('client.invalid_diagnostic_policy_rule_response');
  }
  return value;
}

function isDiagnosticPolicyRuleResponse(value: unknown): value is DiagnosticPolicyRuleResponse {
  return isRecord(value) && ((value["bestEffortCapacityOverride"] === null) || (typeof value["bestEffortCapacityOverride"] === 'number' && Number.isInteger(value["bestEffortCapacityOverride"]))) && (typeof value["expiresAtUtc"] === 'string') && ((value["maxRequestPayloadBytesOverride"] === null) || (typeof value["maxRequestPayloadBytesOverride"] === 'number' && Number.isInteger(value["maxRequestPayloadBytesOverride"]))) && ((value["maxResponsePayloadBytesOverride"] === null) || (typeof value["maxResponsePayloadBytesOverride"] === 'number' && Number.isInteger(value["maxResponsePayloadBytesOverride"]))) && (typeof value["scopeKind"] === 'string') && (typeof value["scopeValue"] === 'string') && ((value["successSampleRateOverride"] === null) || (typeof value["successSampleRateOverride"] === 'number' && Number.isFinite(value["successSampleRateOverride"])) || (typeof value["successSampleRateOverride"] === 'string'));
}

export function readDictItemResponse(value: unknown): DictItemResponse {
  if (!(isDictItemResponse(value))) {
    throw new Error('client.invalid_dict_item_response');
  }
  return value;
}

function isDictItemResponse(value: unknown): value is DictItemResponse {
  return isRecord(value) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["dictTypeId"] === 'string' && guidPattern.test(value["dictTypeId"])) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["label"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDictTypeResponse(value: unknown): DictTypeResponse {
  if (!(isDictTypeResponse(value))) {
    throw new Error('client.invalid_dict_type_response');
  }
  return value;
}

function isDictTypeResponse(value: unknown): value is DictTypeResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readDisableHostJobDefinitionRequest(value: unknown): DisableHostJobDefinitionRequest {
  if (!(isDisableHostJobDefinitionRequest(value))) {
    throw new Error('client.invalid_disable_host_job_definition_request');
  }
  return value;
}

function isDisableHostJobDefinitionRequest(value: unknown): value is DisableHostJobDefinitionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readEnumCatalogDetail(value: unknown): EnumCatalogDetail {
  if (!(isEnumCatalogDetail(value))) {
    throw new Error('client.invalid_enum_catalog_detail');
  }
  return value;
}

function isEnumCatalogDetail(value: unknown): value is EnumCatalogDetail {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["key"] === 'string') && (Array.isArray(value["members"]) && value["members"].every(item16 => isEnumCatalogMember(item16)));
}

export function readEnumCatalogMember(value: unknown): EnumCatalogMember {
  if (!(isEnumCatalogMember(value))) {
    throw new Error('client.invalid_enum_catalog_member');
  }
  return value;
}

function isEnumCatalogMember(value: unknown): value is EnumCatalogMember {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["label"] === 'string');
}

export function readEnumCatalogSummary(value: unknown): EnumCatalogSummary {
  if (!(isEnumCatalogSummary(value))) {
    throw new Error('client.invalid_enum_catalog_summary');
  }
  return value;
}

function isEnumCatalogSummary(value: unknown): value is EnumCatalogSummary {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["key"] === 'string') && (typeof value["memberCount"] === 'number' && Number.isInteger(value["memberCount"]));
}

export function readExceptionLogResponse(value: unknown): ExceptionLogResponse {
  if (!(isExceptionLogResponse(value))) {
    throw new Error('client.invalid_exception_log_response');
  }
  return value;
}

function isExceptionLogResponse(value: unknown): value is ExceptionLogResponse {
  return isRecord(value) && ((value["clientIpFingerprint"] === null) || (typeof value["clientIpFingerprint"] === 'string')) && (typeof value["exceptionType"] === 'string') && ((value["httpMethod"] === null) || (typeof value["httpMethod"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["message"] === 'string') && (typeof value["occurredAtUtc"] === 'string') && ((value["requestPath"] === null) || (typeof value["requestPath"] === 'string')) && ((value["stackTrace"] === null) || (typeof value["stackTrace"] === 'string')) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readFieldProjectionDefaultVisibility(value: unknown): FieldProjectionDefaultVisibility {
  if (!(isFieldProjectionDefaultVisibility(value))) {
    throw new Error('client.invalid_field_projection_default_visibility');
  }
  return value;
}

function isFieldProjectionDefaultVisibility(value: unknown): value is FieldProjectionDefaultVisibility {
  return typeof value === 'number' && Number.isInteger(value);
}

export function readFieldProjectionFieldDefinition(value: unknown): FieldProjectionFieldDefinition {
  if (!(isFieldProjectionFieldDefinition(value))) {
    throw new Error('client.invalid_field_projection_field_definition');
  }
  return value;
}

function isFieldProjectionFieldDefinition(value: unknown): value is FieldProjectionFieldDefinition {
  return isRecord(value) && (typeof value["assignable"] === 'boolean') && (isFieldProjectionDefaultVisibility(value["defaultVisibility"])) && (typeof value["displayName"] === 'string') && (typeof value["fieldKey"] === 'string') && (isFieldProjectionSensitivity(value["sensitivity"]));
}

export function readFieldProjectionResourceDefinition(value: unknown): FieldProjectionResourceDefinition {
  if (!(isFieldProjectionResourceDefinition(value))) {
    throw new Error('client.invalid_field_projection_resource_definition');
  }
  return value;
}

function isFieldProjectionResourceDefinition(value: unknown): value is FieldProjectionResourceDefinition {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (Array.isArray(value["fields"]) && value["fields"].every(item15 => isFieldProjectionFieldDefinition(item15))) && (typeof value["resourceKey"] === 'string');
}

export function readFieldProjectionSensitivity(value: unknown): FieldProjectionSensitivity {
  if (!(isFieldProjectionSensitivity(value))) {
    throw new Error('client.invalid_field_projection_sensitivity');
  }
  return value;
}

function isFieldProjectionSensitivity(value: unknown): value is FieldProjectionSensitivity {
  return typeof value === 'number' && Number.isInteger(value);
}

export function readGrantSuperAdministratorRequest(value: unknown): GrantSuperAdministratorRequest {
  if (!(isGrantSuperAdministratorRequest(value))) {
    throw new Error('client.invalid_grant_super_administrator_request');
  }
  return value;
}

function isGrantSuperAdministratorRequest(value: unknown): value is GrantSuperAdministratorRequest {
  return isRecord(value) && (typeof value["currentPassword"] === 'string') && (value["totpCode"] === undefined || ((value["totpCode"] === null) || (typeof value["totpCode"] === 'string'))) && (typeof value["username"] === 'string');
}

export function readHostAnnouncementResponse(value: unknown): HostAnnouncementResponse {
  if (!(isHostAnnouncementResponse(value))) {
    throw new Error('client.invalid_host_announcement_response');
  }
  return value;
}

function isHostAnnouncementResponse(value: unknown): value is HostAnnouncementResponse {
  return isRecord(value) && (typeof value["audienceKind"] === 'string') && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["kind"] === 'string') && ((value["publishedAtUtc"] === null) || (typeof value["publishedAtUtc"] === 'string')) && ((value["publishedByUserId"] === null) || (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"]))) && ((value["retractedAtUtc"] === null) || (typeof value["retractedAtUtc"] === 'string')) && ((value["retractedByUserId"] === null) || (typeof value["retractedByUserId"] === 'string' && guidPattern.test(value["retractedByUserId"]))) && (typeof value["status"] === 'string') && (Array.isArray(value["targetOrganizations"]) && value["targetOrganizations"].every(item28 => isHostAnnouncementTargetOrganization(item28))) && (Array.isArray(value["targetUserIds"]) && value["targetUserIds"].every(item22 => typeof item22 === 'string' && guidPattern.test(item22))) && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostAnnouncementTargetOrganization(value: unknown): HostAnnouncementTargetOrganization {
  if (!(isHostAnnouncementTargetOrganization(value))) {
    throw new Error('client.invalid_host_announcement_target_organization');
  }
  return value;
}

function isHostAnnouncementTargetOrganization(value: unknown): value is HostAnnouncementTargetOrganization {
  return isRecord(value) && (typeof value["organizationUnitId"] === 'string' && guidPattern.test(value["organizationUnitId"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]));
}

export function readHostApiKeyResponse(value: unknown): HostApiKeyResponse {
  if (!(isHostApiKeyResponse(value))) {
    throw new Error('client.invalid_host_api_key_response');
  }
  return value;
}

function isHostApiKeyResponse(value: unknown): value is HostApiKeyResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && ((value["expiresAtUtc"] === null) || (typeof value["expiresAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["keyPrefix"] === 'string') && ((value["lastUsedAtUtc"] === null) || (typeof value["lastUsedAtUtc"] === 'string')) && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => typeof item20 === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
}

export function readHostDashboardActivityResponse(value: unknown): HostDashboardActivityResponse {
  if (!(isHostDashboardActivityResponse(value))) {
    throw new Error('client.invalid_host_dashboard_activity_response');
  }
  return value;
}

function isHostDashboardActivityResponse(value: unknown): value is HostDashboardActivityResponse {
  return isRecord(value) && (typeof value["actionKey"] === 'string') && (typeof value["httpMethod"] === 'string') && (typeof value["occurredAtUtc"] === 'string') && (typeof value["requestPath"] === 'string') && (typeof value["succeeded"] === 'boolean');
}

export function readHostDashboardSummaryResponse(value: unknown): HostDashboardSummaryResponse {
  if (!(isHostDashboardSummaryResponse(value))) {
    throw new Error('client.invalid_host_dashboard_summary_response');
  }
  return value;
}

function isHostDashboardSummaryResponse(value: unknown): value is HostDashboardSummaryResponse {
  return isRecord(value) && (typeof value["activeTenantCount"] === 'number' && Number.isInteger(value["activeTenantCount"])) && (typeof value["onlineSessionCount"] === 'number' && Number.isInteger(value["onlineSessionCount"])) && (Array.isArray(value["recentActivities"]) && value["recentActivities"].every(item25 => isHostDashboardActivityResponse(item25))) && ((typeof value["todayErrorRate"] === 'number' && Number.isFinite(value["todayErrorRate"])) || (typeof value["todayErrorRate"] === 'string')) && (typeof value["todayRequestCount"] === 'number' && Number.isInteger(value["todayRequestCount"]));
}

export function readHostDocumentCategoryResponse(value: unknown): HostDocumentCategoryResponse {
  if (!(isHostDocumentCategoryResponse(value))) {
    throw new Error('client.invalid_host_document_category_response');
  }
  return value;
}

function isHostDocumentCategoryResponse(value: unknown): value is HostDocumentCategoryResponse {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isInteger(value["sortOrder"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostDocumentItemResponse(value: unknown): HostDocumentItemResponse {
  if (!(isHostDocumentItemResponse(value))) {
    throw new Error('client.invalid_host_document_item_response');
  }
  return value;
}

function isHostDocumentItemResponse(value: unknown): value is HostDocumentItemResponse {
  return isRecord(value) && (typeof value["accessCount"] === 'number' && Number.isInteger(value["accessCount"])) && ((value["categoryColor"] === null) || (typeof value["categoryColor"] === 'string')) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["categoryName"] === null) || (typeof value["categoryName"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["currentVersion"] === null) || (isHostDocumentVersionResponse(value["currentVersion"]))) && ((value["deletedAtUtc"] === null) || (typeof value["deletedAtUtc"] === 'string')) && ((value["deletedByUserId"] === null) || (typeof value["deletedByUserId"] === 'string' && guidPattern.test(value["deletedByUserId"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["documentNo"] === 'string') && (isHostDocumentType(value["documentType"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["lastAccessTime"] === null) || (typeof value["lastAccessTime"] === 'string')) && (typeof value["sizeKb"] === 'number' && Number.isInteger(value["sizeKb"])) && (typeof value["sort"] === 'number' && Number.isInteger(value["sort"])) && (isHostDocumentStatus(value["status"])) && (Array.isArray(value["tags"]) && value["tags"].every(item13 => isHostDocumentTagAssignmentResponse(item13))) && ((value["thumbnail"] === null) || (typeof value["thumbnail"] === 'string')) && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostDocumentPermissionEntry(value: unknown): HostDocumentPermissionEntry {
  if (!(isHostDocumentPermissionEntry(value))) {
    throw new Error('client.invalid_host_document_permission_entry');
  }
  return value;
}

function isHostDocumentPermissionEntry(value: unknown): value is HostDocumentPermissionEntry {
  return isRecord(value) && (typeof value["permissionLevel"] === 'string') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"]));
}

export function readHostDocumentPermissionResponse(value: unknown): HostDocumentPermissionResponse {
  if (!(isHostDocumentPermissionResponse(value))) {
    throw new Error('client.invalid_host_document_permission_response');
  }
  return value;
}

function isHostDocumentPermissionResponse(value: unknown): value is HostDocumentPermissionResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["permissionLevel"] === 'string') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"]));
}

export function readHostDocumentShareAccessResponse(value: unknown): HostDocumentShareAccessResponse {
  if (!(isHostDocumentShareAccessResponse(value))) {
    throw new Error('client.invalid_host_document_share_access_response');
  }
  return value;
}

function isHostDocumentShareAccessResponse(value: unknown): value is HostDocumentShareAccessResponse {
  return isRecord(value) && (typeof value["accessCountRemaining"] === 'number' && Number.isInteger(value["accessCountRemaining"])) && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && ((value["fileName"] === null) || (typeof value["fileName"] === 'string')) && (typeof value["fileSizeBytes"] === 'number' && Number.isInteger(value["fileSizeBytes"])) && (typeof value["hasPassword"] === 'boolean') && ((value["mimeType"] === null) || (typeof value["mimeType"] === 'string')) && (typeof value["shareCode"] === 'string') && (typeof value["shareId"] === 'string' && guidPattern.test(value["shareId"])) && (typeof value["title"] === 'string');
}

export function readHostDocumentShareResponse(value: unknown): HostDocumentShareResponse {
  if (!(isHostDocumentShareResponse(value))) {
    throw new Error('client.invalid_host_document_share_response');
  }
  return value;
}

function isHostDocumentShareResponse(value: unknown): value is HostDocumentShareResponse {
  return isRecord(value) && (typeof value["accessCount"] === 'number' && Number.isInteger(value["accessCount"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && (typeof value["expireTime"] === 'string') && (typeof value["hasPassword"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["maxAccessCount"] === null) || (typeof value["maxAccessCount"] === 'number' && Number.isInteger(value["maxAccessCount"]))) && (typeof value["shareCode"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostDocumentStatisticsCategoryItem(value: unknown): HostDocumentStatisticsCategoryItem {
  if (!(isHostDocumentStatisticsCategoryItem(value))) {
    throw new Error('client.invalid_host_document_statistics_category_item');
  }
  return value;
}

function isHostDocumentStatisticsCategoryItem(value: unknown): value is HostDocumentStatisticsCategoryItem {
  return isRecord(value) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["categoryName"] === null) || (typeof value["categoryName"] === 'string')) && (typeof value["count"] === 'number' && Number.isInteger(value["count"]));
}

export function readHostDocumentStatisticsResponse(value: unknown): HostDocumentStatisticsResponse {
  if (!(isHostDocumentStatisticsResponse(value))) {
    throw new Error('client.invalid_host_document_statistics_response');
  }
  return value;
}

function isHostDocumentStatisticsResponse(value: unknown): value is HostDocumentStatisticsResponse {
  return isRecord(value) && (Array.isArray(value["byCategory"]) && value["byCategory"].every(item19 => isHostDocumentStatisticsCategoryItem(item19))) && (Array.isArray(value["byType"]) && value["byType"].every(item15 => isHostDocumentStatisticsTypeItem(item15))) && (typeof value["recycleBinCount"] === 'number' && Number.isInteger(value["recycleBinCount"])) && (typeof value["shareCount"] === 'number' && Number.isInteger(value["shareCount"])) && (isHostDocumentStatisticsSummaryResponse(value["summary"])) && (typeof value["todayAccessCount"] === 'number' && Number.isInteger(value["todayAccessCount"])) && (typeof value["todayCreatedCount"] === 'number' && Number.isInteger(value["todayCreatedCount"])) && (typeof value["todayDownloadCount"] === 'number' && Number.isInteger(value["todayDownloadCount"]));
}

export function readHostDocumentStatisticsSummaryResponse(value: unknown): HostDocumentStatisticsSummaryResponse {
  if (!(isHostDocumentStatisticsSummaryResponse(value))) {
    throw new Error('client.invalid_host_document_statistics_summary_response');
  }
  return value;
}

function isHostDocumentStatisticsSummaryResponse(value: unknown): value is HostDocumentStatisticsSummaryResponse {
  return isRecord(value) && (typeof value["totalItems"] === 'number' && Number.isInteger(value["totalItems"])) && (typeof value["totalSizeInfo"] === 'string') && (typeof value["totalSizeKb"] === 'number' && Number.isInteger(value["totalSizeKb"])) && (typeof value["totalVersions"] === 'number' && Number.isInteger(value["totalVersions"]));
}

export function readHostDocumentStatisticsTypeItem(value: unknown): HostDocumentStatisticsTypeItem {
  if (!(isHostDocumentStatisticsTypeItem(value))) {
    throw new Error('client.invalid_host_document_statistics_type_item');
  }
  return value;
}

function isHostDocumentStatisticsTypeItem(value: unknown): value is HostDocumentStatisticsTypeItem {
  return isRecord(value) && (typeof value["count"] === 'number' && Number.isInteger(value["count"])) && ((value["extension"] === null) || (typeof value["extension"] === 'string')) && (typeof value["totalSizeKb"] === 'number' && Number.isInteger(value["totalSizeKb"]));
}

export function readHostDocumentStatus(value: unknown): HostDocumentStatus {
  if (!(isHostDocumentStatus(value))) {
    throw new Error('client.invalid_host_document_status');
  }
  return value;
}

function isHostDocumentStatus(value: unknown): value is HostDocumentStatus {
  return typeof value === 'number' && Number.isInteger(value);
}

export function readHostDocumentTagAssignmentResponse(value: unknown): HostDocumentTagAssignmentResponse {
  if (!(isHostDocumentTagAssignmentResponse(value))) {
    throw new Error('client.invalid_host_document_tag_assignment_response');
  }
  return value;
}

function isHostDocumentTagAssignmentResponse(value: unknown): value is HostDocumentTagAssignmentResponse {
  return isRecord(value) && (typeof value["tagId"] === 'string' && guidPattern.test(value["tagId"])) && (typeof value["tagName"] === 'string');
}

export function readHostDocumentTagResponse(value: unknown): HostDocumentTagResponse {
  if (!(isHostDocumentTagResponse(value))) {
    throw new Error('client.invalid_host_document_tag_response');
  }
  return value;
}

function isHostDocumentTagResponse(value: unknown): value is HostDocumentTagResponse {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["useCount"] === 'number' && Number.isInteger(value["useCount"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostDocumentType(value: unknown): HostDocumentType {
  if (!(isHostDocumentType(value))) {
    throw new Error('client.invalid_host_document_type');
  }
  return value;
}

function isHostDocumentType(value: unknown): value is HostDocumentType {
  return typeof value === 'number' && Number.isInteger(value);
}

export function readHostDocumentVersionResponse(value: unknown): HostDocumentVersionResponse {
  if (!(isHostDocumentVersionResponse(value))) {
    throw new Error('client.invalid_host_document_version_response');
  }
  return value;
}

function isHostDocumentVersionResponse(value: unknown): value is HostDocumentVersionResponse {
  return isRecord(value) && ((value["changeDescription"] === null) || (typeof value["changeDescription"] === 'string')) && ((value["contentHash"] === null) || (typeof value["contentHash"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["fileId"] === 'string' && guidPattern.test(value["fileId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["sizeBytes"] === 'number' && Number.isInteger(value["sizeBytes"])) && (typeof value["uploadedByUserId"] === 'string' && guidPattern.test(value["uploadedByUserId"])) && (typeof value["versionNumber"] === 'number' && Number.isInteger(value["versionNumber"]));
}

export function readHostFileResponse(value: unknown): HostFileResponse {
  if (!(isHostFileResponse(value))) {
    throw new Error('client.invalid_host_file_response');
  }
  return value;
}

function isHostFileResponse(value: unknown): value is HostFileResponse {
  return isRecord(value) && ((value["contentHash"] === null) || (typeof value["contentHash"] === 'string')) && (typeof value["contentType"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["originalFileName"] === 'string') && (typeof value["sizeBytes"] === 'number' && Number.isInteger(value["sizeBytes"]));
}

export function readHostJobDefinitionResponse(value: unknown): HostJobDefinitionResponse {
  if (!(isHostJobDefinitionResponse(value))) {
    throw new Error('client.invalid_host_job_definition_response');
  }
  return value;
}

function isHostJobDefinitionResponse(value: unknown): value is HostJobDefinitionResponse {
  return isRecord(value) && (typeof value["allowConcurrentExecutions"] === 'boolean') && ((value["args"] === null) || (isHttpJobArgs(value["args"]))) && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["handlerKind"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["jobKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostJobExecutionResponse(value: unknown): HostJobExecutionResponse {
  if (!(isHostJobExecutionResponse(value))) {
    throw new Error('client.invalid_host_job_execution_response');
  }
  return value;
}

function isHostJobExecutionResponse(value: unknown): value is HostJobExecutionResponse {
  return isRecord(value) && (typeof value["attemptCount"] === 'number' && Number.isInteger(value["attemptCount"])) && (typeof value["createdAtUtc"] === 'string') && ((value["errorMessage"] === null) || (typeof value["errorMessage"] === 'string')) && ((value["finishedAtUtc"] === null) || (typeof value["finishedAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["jobDefinitionId"] === 'string' && guidPattern.test(value["jobDefinitionId"])) && ((value["jobScheduleId"] === null) || (typeof value["jobScheduleId"] === 'string' && guidPattern.test(value["jobScheduleId"]))) && ((value["nextAttemptAtUtc"] === null) || (typeof value["nextAttemptAtUtc"] === 'string')) && ((value["scheduledForUtc"] === null) || (typeof value["scheduledForUtc"] === 'string')) && ((value["startedAtUtc"] === null) || (typeof value["startedAtUtc"] === 'string')) && (typeof value["status"] === 'string') && (typeof value["triggerKind"] === 'string');
}

export function readHostJobGroupResponse(value: unknown): HostJobGroupResponse {
  if (!(isHostJobGroupResponse(value))) {
    throw new Error('client.invalid_host_job_group_response');
  }
  return value;
}

function isHostJobGroupResponse(value: unknown): value is HostJobGroupResponse {
  return isRecord(value) && (typeof value["groupName"] === 'string');
}

export function readHostJobHealthBacklogSnapshot(value: unknown): HostJobHealthBacklogSnapshot {
  if (!(isHostJobHealthBacklogSnapshot(value))) {
    throw new Error('client.invalid_host_job_health_backlog_snapshot');
  }
  return value;
}

function isHostJobHealthBacklogSnapshot(value: unknown): value is HostJobHealthBacklogSnapshot {
  return isRecord(value) && (typeof value["dueRetryCount"] === 'number' && Number.isInteger(value["dueRetryCount"])) && ((value["oldestClaimableCreatedAtUtc"] === null) || (typeof value["oldestClaimableCreatedAtUtc"] === 'string')) && ((value["oldestDueRetryAtUtc"] === null) || (typeof value["oldestDueRetryAtUtc"] === 'string')) && (typeof value["pendingCount"] === 'number' && Number.isInteger(value["pendingCount"]));
}

export function readHostJobHealthResponse(value: unknown): HostJobHealthResponse {
  if (!(isHostJobHealthResponse(value))) {
    throw new Error('client.invalid_host_job_health_response');
  }
  return value;
}

function isHostJobHealthResponse(value: unknown): value is HostJobHealthResponse {
  return isRecord(value) && (isHostJobHealthBacklogSnapshot(value["backlog"])) && (Array.isArray(value["registeredHandlers"]) && value["registeredHandlers"].every(item27 => typeof item27 === 'string')) && (Array.isArray(value["workers"]) && value["workers"].every(item16 => isHostJobWorkerInstanceResponse(item16)));
}

export function readHostJobScheduleCronPreviewResponse(value: unknown): HostJobScheduleCronPreviewResponse {
  if (!(isHostJobScheduleCronPreviewResponse(value))) {
    throw new Error('client.invalid_host_job_schedule_cron_preview_response');
  }
  return value;
}

function isHostJobScheduleCronPreviewResponse(value: unknown): value is HostJobScheduleCronPreviewResponse {
  return isRecord(value) && (typeof value["humanDescription"] === 'string') && (typeof value["nextExecutionAtUtc"] === 'string') && (Array.isArray(value["nextOccurrencesUtc"]) && value["nextOccurrencesUtc"].every(item27 => typeof item27 === 'string'));
}

export function readHostJobScheduleDefinitionOptionResponse(value: unknown): HostJobScheduleDefinitionOptionResponse {
  if (!(isHostJobScheduleDefinitionOptionResponse(value))) {
    throw new Error('client.invalid_host_job_schedule_definition_option_response');
  }
  return value;
}

function isHostJobScheduleDefinitionOptionResponse(value: unknown): value is HostJobScheduleDefinitionOptionResponse {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["handlerKind"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["jobKey"] === 'string');
}

export function readHostJobScheduleResponse(value: unknown): HostJobScheduleResponse {
  if (!(isHostJobScheduleResponse(value))) {
    throw new Error('client.invalid_host_job_schedule_response');
  }
  return value;
}

function isHostJobScheduleResponse(value: unknown): value is HostJobScheduleResponse {
  return isRecord(value) && ((value["args"] === null) || (typeof value["args"] === 'string')) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["cronExpression"] === null) || (typeof value["cronExpression"] === 'string')) && ((value["endTime"] === null) || (typeof value["endTime"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["jobDefinitionDisplayName"] === 'string') && (typeof value["jobDefinitionId"] === 'string' && guidPattern.test(value["jobDefinitionId"])) && (typeof value["jobDefinitionJobKey"] === 'string') && ((value["lastExecutionAtUtc"] === null) || (typeof value["lastExecutionAtUtc"] === 'string')) && (typeof value["misfirePolicy"] === 'string') && ((value["nextExecutionAtUtc"] === null) || (typeof value["nextExecutionAtUtc"] === 'string')) && (typeof value["numberOfErrors"] === 'number' && Number.isInteger(value["numberOfErrors"])) && (typeof value["numberOfRuns"] === 'number' && Number.isInteger(value["numberOfRuns"])) && ((value["oneTimeAtUtc"] === null) || (typeof value["oneTimeAtUtc"] === 'string')) && ((value["startTime"] === null) || (typeof value["startTime"] === 'string')) && (typeof value["timeZoneId"] === 'string') && (typeof value["triggerKind"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostJobWorkerInstanceResponse(value: unknown): HostJobWorkerInstanceResponse {
  if (!(isHostJobWorkerInstanceResponse(value))) {
    throw new Error('client.invalid_host_job_worker_instance_response');
  }
  return value;
}

function isHostJobWorkerInstanceResponse(value: unknown): value is HostJobWorkerInstanceResponse {
  return isRecord(value) && (typeof value["hostProfile"] === 'string') && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["isStale"] === 'boolean') && (typeof value["lastHeartbeatAtUtc"] === 'string') && (typeof value["startedAtUtc"] === 'string') && ((value["workerVersion"] === null) || (typeof value["workerVersion"] === 'string'));
}

export function readHostMenuPermissionOptionResponse(value: unknown): HostMenuPermissionOptionResponse {
  if (!(isHostMenuPermissionOptionResponse(value))) {
    throw new Error('client.invalid_host_menu_permission_option_response');
  }
  return value;
}

function isHostMenuPermissionOptionResponse(value: unknown): value is HostMenuPermissionOptionResponse {
  return isRecord(value) && (value["actionId"] === undefined || ((value["actionId"] === null) || (typeof value["actionId"] === 'string'))) && (value["actionKey"] === undefined || ((value["actionKey"] === null) || (typeof value["actionKey"] === 'string'))) && (typeof value["code"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["displayNameKey"] === 'string') && (typeof value["kind"] === 'string') && (typeof value["moduleKey"] === 'string') && (typeof value["moduleTitle"] === 'string') && (typeof value["pageId"] === 'string') && (typeof value["pageTitle"] === 'string');
}

export function readHostMenuResponse(value: unknown): HostMenuResponse {
  if (!(isHostMenuResponse(value))) {
    throw new Error('client.invalid_host_menu_response');
  }
  return value;
}

function isHostMenuResponse(value: unknown): value is HostMenuResponse {
  return isRecord(value) && (typeof value["caption"] === 'string') && (typeof value["componentKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["icon"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isAffix"] === 'boolean') && (typeof value["isEmbedded"] === 'boolean') && (typeof value["isHidden"] === 'boolean') && (typeof value["isKeepAlive"] === 'boolean') && (typeof value["isSystem"] === 'boolean') && ((value["linkUrl"] === null) || (typeof value["linkUrl"] === 'string')) && (typeof value["menuType"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["path"] === 'string') && ((value["redirect"] === null) || (typeof value["redirect"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && (typeof value["requiredPermission"] === 'string') && (typeof value["routeName"] === 'string') && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostNavigationCatalogSyncResponse(value: unknown): HostNavigationCatalogSyncResponse {
  if (!(isHostNavigationCatalogSyncResponse(value))) {
    throw new Error('client.invalid_host_navigation_catalog_sync_response');
  }
  return value;
}

function isHostNavigationCatalogSyncResponse(value: unknown): value is HostNavigationCatalogSyncResponse {
  return isRecord(value) && (typeof value["created"] === 'number' && Number.isInteger(value["created"])) && (typeof value["reparented"] === 'number' && Number.isInteger(value["reparented"])) && (typeof value["skipped"] === 'number' && Number.isInteger(value["skipped"]));
}

export function readHostOnlineSessionResponse(value: unknown): HostOnlineSessionResponse {
  if (!(isHostOnlineSessionResponse(value))) {
    throw new Error('client.invalid_host_online_session_response');
  }
  return value;
}

function isHostOnlineSessionResponse(value: unknown): value is HostOnlineSessionResponse {
  return isRecord(value) && ((value["activeTenantId"] === null) || (typeof value["activeTenantId"] === 'string' && guidPattern.test(value["activeTenantId"]))) && (typeof value["clientId"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["expiresAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
}

export function readHostRoleDataScopeResponse(value: unknown): HostRoleDataScopeResponse {
  if (!(isHostRoleDataScopeResponse(value))) {
    throw new Error('client.invalid_host_role_data_scope_response');
  }
  return value;
}

function isHostRoleDataScopeResponse(value: unknown): value is HostRoleDataScopeResponse {
  return isRecord(value) && (typeof value["dataScopeKind"] === 'string') && (typeof value["roleId"] === 'string' && guidPattern.test(value["roleId"])) && (Array.isArray(value["unitIds"]) && value["unitIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostRoleFieldGrantsResponse(value: unknown): HostRoleFieldGrantsResponse {
  if (!(isHostRoleFieldGrantsResponse(value))) {
    throw new Error('client.invalid_host_role_field_grants_response');
  }
  return value;
}

function isHostRoleFieldGrantsResponse(value: unknown): value is HostRoleFieldGrantsResponse {
  return isRecord(value) && (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string')) && (typeof value["resourceKey"] === 'string') && (typeof value["roleId"] === 'string' && guidPattern.test(value["roleId"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostRoleResponse(value: unknown): HostRoleResponse {
  if (!(isHostRoleResponse(value))) {
    throw new Error('client.invalid_host_role_response');
  }
  return value;
}

function isHostRoleResponse(value: unknown): value is HostRoleResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isSuperAdministrator"] === 'boolean') && (typeof value["isSystem"] === 'boolean') && (typeof value["name"] === 'string') && (Array.isArray(value["permissionCodes"]) && value["permissionCodes"].every(item24 => typeof item24 === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostUserManagementOrganizationReferenceResponse(value: unknown): HostUserManagementOrganizationReferenceResponse {
  if (!(isHostUserManagementOrganizationReferenceResponse(value))) {
    throw new Error('client.invalid_host_user_management_organization_reference_response');
  }
  return value;
}

function isHostUserManagementOrganizationReferenceResponse(value: unknown): value is HostUserManagementOrganizationReferenceResponse {
  return isRecord(value) && (Array.isArray(value["positions"]) && value["positions"].every(item18 => isOrganizationPositionResponse(item18))) && (Array.isArray(value["units"]) && value["units"].every(item14 => isOrganizationUnitResponse(item14))) && (Array.isArray(value["userPositions"]) && value["userPositions"].every(item22 => isOrganizationUserPositionResponse(item22))) && (Array.isArray(value["userUnits"]) && value["userUnits"].every(item18 => isOrganizationUserUnitResponse(item18)));
}

export function readHostUserProfileResponse(value: unknown): HostUserProfileResponse {
  if (!(isHostUserProfileResponse(value))) {
    throw new Error('client.invalid_host_user_profile_response');
  }
  return value;
}

function isHostUserProfileResponse(value: unknown): value is HostUserProfileResponse {
  return isRecord(value) && ((value["address"] === null) || (typeof value["address"] === 'string')) && ((value["birthDate"] === null) || (typeof value["birthDate"] === 'string')) && ((value["educationLevel"] === null) || (typeof value["educationLevel"] === 'string')) && ((value["email"] === null) || (typeof value["email"] === 'string')) && ((value["emergencyContact"] === null) || (typeof value["emergencyContact"] === 'string')) && ((value["emergencyContactAddress"] === null) || (typeof value["emergencyContactAddress"] === 'string')) && ((value["emergencyContactPhone"] === null) || (typeof value["emergencyContactPhone"] === 'string')) && ((value["emergencyContactRelation"] === null) || (typeof value["emergencyContactRelation"] === 'string')) && ((value["employeeNumber"] === null) || (typeof value["employeeNumber"] === 'string')) && ((value["ethnicity"] === null) || (typeof value["ethnicity"] === 'string')) && ((value["gender"] === null) || (typeof value["gender"] === 'string')) && ((value["graduatedSchool"] === null) || (typeof value["graduatedSchool"] === 'string')) && ((value["idCardNumber"] === null) || (typeof value["idCardNumber"] === 'string')) && ((value["idCardType"] === null) || (typeof value["idCardType"] === 'string')) && ((value["joinDateUtc"] === null) || (typeof value["joinDateUtc"] === 'string')) && ((value["nickname"] === null) || (typeof value["nickname"] === 'string')) && ((value["officePhone"] === null) || (typeof value["officePhone"] === 'string')) && ((value["phoneNumber"] === null) || (typeof value["phoneNumber"] === 'string')) && ((value["politicalStatus"] === null) || (typeof value["politicalStatus"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["sortOrder"] === null) || (typeof value["sortOrder"] === 'number' && Number.isInteger(value["sortOrder"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostUserProfileWriteRequest(value: unknown): HostUserProfileWriteRequest {
  if (!(isHostUserProfileWriteRequest(value))) {
    throw new Error('client.invalid_host_user_profile_write_request');
  }
  return value;
}

function isHostUserProfileWriteRequest(value: unknown): value is HostUserProfileWriteRequest {
  return isRecord(value) && ((value["address"] === null) || (typeof value["address"] === 'string')) && ((value["birthDate"] === null) || (typeof value["birthDate"] === 'string')) && ((value["educationLevel"] === null) || (typeof value["educationLevel"] === 'string')) && ((value["email"] === null) || (typeof value["email"] === 'string')) && ((value["emergencyContact"] === null) || (typeof value["emergencyContact"] === 'string')) && ((value["emergencyContactAddress"] === null) || (typeof value["emergencyContactAddress"] === 'string')) && ((value["emergencyContactPhone"] === null) || (typeof value["emergencyContactPhone"] === 'string')) && ((value["emergencyContactRelation"] === null) || (typeof value["emergencyContactRelation"] === 'string')) && ((value["employeeNumber"] === null) || (typeof value["employeeNumber"] === 'string')) && ((value["ethnicity"] === null) || (typeof value["ethnicity"] === 'string')) && ((value["fieldKeys"] === null) || (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string'))) && ((value["gender"] === null) || (typeof value["gender"] === 'string')) && ((value["graduatedSchool"] === null) || (typeof value["graduatedSchool"] === 'string')) && ((value["idCardNumber"] === null) || (typeof value["idCardNumber"] === 'string')) && ((value["idCardType"] === null) || (typeof value["idCardType"] === 'string')) && ((value["joinDateUtc"] === null) || (typeof value["joinDateUtc"] === 'string')) && ((value["nickname"] === null) || (typeof value["nickname"] === 'string')) && ((value["officePhone"] === null) || (typeof value["officePhone"] === 'string')) && ((value["phoneNumber"] === null) || (typeof value["phoneNumber"] === 'string')) && ((value["politicalStatus"] === null) || (typeof value["politicalStatus"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["sortOrder"] === null) || (typeof value["sortOrder"] === 'number' && Number.isInteger(value["sortOrder"]))) && ((value["version"] === null) || (typeof value["version"] === 'number' && Number.isInteger(value["version"])));
}

export function readHostUserProjectedFieldsResponse(value: unknown): HostUserProjectedFieldsResponse {
  if (!(isHostUserProjectedFieldsResponse(value))) {
    throw new Error('client.invalid_host_user_projected_fields_response');
  }
  return value;
}

function isHostUserProjectedFieldsResponse(value: unknown): value is HostUserProjectedFieldsResponse {
  return isRecord(value) && (Array.isArray(value["effectiveFieldKeys"]) && value["effectiveFieldKeys"].every(item27 => typeof item27 === 'string')) && ((value["failedLoginCount"] === null) || (typeof value["failedLoginCount"] === 'number' && Number.isInteger(value["failedLoginCount"]))) && ((value["lockoutEndUtc"] === null) || (typeof value["lockoutEndUtc"] === 'string')) && ((value["preferredLocale"] === null) || (typeof value["preferredLocale"] === 'string'));
}

export function readHostUserResponse(value: unknown): HostUserResponse {
  if (!(isHostUserResponse(value))) {
    throw new Error('client.invalid_host_user_response');
  }
  return value;
}

function isHostUserResponse(value: unknown): value is HostUserResponse {
  return isRecord(value) && (typeof value["accountType"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileResponse(value["profile"])))) && (value["projectedFields"] === undefined || ((value["projectedFields"] === null) || (isHostUserProjectedFieldsResponse(value["projectedFields"])))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHostUserRolesResponse(value: unknown): HostUserRolesResponse {
  if (!(isHostUserRolesResponse(value))) {
    throw new Error('client.invalid_host_user_roles_response');
  }
  return value;
}

function isHostUserRolesResponse(value: unknown): value is HostUserRolesResponse {
  return isRecord(value) && (Array.isArray(value["roleIds"]) && value["roleIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readHttpJobArgs(value: unknown): HttpJobArgs {
  if (!(isHttpJobArgs(value))) {
    throw new Error('client.invalid_http_job_args');
  }
  return value;
}

function isHttpJobArgs(value: unknown): value is HttpJobArgs {
  return isRecord(value) && (value["headers"] === undefined || ((value["headers"] === null) || (isRecord(value["headers"])))) && (typeof value["method"] === 'string') && (value["secretHeaders"] === undefined || ((value["secretHeaders"] === null) || (isRecord(value["secretHeaders"])))) && (value["successStatusCodes"] === undefined || ((value["successStatusCodes"] === null) || (Array.isArray(value["successStatusCodes"]) && value["successStatusCodes"].every(item27 => typeof item27 === 'number' && Number.isInteger(item27))))) && (value["timeoutSeconds"] === undefined || ((value["timeoutSeconds"] === null) || (typeof value["timeoutSeconds"] === 'number' && Number.isInteger(value["timeoutSeconds"])))) && (typeof value["url"] === 'string');
}

export function readHttpJobSecretHeaderRef(value: unknown): HttpJobSecretHeaderRef {
  if (!(isHttpJobSecretHeaderRef(value))) {
    throw new Error('client.invalid_http_job_secret_header_ref');
  }
  return value;
}

function isHttpJobSecretHeaderRef(value: unknown): value is HttpJobSecretHeaderRef {
  return isRecord(value) && (typeof value["configKey"] === 'string');
}

export function readIFormFile(value: unknown): IFormFile {
  if (!(isIFormFile(value))) {
    throw new Error('client.invalid_iform_file');
  }
  return value;
}

function isIFormFile(value: unknown): value is IFormFile {
  return value instanceof Blob;
}

export function readImportHostUserRowResult(value: unknown): ImportHostUserRowResult {
  if (!(isImportHostUserRowResult(value))) {
    throw new Error('client.invalid_import_host_user_row_result');
  }
  return value;
}

function isImportHostUserRowResult(value: unknown): value is ImportHostUserRowResult {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["line"] === 'number' && Number.isInteger(value["line"])) && ((value["message"] === null) || (typeof value["message"] === 'string')) && (typeof value["succeeded"] === 'boolean') && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readImportHostUsersRequest(value: unknown): ImportHostUsersRequest {
  if (!(isImportHostUsersRequest(value))) {
    throw new Error('client.invalid_import_host_users_request');
  }
  return value;
}

function isImportHostUsersRequest(value: unknown): value is ImportHostUsersRequest {
  return isRecord(value) && (Array.isArray(value["rows"]) && value["rows"].every(item13 => isCreateHostUserRequest(item13)));
}

export function readImportHostUsersResponse(value: unknown): ImportHostUsersResponse {
  if (!(isImportHostUsersResponse(value))) {
    throw new Error('client.invalid_import_host_users_response');
  }
  return value;
}

function isImportHostUsersResponse(value: unknown): value is ImportHostUsersResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isImportHostUserRowResult(item16))) && (typeof value["succeededCount"] === 'number' && Number.isInteger(value["succeededCount"]));
}

export function readInboxMessageResponse(value: unknown): InboxMessageResponse {
  if (!(isInboxMessageResponse(value))) {
    throw new Error('client.invalid_inbox_message_response');
  }
  return value;
}

function isInboxMessageResponse(value: unknown): value is InboxMessageResponse {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && ((value["createdByUserId"] === null) || (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"]))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && (typeof value["status"] === 'string') && (typeof value["title"] === 'string');
}

export function readInboxUnreadCountResponse(value: unknown): InboxUnreadCountResponse {
  if (!(isInboxUnreadCountResponse(value))) {
    throw new Error('client.invalid_inbox_unread_count_response');
  }
  return value;
}

function isInboxUnreadCountResponse(value: unknown): value is InboxUnreadCountResponse {
  return isRecord(value) && (typeof value["unreadCount"] === 'number' && Number.isInteger(value["unreadCount"]));
}

export function readJsonElement(value: unknown): JsonElement {
  if (!(isJsonElement(value))) {
    throw new Error('client.invalid_json_element');
  }
  return value;
}

function isJsonElement(value: unknown): value is JsonElement {
  return isJsonValue(value);
}

export function readLocalePreferenceResponse(value: unknown): LocalePreferenceResponse {
  if (!(isLocalePreferenceResponse(value))) {
    throw new Error('client.invalid_locale_preference_response');
  }
  return value;
}

function isLocalePreferenceResponse(value: unknown): value is LocalePreferenceResponse {
  return isRecord(value) && (typeof value["preferredLocale"] === 'string') && (typeof value["profileVersion"] === 'number' && Number.isInteger(value["profileVersion"]));
}

export function readLogFileSummary(value: unknown): LogFileSummary {
  if (!(isLogFileSummary(value))) {
    throw new Error('client.invalid_log_file_summary');
  }
  return value;
}

function isLogFileSummary(value: unknown): value is LogFileSummary {
  return isRecord(value) && (typeof value["fileName"] === 'string') && (typeof value["id"] === 'string') && (typeof value["lastModifiedUtc"] === 'string') && (typeof value["sizeBytes"] === 'number' && Number.isInteger(value["sizeBytes"]));
}

export function readLogFileTail(value: unknown): LogFileTail {
  if (!(isLogFileTail(value))) {
    throw new Error('client.invalid_log_file_tail');
  }
  return value;
}

function isLogFileTail(value: unknown): value is LogFileTail {
  return isRecord(value) && (typeof value["bytesRead"] === 'number' && Number.isInteger(value["bytesRead"])) && (typeof value["content"] === 'string') && (typeof value["fileName"] === 'string') && (typeof value["id"] === 'string') && (typeof value["isTruncated"] === 'boolean');
}

export function readLoginRequest(value: unknown): LoginRequest {
  if (!(isLoginRequest(value))) {
    throw new Error('client.invalid_login_request');
  }
  return value;
}

function isLoginRequest(value: unknown): value is LoginRequest {
  return isRecord(value) && (typeof value["password"] === 'string') && (typeof value["username"] === 'string');
}

export function readModuleCatalogEntryResponse(value: unknown): ModuleCatalogEntryResponse {
  if (!(isModuleCatalogEntryResponse(value))) {
    throw new Error('client.invalid_module_catalog_entry_response');
  }
  return value;
}

function isModuleCatalogEntryResponse(value: unknown): value is ModuleCatalogEntryResponse {
  return isRecord(value) && (Array.isArray(value["dependencies"]) && value["dependencies"].every(item21 => typeof item21 === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["healthCapability"] === 'string') && (Array.isArray(value["hostProfiles"]) && value["hostProfiles"].every(item21 => typeof item21 === 'string')) && (typeof value["moduleKey"] === 'string') && (typeof value["sourceClassification"] === 'string') && (typeof value["version"] === 'string');
}

export function readNotificationBindingResponse(value: unknown): NotificationBindingResponse {
  if (!(isNotificationBindingResponse(value))) {
    throw new Error('client.invalid_notification_binding_response');
  }
  return value;
}

function isNotificationBindingResponse(value: unknown): value is NotificationBindingResponse {
  return isRecord(value) && (typeof value["bindingKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["draftDispatchModeKey"] === 'string') && (typeof value["draftJson"] === 'string') && (typeof value["draftRevision"] === 'number' && Number.isInteger(value["draftRevision"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestBindingTargetsJson"] === null) || (typeof value["latestBindingTargetsJson"] === 'string')) && ((value["latestChannelKey"] === null) || (typeof value["latestChannelKey"] === 'string')) && ((value["latestDispatchModeKey"] === null) || (typeof value["latestDispatchModeKey"] === 'string')) && ((value["latestProducerKey"] === null) || (typeof value["latestProducerKey"] === 'string')) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["latestPublishedVersionNumber"] === null) || (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isInteger(value["latestPublishedVersionNumber"]))) && ((value["latestSceneKey"] === null) || (typeof value["latestSceneKey"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readNotificationBindingTargetInput(value: unknown): NotificationBindingTargetInput {
  if (!(isNotificationBindingTargetInput(value))) {
    throw new Error('client.invalid_notification_binding_target_input');
  }
  return value;
}

function isNotificationBindingTargetInput(value: unknown): value is NotificationBindingTargetInput {
  return isRecord(value) && (typeof value["order"] === 'number' && Number.isInteger(value["order"])) && (typeof value["profileKey"] === 'string');
}

export function readNotificationDeliveryAttemptResponse(value: unknown): NotificationDeliveryAttemptResponse {
  if (!(isNotificationDeliveryAttemptResponse(value))) {
    throw new Error('client.invalid_notification_delivery_attempt_response');
  }
  return value;
}

function isNotificationDeliveryAttemptResponse(value: unknown): value is NotificationDeliveryAttemptResponse {
  return isRecord(value) && (typeof value["attemptNumber"] === 'number' && Number.isInteger(value["attemptNumber"])) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["finishedAtUtc"] === null) || (typeof value["finishedAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["providerMessageId"] === null) || (typeof value["providerMessageId"] === 'string')) && ((value["resultCategoryKey"] === null) || (typeof value["resultCategoryKey"] === 'string')) && (typeof value["startedAtUtc"] === 'string') && (typeof value["statusKey"] === 'string');
}

export function readNotificationDeliveryResponse(value: unknown): NotificationDeliveryResponse {
  if (!(isNotificationDeliveryResponse(value))) {
    throw new Error('client.invalid_notification_delivery_response');
  }
  return value;
}

function isNotificationDeliveryResponse(value: unknown): value is NotificationDeliveryResponse {
  return isRecord(value) && (Array.isArray(value["attempts"]) && value["attempts"].every(item17 => isNotificationDeliveryAttemptResponse(item17))) && ((value["bindingVersionId"] === null) || (typeof value["bindingVersionId"] === 'string' && guidPattern.test(value["bindingVersionId"]))) && (typeof value["channelKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["intentId"] === 'string' && guidPattern.test(value["intentId"])) && ((value["nextAttemptAtUtc"] === null) || (typeof value["nextAttemptAtUtc"] === 'string')) && ((value["providerProfileVersionId"] === null) || (typeof value["providerProfileVersionId"] === 'string' && guidPattern.test(value["providerProfileVersionId"]))) && (typeof value["recipientId"] === 'string' && guidPattern.test(value["recipientId"])) && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string'));
}

export function readNotificationProviderConfigField(value: unknown): NotificationProviderConfigField {
  if (!(isNotificationProviderConfigField(value))) {
    throw new Error('client.invalid_notification_provider_config_field');
  }
  return value;
}

function isNotificationProviderConfigField(value: unknown): value is NotificationProviderConfigField {
  return isRecord(value) && (typeof value["name"] === 'string') && (typeof value["required"] === 'boolean') && (typeof value["typeKey"] === 'string');
}

export function readNotificationProviderProfileResponse(value: unknown): NotificationProviderProfileResponse {
  if (!(isNotificationProviderProfileResponse(value))) {
    throw new Error('client.invalid_notification_provider_profile_response');
  }
  return value;
}

function isNotificationProviderProfileResponse(value: unknown): value is NotificationProviderProfileResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["draftRevision"] === 'number' && Number.isInteger(value["draftRevision"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["latestAdapterVersion"] === null) || (typeof value["latestAdapterVersion"] === 'string')) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["latestPublishedVersionNumber"] === null) || (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isInteger(value["latestPublishedVersionNumber"]))) && (typeof value["nonSecretConfigJson"] === 'string') && (typeof value["profileKey"] === 'string') && (typeof value["providerTypeKey"] === 'string') && (typeof value["secretStatus"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readNotificationProviderTypeDescriptor(value: unknown): NotificationProviderTypeDescriptor {
  if (!(isNotificationProviderTypeDescriptor(value))) {
    throw new Error('client.invalid_notification_provider_type_descriptor');
  }
  return value;
}

function isNotificationProviderTypeDescriptor(value: unknown): value is NotificationProviderTypeDescriptor {
  return isRecord(value) && (typeof value["adapterVersion"] === 'string') && (Array.isArray(value["nonSecretFields"]) && value["nonSecretFields"].every(item24 => isNotificationProviderConfigField(item24))) && (typeof value["providerTypeKey"] === 'string') && (typeof value["receiptModeKey"] === 'string') && (Array.isArray(value["secretFieldKeys"]) && value["secretFieldKeys"].every(item24 => typeof item24 === 'string')) && (Array.isArray(value["supportedChannelKeys"]) && value["supportedChannelKeys"].every(item29 => typeof item29 === 'string')) && (typeof value["supportsNativeAot"] === 'boolean');
}

export function readNotificationTemplateBody(value: unknown): NotificationTemplateBody {
  if (!(isNotificationTemplateBody(value))) {
    throw new Error('client.invalid_notification_template_body');
  }
  return value;
}

function isNotificationTemplateBody(value: unknown): value is NotificationTemplateBody {
  return isRecord(value) && (typeof value["text"] === 'string');
}

export function readNotificationTemplateParameterDefinition(value: unknown): NotificationTemplateParameterDefinition {
  if (!(isNotificationTemplateParameterDefinition(value))) {
    throw new Error('client.invalid_notification_template_parameter_definition');
  }
  return value;
}

function isNotificationTemplateParameterDefinition(value: unknown): value is NotificationTemplateParameterDefinition {
  return isRecord(value) && ((value["maxLength"] === null) || (typeof value["maxLength"] === 'number' && Number.isInteger(value["maxLength"]))) && (typeof value["name"] === 'string') && (typeof value["required"] === 'boolean') && (typeof value["typeKey"] === 'string');
}

export function readNotificationTemplateParameterSchema(value: unknown): NotificationTemplateParameterSchema {
  if (!(isNotificationTemplateParameterSchema(value))) {
    throw new Error('client.invalid_notification_template_parameter_schema');
  }
  return value;
}

function isNotificationTemplateParameterSchema(value: unknown): value is NotificationTemplateParameterSchema {
  return isRecord(value) && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isNotificationTemplateParameterDefinition(item19))) && (typeof value["schemaVersion"] === 'number' && Number.isInteger(value["schemaVersion"]));
}

export function readNotificationTemplateResponse(value: unknown): NotificationTemplateResponse {
  if (!(isNotificationTemplateResponse(value))) {
    throw new Error('client.invalid_notification_template_response');
  }
  return value;
}

function isNotificationTemplateResponse(value: unknown): value is NotificationTemplateResponse {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["contentCategoryKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["draftBodyJson"] === 'string') && (typeof value["draftParameterSchemaJson"] === 'string') && (typeof value["draftRevision"] === 'number' && Number.isInteger(value["draftRevision"])) && (typeof value["draftSubject"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestContentClassificationKey"] === null) || (typeof value["latestContentClassificationKey"] === 'string')) && ((value["latestContentHash"] === null) || (typeof value["latestContentHash"] === 'string')) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["latestPublishedVersionNumber"] === null) || (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isInteger(value["latestPublishedVersionNumber"]))) && (typeof value["templateKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readOperationLogResponse(value: unknown): OperationLogResponse {
  if (!(isOperationLogResponse(value))) {
    throw new Error('client.invalid_operation_log_response');
  }
  return value;
}

function isOperationLogResponse(value: unknown): value is OperationLogResponse {
  return isRecord(value) && (typeof value["actionKey"] === 'string') && ((value["clientIpFingerprint"] === null) || (typeof value["clientIpFingerprint"] === 'string')) && (typeof value["durationMs"] === 'number' && Number.isInteger(value["durationMs"])) && (typeof value["httpMethod"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && ((value["permissionCode"] === null) || (typeof value["permissionCode"] === 'string')) && (typeof value["requestPath"] === 'string') && (typeof value["statusCode"] === 'number' && Number.isInteger(value["statusCode"])) && (typeof value["succeeded"] === 'boolean') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readOrganizationAssignableUserResponse(value: unknown): OrganizationAssignableUserResponse {
  if (!(isOrganizationAssignableUserResponse(value))) {
    throw new Error('client.invalid_organization_assignable_user_response');
  }
  return value;
}

function isOrganizationAssignableUserResponse(value: unknown): value is OrganizationAssignableUserResponse {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["username"] === 'string');
}

export function readOrganizationPositionLevelResponse(value: unknown): OrganizationPositionLevelResponse {
  if (!(isOrganizationPositionLevelResponse(value))) {
    throw new Error('client.invalid_organization_position_level_response');
  }
  return value;
}

function isOrganizationPositionLevelResponse(value: unknown): value is OrganizationPositionLevelResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readOrganizationPositionResponse(value: unknown): OrganizationPositionResponse {
  if (!(isOrganizationPositionResponse(value))) {
    throw new Error('client.invalid_organization_position_response');
  }
  return value;
}

function isOrganizationPositionResponse(value: unknown): value is OrganizationPositionResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["positionLevelCode"] === null) || (typeof value["positionLevelCode"] === 'string')) && ((value["positionLevelId"] === null) || (typeof value["positionLevelId"] === 'string' && guidPattern.test(value["positionLevelId"]))) && ((value["positionLevelName"] === null) || (typeof value["positionLevelName"] === 'string')) && ((value["unitCode"] === null) || (typeof value["unitCode"] === 'string')) && ((value["unitId"] === null) || (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"]))) && ((value["unitName"] === null) || (typeof value["unitName"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readOrganizationUnitResponse(value: unknown): OrganizationUnitResponse {
  if (!(isOrganizationUnitResponse(value))) {
    throw new Error('client.invalid_organization_unit_response');
  }
  return value;
}

function isOrganizationUnitResponse(value: unknown): value is OrganizationUnitResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readOrganizationUserPositionResponse(value: unknown): OrganizationUserPositionResponse {
  if (!(isOrganizationUserPositionResponse(value))) {
    throw new Error('client.invalid_organization_user_position_response');
  }
  return value;
}

function isOrganizationUserPositionResponse(value: unknown): value is OrganizationUserPositionResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isPrimary"] === 'boolean') && (typeof value["positionCode"] === 'string') && (typeof value["positionId"] === 'string' && guidPattern.test(value["positionId"])) && (typeof value["positionName"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readOrganizationUserUnitResponse(value: unknown): OrganizationUserUnitResponse {
  if (!(isOrganizationUserUnitResponse(value))) {
    throw new Error('client.invalid_organization_user_unit_response');
  }
  return value;
}

function isOrganizationUserUnitResponse(value: unknown): value is OrganizationUserUnitResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isPrimary"] === 'boolean') && (typeof value["unitCode"] === 'string') && (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"])) && (typeof value["unitName"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readOutboundCallLogResponse(value: unknown): OutboundCallLogResponse {
  if (!(isOutboundCallLogResponse(value))) {
    throw new Error('client.invalid_outbound_call_log_response');
  }
  return value;
}

function isOutboundCallLogResponse(value: unknown): value is OutboundCallLogResponse {
  return isRecord(value) && (typeof value["destinationHostCategory"] === 'string') && (typeof value["durationMs"] === 'number' && Number.isInteger(value["durationMs"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && (typeof value["operationKey"] === 'string') && (typeof value["providerKey"] === 'string') && (typeof value["retryCount"] === 'number' && Number.isInteger(value["retryCount"])) && ((value["safeErrorCode"] === null) || (typeof value["safeErrorCode"] === 'string')) && (typeof value["statusCode"] === 'number' && Number.isInteger(value["statusCode"])) && (typeof value["succeeded"] === 'boolean') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readPagedResultOfAccessLogResponse(value: unknown): PagedResultOfAccessLogResponse {
  if (!(isPagedResultOfAccessLogResponse(value))) {
    throw new Error('client.invalid_paged_result_of_access_log_response');
  }
  return value;
}

function isPagedResultOfAccessLogResponse(value: unknown): value is PagedResultOfAccessLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAccessLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfCodeGenerationRunResponse(value: unknown): PagedResultOfCodeGenerationRunResponse {
  if (!(isPagedResultOfCodeGenerationRunResponse(value))) {
    throw new Error('client.invalid_paged_result_of_code_generation_run_response');
  }
  return value;
}

function isPagedResultOfCodeGenerationRunResponse(value: unknown): value is PagedResultOfCodeGenerationRunResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isCodeGenerationRunResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfCodeGenerationTemplateResponse(value: unknown): PagedResultOfCodeGenerationTemplateResponse {
  if (!(isPagedResultOfCodeGenerationTemplateResponse(value))) {
    throw new Error('client.invalid_paged_result_of_code_generation_template_response');
  }
  return value;
}

function isPagedResultOfCodeGenerationTemplateResponse(value: unknown): value is PagedResultOfCodeGenerationTemplateResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isCodeGenerationTemplateResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfConfigEntryResponse(value: unknown): PagedResultOfConfigEntryResponse {
  if (!(isPagedResultOfConfigEntryResponse(value))) {
    throw new Error('client.invalid_paged_result_of_config_entry_response');
  }
  return value;
}

function isPagedResultOfConfigEntryResponse(value: unknown): value is PagedResultOfConfigEntryResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isConfigEntryResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfDictItemResponse(value: unknown): PagedResultOfDictItemResponse {
  if (!(isPagedResultOfDictItemResponse(value))) {
    throw new Error('client.invalid_paged_result_of_dict_item_response');
  }
  return value;
}

function isPagedResultOfDictItemResponse(value: unknown): value is PagedResultOfDictItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isDictItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfDictTypeResponse(value: unknown): PagedResultOfDictTypeResponse {
  if (!(isPagedResultOfDictTypeResponse(value))) {
    throw new Error('client.invalid_paged_result_of_dict_type_response');
  }
  return value;
}

function isPagedResultOfDictTypeResponse(value: unknown): value is PagedResultOfDictTypeResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isDictTypeResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfExceptionLogResponse(value: unknown): PagedResultOfExceptionLogResponse {
  if (!(isPagedResultOfExceptionLogResponse(value))) {
    throw new Error('client.invalid_paged_result_of_exception_log_response');
  }
  return value;
}

function isPagedResultOfExceptionLogResponse(value: unknown): value is PagedResultOfExceptionLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isExceptionLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostAnnouncementResponse(value: unknown): PagedResultOfHostAnnouncementResponse {
  if (!(isPagedResultOfHostAnnouncementResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_announcement_response');
  }
  return value;
}

function isPagedResultOfHostAnnouncementResponse(value: unknown): value is PagedResultOfHostAnnouncementResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostAnnouncementResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostApiKeyResponse(value: unknown): PagedResultOfHostApiKeyResponse {
  if (!(isPagedResultOfHostApiKeyResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_api_key_response');
  }
  return value;
}

function isPagedResultOfHostApiKeyResponse(value: unknown): value is PagedResultOfHostApiKeyResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostApiKeyResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostDocumentItemResponse(value: unknown): PagedResultOfHostDocumentItemResponse {
  if (!(isPagedResultOfHostDocumentItemResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_document_item_response');
  }
  return value;
}

function isPagedResultOfHostDocumentItemResponse(value: unknown): value is PagedResultOfHostDocumentItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostDocumentItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostDocumentShareResponse(value: unknown): PagedResultOfHostDocumentShareResponse {
  if (!(isPagedResultOfHostDocumentShareResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_document_share_response');
  }
  return value;
}

function isPagedResultOfHostDocumentShareResponse(value: unknown): value is PagedResultOfHostDocumentShareResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostDocumentShareResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostFileResponse(value: unknown): PagedResultOfHostFileResponse {
  if (!(isPagedResultOfHostFileResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_file_response');
  }
  return value;
}

function isPagedResultOfHostFileResponse(value: unknown): value is PagedResultOfHostFileResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostFileResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostJobDefinitionResponse(value: unknown): PagedResultOfHostJobDefinitionResponse {
  if (!(isPagedResultOfHostJobDefinitionResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_job_definition_response');
  }
  return value;
}

function isPagedResultOfHostJobDefinitionResponse(value: unknown): value is PagedResultOfHostJobDefinitionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostJobDefinitionResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostJobExecutionResponse(value: unknown): PagedResultOfHostJobExecutionResponse {
  if (!(isPagedResultOfHostJobExecutionResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_job_execution_response');
  }
  return value;
}

function isPagedResultOfHostJobExecutionResponse(value: unknown): value is PagedResultOfHostJobExecutionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostJobExecutionResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostJobScheduleResponse(value: unknown): PagedResultOfHostJobScheduleResponse {
  if (!(isPagedResultOfHostJobScheduleResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_job_schedule_response');
  }
  return value;
}

function isPagedResultOfHostJobScheduleResponse(value: unknown): value is PagedResultOfHostJobScheduleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostJobScheduleResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostMenuResponse(value: unknown): PagedResultOfHostMenuResponse {
  if (!(isPagedResultOfHostMenuResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_menu_response');
  }
  return value;
}

function isPagedResultOfHostMenuResponse(value: unknown): value is PagedResultOfHostMenuResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostMenuResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostOnlineSessionResponse(value: unknown): PagedResultOfHostOnlineSessionResponse {
  if (!(isPagedResultOfHostOnlineSessionResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_online_session_response');
  }
  return value;
}

function isPagedResultOfHostOnlineSessionResponse(value: unknown): value is PagedResultOfHostOnlineSessionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostOnlineSessionResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostRoleResponse(value: unknown): PagedResultOfHostRoleResponse {
  if (!(isPagedResultOfHostRoleResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_role_response');
  }
  return value;
}

function isPagedResultOfHostRoleResponse(value: unknown): value is PagedResultOfHostRoleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostRoleResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfHostUserResponse(value: unknown): PagedResultOfHostUserResponse {
  if (!(isPagedResultOfHostUserResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_user_response');
  }
  return value;
}

function isPagedResultOfHostUserResponse(value: unknown): value is PagedResultOfHostUserResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostUserResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfInboxMessageResponse(value: unknown): PagedResultOfInboxMessageResponse {
  if (!(isPagedResultOfInboxMessageResponse(value))) {
    throw new Error('client.invalid_paged_result_of_inbox_message_response');
  }
  return value;
}

function isPagedResultOfInboxMessageResponse(value: unknown): value is PagedResultOfInboxMessageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isInboxMessageResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfNotificationBindingResponse(value: unknown): PagedResultOfNotificationBindingResponse {
  if (!(isPagedResultOfNotificationBindingResponse(value))) {
    throw new Error('client.invalid_paged_result_of_notification_binding_response');
  }
  return value;
}

function isPagedResultOfNotificationBindingResponse(value: unknown): value is PagedResultOfNotificationBindingResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationBindingResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfNotificationDeliveryResponse(value: unknown): PagedResultOfNotificationDeliveryResponse {
  if (!(isPagedResultOfNotificationDeliveryResponse(value))) {
    throw new Error('client.invalid_paged_result_of_notification_delivery_response');
  }
  return value;
}

function isPagedResultOfNotificationDeliveryResponse(value: unknown): value is PagedResultOfNotificationDeliveryResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationDeliveryResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfNotificationProviderProfileResponse(value: unknown): PagedResultOfNotificationProviderProfileResponse {
  if (!(isPagedResultOfNotificationProviderProfileResponse(value))) {
    throw new Error('client.invalid_paged_result_of_notification_provider_profile_response');
  }
  return value;
}

function isPagedResultOfNotificationProviderProfileResponse(value: unknown): value is PagedResultOfNotificationProviderProfileResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationProviderProfileResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfNotificationTemplateResponse(value: unknown): PagedResultOfNotificationTemplateResponse {
  if (!(isPagedResultOfNotificationTemplateResponse(value))) {
    throw new Error('client.invalid_paged_result_of_notification_template_response');
  }
  return value;
}

function isPagedResultOfNotificationTemplateResponse(value: unknown): value is PagedResultOfNotificationTemplateResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationTemplateResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOperationLogResponse(value: unknown): PagedResultOfOperationLogResponse {
  if (!(isPagedResultOfOperationLogResponse(value))) {
    throw new Error('client.invalid_paged_result_of_operation_log_response');
  }
  return value;
}

function isPagedResultOfOperationLogResponse(value: unknown): value is PagedResultOfOperationLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOperationLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOrganizationAssignableUserResponse(value: unknown): PagedResultOfOrganizationAssignableUserResponse {
  if (!(isPagedResultOfOrganizationAssignableUserResponse(value))) {
    throw new Error('client.invalid_paged_result_of_organization_assignable_user_response');
  }
  return value;
}

function isPagedResultOfOrganizationAssignableUserResponse(value: unknown): value is PagedResultOfOrganizationAssignableUserResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationAssignableUserResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOrganizationPositionLevelResponse(value: unknown): PagedResultOfOrganizationPositionLevelResponse {
  if (!(isPagedResultOfOrganizationPositionLevelResponse(value))) {
    throw new Error('client.invalid_paged_result_of_organization_position_level_response');
  }
  return value;
}

function isPagedResultOfOrganizationPositionLevelResponse(value: unknown): value is PagedResultOfOrganizationPositionLevelResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationPositionLevelResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOrganizationPositionResponse(value: unknown): PagedResultOfOrganizationPositionResponse {
  if (!(isPagedResultOfOrganizationPositionResponse(value))) {
    throw new Error('client.invalid_paged_result_of_organization_position_response');
  }
  return value;
}

function isPagedResultOfOrganizationPositionResponse(value: unknown): value is PagedResultOfOrganizationPositionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationPositionResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOrganizationUnitResponse(value: unknown): PagedResultOfOrganizationUnitResponse {
  if (!(isPagedResultOfOrganizationUnitResponse(value))) {
    throw new Error('client.invalid_paged_result_of_organization_unit_response');
  }
  return value;
}

function isPagedResultOfOrganizationUnitResponse(value: unknown): value is PagedResultOfOrganizationUnitResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationUnitResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOrganizationUserPositionResponse(value: unknown): PagedResultOfOrganizationUserPositionResponse {
  if (!(isPagedResultOfOrganizationUserPositionResponse(value))) {
    throw new Error('client.invalid_paged_result_of_organization_user_position_response');
  }
  return value;
}

function isPagedResultOfOrganizationUserPositionResponse(value: unknown): value is PagedResultOfOrganizationUserPositionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationUserPositionResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOrganizationUserUnitResponse(value: unknown): PagedResultOfOrganizationUserUnitResponse {
  if (!(isPagedResultOfOrganizationUserUnitResponse(value))) {
    throw new Error('client.invalid_paged_result_of_organization_user_unit_response');
  }
  return value;
}

function isPagedResultOfOrganizationUserUnitResponse(value: unknown): value is PagedResultOfOrganizationUserUnitResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationUserUnitResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfOutboundCallLogResponse(value: unknown): PagedResultOfOutboundCallLogResponse {
  if (!(isPagedResultOfOutboundCallLogResponse(value))) {
    throw new Error('client.invalid_paged_result_of_outbound_call_log_response');
  }
  return value;
}

function isPagedResultOfOutboundCallLogResponse(value: unknown): value is PagedResultOfOutboundCallLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOutboundCallLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfSerialNumberRuleResponse(value: unknown): PagedResultOfSerialNumberRuleResponse {
  if (!(isPagedResultOfSerialNumberRuleResponse(value))) {
    throw new Error('client.invalid_paged_result_of_serial_number_rule_response');
  }
  return value;
}

function isPagedResultOfSerialNumberRuleResponse(value: unknown): value is PagedResultOfSerialNumberRuleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isSerialNumberRuleResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfTenantPackageSummary(value: unknown): PagedResultOfTenantPackageSummary {
  if (!(isPagedResultOfTenantPackageSummary(value))) {
    throw new Error('client.invalid_paged_result_of_tenant_package_summary');
  }
  return value;
}

function isPagedResultOfTenantPackageSummary(value: unknown): value is PagedResultOfTenantPackageSummary {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isTenantPackageSummary(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfTenantSummary(value: unknown): PagedResultOfTenantSummary {
  if (!(isPagedResultOfTenantSummary(value))) {
    throw new Error('client.invalid_paged_result_of_tenant_summary');
  }
  return value;
}

function isPagedResultOfTenantSummary(value: unknown): value is PagedResultOfTenantSummary {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isTenantSummary(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPagedResultOfWorkflowRecoveryTaskResponse(value: unknown): PagedResultOfWorkflowRecoveryTaskResponse {
  if (!(isPagedResultOfWorkflowRecoveryTaskResponse(value))) {
    throw new Error('client.invalid_paged_result_of_workflow_recovery_task_response');
  }
  return value;
}

function isPagedResultOfWorkflowRecoveryTaskResponse(value: unknown): value is PagedResultOfWorkflowRecoveryTaskResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWorkflowRecoveryTaskResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readPauseWorkflowInstanceRequest(value: unknown): PauseWorkflowInstanceRequest {
  if (!(isPauseWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_pause_workflow_instance_request');
  }
  return value;
}

function isPauseWorkflowInstanceRequest(value: unknown): value is PauseWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readPreviewSerialNumberRequest(value: unknown): PreviewSerialNumberRequest {
  if (!(isPreviewSerialNumberRequest(value))) {
    throw new Error('client.invalid_preview_serial_number_request');
  }
  return value;
}

function isPreviewSerialNumberRequest(value: unknown): value is PreviewSerialNumberRequest {
  return isRecord(value) && (typeof value["atUtc"] === 'string') && (typeof value["pattern"] === 'string') && (value["resetInterval"] === undefined || (isSerialNumberResetInterval(value["resetInterval"]))) && (isSerialNumberRuleScope(value["scope"])) && (typeof value["sequenceValue"] === 'number' && Number.isInteger(value["sequenceValue"])) && ((value["tenantIdentifier"] === null) || (typeof value["tenantIdentifier"] === 'string'));
}

export function readProblemDetails(value: unknown): ProblemDetails {
  if (!(isProblemDetails(value))) {
    throw new Error('client.invalid_problem_details');
  }
  return value;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return isRecord(value) && (value["detail"] === undefined || ((value["detail"] === null) || (typeof value["detail"] === 'string'))) && (value["instance"] === undefined || ((value["instance"] === null) || (typeof value["instance"] === 'string'))) && (value["status"] === undefined || ((value["status"] === null) || (typeof value["status"] === 'number' && Number.isInteger(value["status"])))) && (value["title"] === undefined || ((value["title"] === null) || (typeof value["title"] === 'string'))) && (value["type"] === undefined || ((value["type"] === null) || (typeof value["type"] === 'string')));
}

export function readProvisionTenantRequest(value: unknown): ProvisionTenantRequest {
  if (!(isProvisionTenantRequest(value))) {
    throw new Error('client.invalid_provision_tenant_request');
  }
  return value;
}

function isProvisionTenantRequest(value: unknown): value is ProvisionTenantRequest {
  return isRecord(value) && (typeof value["domain"] === 'string') && (typeof value["identifier"] === 'string') && (typeof value["name"] === 'string') && (value["tenantPackageId"] === undefined || ((value["tenantPackageId"] === null) || (typeof value["tenantPackageId"] === 'string' && guidPattern.test(value["tenantPackageId"]))));
}

export function readPublishHostAnnouncementRequest(value: unknown): PublishHostAnnouncementRequest {
  if (!(isPublishHostAnnouncementRequest(value))) {
    throw new Error('client.invalid_publish_host_announcement_request');
  }
  return value;
}

function isPublishHostAnnouncementRequest(value: unknown): value is PublishHostAnnouncementRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readPublishNotificationBindingRequest(value: unknown): PublishNotificationBindingRequest {
  if (!(isPublishNotificationBindingRequest(value))) {
    throw new Error('client.invalid_publish_notification_binding_request');
  }
  return value;
}

function isPublishNotificationBindingRequest(value: unknown): value is PublishNotificationBindingRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readPublishNotificationProviderProfileRequest(value: unknown): PublishNotificationProviderProfileRequest {
  if (!(isPublishNotificationProviderProfileRequest(value))) {
    throw new Error('client.invalid_publish_notification_provider_profile_request');
  }
  return value;
}

function isPublishNotificationProviderProfileRequest(value: unknown): value is PublishNotificationProviderProfileRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readPublishNotificationTemplateRequest(value: unknown): PublishNotificationTemplateRequest {
  if (!(isPublishNotificationTemplateRequest(value))) {
    throw new Error('client.invalid_publish_notification_template_request');
  }
  return value;
}

function isPublishNotificationTemplateRequest(value: unknown): value is PublishNotificationTemplateRequest {
  return isRecord(value) && (typeof value["contentClassificationKey"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readPublishWorkflowDefinitionRequest(value: unknown): PublishWorkflowDefinitionRequest {
  if (!(isPublishWorkflowDefinitionRequest(value))) {
    throw new Error('client.invalid_publish_workflow_definition_request');
  }
  return value;
}

function isPublishWorkflowDefinitionRequest(value: unknown): value is PublishWorkflowDefinitionRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"]));
}

export function readPublishWorkflowFormRequest(value: unknown): PublishWorkflowFormRequest {
  if (!(isPublishWorkflowFormRequest(value))) {
    throw new Error('client.invalid_publish_workflow_form_request');
  }
  return value;
}

function isPublishWorkflowFormRequest(value: unknown): value is PublishWorkflowFormRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"]));
}

export function readReassignWorkflowInstanceRequest(value: unknown): ReassignWorkflowInstanceRequest {
  if (!(isReassignWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_reassign_workflow_instance_request');
  }
  return value;
}

function isReassignWorkflowInstanceRequest(value: unknown): value is ReassignWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readRecipientEndpointResponse(value: unknown): RecipientEndpointResponse {
  if (!(isRecipientEndpointResponse(value))) {
    throw new Error('client.invalid_recipient_endpoint_response');
  }
  return value;
}

function isRecipientEndpointResponse(value: unknown): value is RecipientEndpointResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["endpointKindKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["maskedValue"] === 'string') && (typeof value["providerProfileVersionId"] === 'string' && guidPattern.test(value["providerProfileVersionId"])) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["verificationStatusKey"] === 'string');
}

export function readReconcileWorkflowRecoveryTaskRequest(value: unknown): ReconcileWorkflowRecoveryTaskRequest {
  if (!(isReconcileWorkflowRecoveryTaskRequest(value))) {
    throw new Error('client.invalid_reconcile_workflow_recovery_task_request');
  }
  return value;
}

function isReconcileWorkflowRecoveryTaskRequest(value: unknown): value is ReconcileWorkflowRecoveryTaskRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readRecoverWorkflowInstanceRequest(value: unknown): RecoverWorkflowInstanceRequest {
  if (!(isRecoverWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_recover_workflow_instance_request');
  }
  return value;
}

function isRecoverWorkflowInstanceRequest(value: unknown): value is RecoverWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && (typeof value["reason"] === 'string');
}

export function readReplaceHostRoleFieldGrantsRequest(value: unknown): ReplaceHostRoleFieldGrantsRequest {
  if (!(isReplaceHostRoleFieldGrantsRequest(value))) {
    throw new Error('client.invalid_replace_host_role_field_grants_request');
  }
  return value;
}

function isReplaceHostRoleFieldGrantsRequest(value: unknown): value is ReplaceHostRoleFieldGrantsRequest {
  return isRecord(value) && (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string')) && (typeof value["resourceKey"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readReplaceHostRolePermissionsRequest(value: unknown): ReplaceHostRolePermissionsRequest {
  if (!(isReplaceHostRolePermissionsRequest(value))) {
    throw new Error('client.invalid_replace_host_role_permissions_request');
  }
  return value;
}

function isReplaceHostRolePermissionsRequest(value: unknown): value is ReplaceHostRolePermissionsRequest {
  return isRecord(value) && (Array.isArray(value["permissionCodes"]) && value["permissionCodes"].every(item24 => typeof item24 === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readReplaceHostUserRolesRequest(value: unknown): ReplaceHostUserRolesRequest {
  if (!(isReplaceHostUserRolesRequest(value))) {
    throw new Error('client.invalid_replace_host_user_roles_request');
  }
  return value;
}

function isReplaceHostUserRolesRequest(value: unknown): value is ReplaceHostUserRolesRequest {
  return isRecord(value) && (Array.isArray(value["roleIds"]) && value["roleIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readResetHostUserPasswordRequest(value: unknown): ResetHostUserPasswordRequest {
  if (!(isResetHostUserPasswordRequest(value))) {
    throw new Error('client.invalid_reset_host_user_password_request');
  }
  return value;
}

function isResetHostUserPasswordRequest(value: unknown): value is ResetHostUserPasswordRequest {
  return isRecord(value) && (typeof value["password"] === 'string');
}

export function readRestoreDiagnosticPolicyRequest(value: unknown): RestoreDiagnosticPolicyRequest {
  if (!(isRestoreDiagnosticPolicyRequest(value))) {
    throw new Error('client.invalid_restore_diagnostic_policy_request');
  }
  return value;
}

function isRestoreDiagnosticPolicyRequest(value: unknown): value is RestoreDiagnosticPolicyRequest {
  return isRecord(value) && (typeof value["configEntryVersion"] === 'number' && Number.isInteger(value["configEntryVersion"]));
}

export function readRestoreHostDocumentItemRequest(value: unknown): RestoreHostDocumentItemRequest {
  if (!(isRestoreHostDocumentItemRequest(value))) {
    throw new Error('client.invalid_restore_host_document_item_request');
  }
  return value;
}

function isRestoreHostDocumentItemRequest(value: unknown): value is RestoreHostDocumentItemRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readResumeWorkflowInstanceRequest(value: unknown): ResumeWorkflowInstanceRequest {
  if (!(isResumeWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_resume_workflow_instance_request');
  }
  return value;
}

function isResumeWorkflowInstanceRequest(value: unknown): value is ResumeWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readRetryNotificationDeliveryRequest(value: unknown): RetryNotificationDeliveryRequest {
  if (!(isRetryNotificationDeliveryRequest(value))) {
    throw new Error('client.invalid_retry_notification_delivery_request');
  }
  return value;
}

function isRetryNotificationDeliveryRequest(value: unknown): value is RetryNotificationDeliveryRequest {
  return isRecord(value) && (typeof value["reason"] === 'string') && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"]));
}

export function readRetryWorkflowRecoveryTaskRequest(value: unknown): RetryWorkflowRecoveryTaskRequest {
  if (!(isRetryWorkflowRecoveryTaskRequest(value))) {
    throw new Error('client.invalid_retry_workflow_recovery_task_request');
  }
  return value;
}

function isRetryWorkflowRecoveryTaskRequest(value: unknown): value is RetryWorkflowRecoveryTaskRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && (typeof value["reason"] === 'string');
}

export function readReturnWorkflowTodoRequest(value: unknown): ReturnWorkflowTodoRequest {
  if (!(isReturnWorkflowTodoRequest(value))) {
    throw new Error('client.invalid_return_workflow_todo_request');
  }
  return value;
}

function isReturnWorkflowTodoRequest(value: unknown): value is ReturnWorkflowTodoRequest {
  return isRecord(value) && (typeof value["comment"] === 'string') && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"])) && (isJsonElement(value["fieldPatch"])) && (typeof value["idempotencyKey"] === 'string') && (typeof value["targetStepId"] === 'string' && guidPattern.test(value["targetStepId"]));
}

export function readRevokeSuperAdministratorRequest(value: unknown): RevokeSuperAdministratorRequest {
  if (!(isRevokeSuperAdministratorRequest(value))) {
    throw new Error('client.invalid_revoke_super_administrator_request');
  }
  return value;
}

function isRevokeSuperAdministratorRequest(value: unknown): value is RevokeSuperAdministratorRequest {
  return isRecord(value) && (typeof value["currentPassword"] === 'string') && (value["totpCode"] === undefined || ((value["totpCode"] === null) || (typeof value["totpCode"] === 'string')));
}

export function readSendHostInboxMessageRequest(value: unknown): SendHostInboxMessageRequest {
  if (!(isSendHostInboxMessageRequest(value))) {
    throw new Error('client.invalid_send_host_inbox_message_request');
  }
  return value;
}

function isSendHostInboxMessageRequest(value: unknown): value is SendHostInboxMessageRequest {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["recipientUserId"] === 'string' && guidPattern.test(value["recipientUserId"])) && (typeof value["title"] === 'string');
}

export function readSendRecipientEndpointVerificationResponse(value: unknown): SendRecipientEndpointVerificationResponse {
  if (!(isSendRecipientEndpointVerificationResponse(value))) {
    throw new Error('client.invalid_send_recipient_endpoint_verification_response');
  }
  return value;
}

function isSendRecipientEndpointVerificationResponse(value: unknown): value is SendRecipientEndpointVerificationResponse {
  return isRecord(value) && (typeof value["expiresAtUtc"] === 'string') && (typeof value["resendAvailableAtUtc"] === 'string');
}

export function readSerialNumberPreviewResponse(value: unknown): SerialNumberPreviewResponse {
  if (!(isSerialNumberPreviewResponse(value))) {
    throw new Error('client.invalid_serial_number_preview_response');
  }
  return value;
}

function isSerialNumberPreviewResponse(value: unknown): value is SerialNumberPreviewResponse {
  return isRecord(value) && (typeof value["resetBucket"] === 'string') && (typeof value["sequenceValue"] === 'number' && Number.isInteger(value["sequenceValue"])) && (typeof value["value"] === 'string');
}

export function readSerialNumberResetInterval(value: unknown): SerialNumberResetInterval {
  if (!(isSerialNumberResetInterval(value))) {
    throw new Error('client.invalid_serial_number_reset_interval');
  }
  return value;
}

function isSerialNumberResetInterval(value: unknown): value is SerialNumberResetInterval {
  return typeof value === 'number' && Number.isInteger(value);
}

export function readSerialNumberRuleResponse(value: unknown): SerialNumberRuleResponse {
  if (!(isSerialNumberRuleResponse(value))) {
    throw new Error('client.invalid_serial_number_rule_response');
  }
  return value;
}

function isSerialNumberRuleResponse(value: unknown): value is SerialNumberRuleResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maximumValue"] === 'number' && Number.isInteger(value["maximumValue"])) && (typeof value["minimumValue"] === 'number' && Number.isInteger(value["minimumValue"])) && (typeof value["pattern"] === 'string') && (isSerialNumberResetInterval(value["resetInterval"])) && (typeof value["ruleKey"] === 'string') && (isSerialNumberRuleScope(value["scope"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"]))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readSerialNumberRuleScope(value: unknown): SerialNumberRuleScope {
  if (!(isSerialNumberRuleScope(value))) {
    throw new Error('client.invalid_serial_number_rule_scope');
  }
  return value;
}

function isSerialNumberRuleScope(value: unknown): value is SerialNumberRuleScope {
  return typeof value === 'number' && Number.isInteger(value);
}

export function readSetHostDocumentPermissionsRequest(value: unknown): SetHostDocumentPermissionsRequest {
  if (!(isSetHostDocumentPermissionsRequest(value))) {
    throw new Error('client.invalid_set_host_document_permissions_request');
  }
  return value;
}

function isSetHostDocumentPermissionsRequest(value: unknown): value is SetHostDocumentPermissionsRequest {
  return isRecord(value) && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => isHostDocumentPermissionEntry(item20)));
}

export function readSetNotificationProviderProfileEnabledRequest(value: unknown): SetNotificationProviderProfileEnabledRequest {
  if (!(isSetNotificationProviderProfileEnabledRequest(value))) {
    throw new Error('client.invalid_set_notification_provider_profile_enabled_request');
  }
  return value;
}

function isSetNotificationProviderProfileEnabledRequest(value: unknown): value is SetNotificationProviderProfileEnabledRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readStartWorkflowInstanceRequest(value: unknown): StartWorkflowInstanceRequest {
  if (!(isStartWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_start_workflow_instance_request');
  }
  return value;
}

function isStartWorkflowInstanceRequest(value: unknown): value is StartWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["businessId"] === 'string') && (typeof value["businessType"] === 'string') && (typeof value["definitionVersionId"] === 'string' && guidPattern.test(value["definitionVersionId"])) && (typeof value["idempotencyKey"] === 'string') && (isJsonElement(value["initialValues"]));
}

export function readStream(value: unknown): Stream {
  if (!(isStream(value))) {
    throw new Error('client.invalid_stream');
  }
  return value;
}

function isStream(value: unknown): value is Stream {
  return value instanceof Blob;
}

export function readSuperAdministratorAuditResponse(value: unknown): SuperAdministratorAuditResponse {
  if (!(isSuperAdministratorAuditResponse(value))) {
    throw new Error('client.invalid_super_administrator_audit_response');
  }
  return value;
}

function isSuperAdministratorAuditResponse(value: unknown): value is SuperAdministratorAuditResponse {
  return isRecord(value) && ((value["actorUserId"] === null) || (typeof value["actorUserId"] === 'string' && guidPattern.test(value["actorUserId"]))) && (typeof value["eventType"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && (typeof value["resultCode"] === 'string') && (typeof value["succeeded"] === 'boolean') && (typeof value["targetUserId"] === 'string' && guidPattern.test(value["targetUserId"]));
}

export function readSuperAdministratorChangeResponse(value: unknown): SuperAdministratorChangeResponse {
  if (!(isSuperAdministratorChangeResponse(value))) {
    throw new Error('client.invalid_super_administrator_change_response');
  }
  return value;
}

function isSuperAdministratorChangeResponse(value: unknown): value is SuperAdministratorChangeResponse {
  return isRecord(value) && (typeof value["changed"] === 'boolean') && (typeof value["targetUserId"] === 'string' && guidPattern.test(value["targetUserId"]));
}

export function readSuperAdministratorResponse(value: unknown): SuperAdministratorResponse {
  if (!(isSuperAdministratorResponse(value))) {
    throw new Error('client.invalid_super_administrator_response');
  }
  return value;
}

function isSuperAdministratorResponse(value: unknown): value is SuperAdministratorResponse {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["isActive"] === 'boolean') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
}

export function readTenantPackageSummary(value: unknown): TenantPackageSummary {
  if (!(isTenantPackageSummary(value))) {
    throw new Error('client.invalid_tenant_package_summary');
  }
  return value;
}

function isTenantPackageSummary(value: unknown): value is TenantPackageSummary {
  return isRecord(value) && (value["assignedTenantCount"] === undefined || (typeof value["assignedTenantCount"] === 'number' && Number.isInteger(value["assignedTenantCount"]))) && (typeof value["code"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readTenantSummary(value: unknown): TenantSummary {
  if (!(isTenantSummary(value))) {
    throw new Error('client.invalid_tenant_summary');
  }
  return value;
}

function isTenantSummary(value: unknown): value is TenantSummary {
  return isRecord(value) && (value["defaultLocale"] === undefined || (typeof value["defaultLocale"] === 'string')) && (typeof value["domain"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["identifier"] === 'string') && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && (value["tenantPackageCode"] === undefined || ((value["tenantPackageCode"] === null) || (typeof value["tenantPackageCode"] === 'string'))) && (value["tenantPackageId"] === undefined || ((value["tenantPackageId"] === null) || (typeof value["tenantPackageId"] === 'string' && guidPattern.test(value["tenantPackageId"])))) && (value["tenantPackageName"] === undefined || ((value["tenantPackageName"] === null) || (typeof value["tenantPackageName"] === 'string'))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readTokenResponse(value: unknown): TokenResponse {
  if (!(isTokenResponse(value))) {
    throw new Error('client.invalid_token_response');
  }
  return value;
}

function isTokenResponse(value: unknown): value is TokenResponse {
  return isRecord(value) && (typeof value["accessToken"] === 'string') && (typeof value["expiresAtUtc"] === 'string') && (typeof value["tokenType"] === 'string');
}

export function readTotpEnrollmentStatusResponse(value: unknown): TotpEnrollmentStatusResponse {
  if (!(isTotpEnrollmentStatusResponse(value))) {
    throw new Error('client.invalid_totp_enrollment_status_response');
  }
  return value;
}

function isTotpEnrollmentStatusResponse(value: unknown): value is TotpEnrollmentStatusResponse {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["isEnrolled"] === 'boolean');
}

export function readUpdateCodeGenerationTemplateRequest(value: unknown): UpdateCodeGenerationTemplateRequest {
  if (!(isUpdateCodeGenerationTemplateRequest(value))) {
    throw new Error('client.invalid_update_code_generation_template_request');
  }
  return value;
}

function isUpdateCodeGenerationTemplateRequest(value: unknown): value is UpdateCodeGenerationTemplateRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string') && (isCodeGenerationPreviewRequest(value["schema"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateConfigEntryRequest(value: unknown): UpdateConfigEntryRequest {
  if (!(isUpdateConfigEntryRequest(value))) {
    throw new Error('client.invalid_update_config_entry_request');
  }
  return value;
}

function isUpdateConfigEntryRequest(value: unknown): value is UpdateConfigEntryRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateDiagnosticPolicyRequest(value: unknown): UpdateDiagnosticPolicyRequest {
  if (!(isUpdateDiagnosticPolicyRequest(value))) {
    throw new Error('client.invalid_update_diagnostic_policy_request');
  }
  return value;
}

function isUpdateDiagnosticPolicyRequest(value: unknown): value is UpdateDiagnosticPolicyRequest {
  return isRecord(value) && (typeof value["configEntryVersion"] === 'number' && Number.isInteger(value["configEntryVersion"])) && (typeof value["pressureState"] === 'string') && (Array.isArray(value["rules"]) && value["rules"].every(item14 => isDiagnosticPolicyRuleRequest(item14)));
}

export function readUpdateDictItemRequest(value: unknown): UpdateDictItemRequest {
  if (!(isUpdateDictItemRequest(value))) {
    throw new Error('client.invalid_update_dict_item_request');
  }
  return value;
}

function isUpdateDictItemRequest(value: unknown): value is UpdateDictItemRequest {
  return isRecord(value) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["label"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateDictTypeRequest(value: unknown): UpdateDictTypeRequest {
  if (!(isUpdateDictTypeRequest(value))) {
    throw new Error('client.invalid_update_dict_type_request');
  }
  return value;
}

function isUpdateDictTypeRequest(value: unknown): value is UpdateDictTypeRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostAnnouncementRequest(value: unknown): UpdateHostAnnouncementRequest {
  if (!(isUpdateHostAnnouncementRequest(value))) {
    throw new Error('client.invalid_update_host_announcement_request');
  }
  return value;
}

function isUpdateHostAnnouncementRequest(value: unknown): value is UpdateHostAnnouncementRequest {
  return isRecord(value) && (value["audienceKind"] === undefined || ((value["audienceKind"] === null) || (typeof value["audienceKind"] === 'string'))) && (typeof value["content"] === 'string') && (value["kind"] === undefined || ((value["kind"] === null) || (typeof value["kind"] === 'string'))) && (value["targetOrganizations"] === undefined || ((value["targetOrganizations"] === null) || (Array.isArray(value["targetOrganizations"]) && value["targetOrganizations"].every(item28 => isHostAnnouncementTargetOrganization(item28))))) && (value["targetUserIds"] === undefined || ((value["targetUserIds"] === null) || (Array.isArray(value["targetUserIds"]) && value["targetUserIds"].every(item22 => typeof item22 === 'string' && guidPattern.test(item22))))) && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostDocumentCategoryRequest(value: unknown): UpdateHostDocumentCategoryRequest {
  if (!(isUpdateHostDocumentCategoryRequest(value))) {
    throw new Error('client.invalid_update_host_document_category_request');
  }
  return value;
}

function isUpdateHostDocumentCategoryRequest(value: unknown): value is UpdateHostDocumentCategoryRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isInteger(value["sortOrder"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostDocumentItemRequest(value: unknown): UpdateHostDocumentItemRequest {
  if (!(isUpdateHostDocumentItemRequest(value))) {
    throw new Error('client.invalid_update_host_document_item_request');
  }
  return value;
}

function isUpdateHostDocumentItemRequest(value: unknown): value is UpdateHostDocumentItemRequest {
  return isRecord(value) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["sort"] === null) || (typeof value["sort"] === 'number' && Number.isInteger(value["sort"]))) && ((value["status"] === null) || (isHostDocumentStatus(value["status"]))) && ((value["tagIds"] === null) || (Array.isArray(value["tagIds"]) && value["tagIds"].every(item15 => typeof item15 === 'string' && guidPattern.test(item15)))) && ((value["thumbnail"] === null) || (typeof value["thumbnail"] === 'string')) && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostDocumentShareStatusRequest(value: unknown): UpdateHostDocumentShareStatusRequest {
  if (!(isUpdateHostDocumentShareStatusRequest(value))) {
    throw new Error('client.invalid_update_host_document_share_status_request');
  }
  return value;
}

function isUpdateHostDocumentShareStatusRequest(value: unknown): value is UpdateHostDocumentShareStatusRequest {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostDocumentTagRequest(value: unknown): UpdateHostDocumentTagRequest {
  if (!(isUpdateHostDocumentTagRequest(value))) {
    throw new Error('client.invalid_update_host_document_tag_request');
  }
  return value;
}

function isUpdateHostDocumentTagRequest(value: unknown): value is UpdateHostDocumentTagRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostJobDefinitionRequest(value: unknown): UpdateHostJobDefinitionRequest {
  if (!(isUpdateHostJobDefinitionRequest(value))) {
    throw new Error('client.invalid_update_host_job_definition_request');
  }
  return value;
}

function isUpdateHostJobDefinitionRequest(value: unknown): value is UpdateHostJobDefinitionRequest {
  return isRecord(value) && (typeof value["allowConcurrentExecutions"] === 'boolean') && ((value["args"] === null) || (isHttpJobArgs(value["args"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["handlerKind"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostJobScheduleRequest(value: unknown): UpdateHostJobScheduleRequest {
  if (!(isUpdateHostJobScheduleRequest(value))) {
    throw new Error('client.invalid_update_host_job_schedule_request');
  }
  return value;
}

function isUpdateHostJobScheduleRequest(value: unknown): value is UpdateHostJobScheduleRequest {
  return isRecord(value) && ((value["args"] === null) || (typeof value["args"] === 'string')) && ((value["cronExpression"] === null) || (typeof value["cronExpression"] === 'string')) && ((value["endTime"] === null) || (typeof value["endTime"] === 'string')) && (typeof value["misfirePolicy"] === 'string') && ((value["oneTimeAtUtc"] === null) || (typeof value["oneTimeAtUtc"] === 'string')) && ((value["startTime"] === null) || (typeof value["startTime"] === 'string')) && (typeof value["timeZoneId"] === 'string') && (typeof value["triggerKind"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostMenuRequest(value: unknown): UpdateHostMenuRequest {
  if (!(isUpdateHostMenuRequest(value))) {
    throw new Error('client.invalid_update_host_menu_request');
  }
  return value;
}

function isUpdateHostMenuRequest(value: unknown): value is UpdateHostMenuRequest {
  return isRecord(value) && (typeof value["caption"] === 'string') && (typeof value["componentKey"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["icon"] === 'string') && (value["isAffix"] === undefined || (typeof value["isAffix"] === 'boolean')) && (value["isEmbedded"] === undefined || (typeof value["isEmbedded"] === 'boolean')) && (value["isHidden"] === undefined || (typeof value["isHidden"] === 'boolean')) && (value["isKeepAlive"] === undefined || (typeof value["isKeepAlive"] === 'boolean')) && (value["linkUrl"] === undefined || ((value["linkUrl"] === null) || (typeof value["linkUrl"] === 'string'))) && (value["menuType"] === undefined || (typeof value["menuType"] === 'string')) && ((value["parentId"] === null) || (typeof value["parentId"] === 'string')) && (typeof value["path"] === 'string') && (value["redirect"] === undefined || ((value["redirect"] === null) || (typeof value["redirect"] === 'string'))) && (value["remark"] === undefined || ((value["remark"] === null) || (typeof value["remark"] === 'string'))) && (typeof value["requiredPermission"] === 'string') && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostRoleDataScopeRequest(value: unknown): UpdateHostRoleDataScopeRequest {
  if (!(isUpdateHostRoleDataScopeRequest(value))) {
    throw new Error('client.invalid_update_host_role_data_scope_request');
  }
  return value;
}

function isUpdateHostRoleDataScopeRequest(value: unknown): value is UpdateHostRoleDataScopeRequest {
  return isRecord(value) && (typeof value["dataScopeKind"] === 'string') && (value["tenantId"] === undefined || ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])))) && ((value["unitIds"] === null) || (Array.isArray(value["unitIds"]) && value["unitIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16)))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostRoleRequest(value: unknown): UpdateHostRoleRequest {
  if (!(isUpdateHostRoleRequest(value))) {
    throw new Error('client.invalid_update_host_role_request');
  }
  return value;
}

function isUpdateHostRoleRequest(value: unknown): value is UpdateHostRoleRequest {
  return isRecord(value) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostTenantPackageRequest(value: unknown): UpdateHostTenantPackageRequest {
  if (!(isUpdateHostTenantPackageRequest(value))) {
    throw new Error('client.invalid_update_host_tenant_package_request');
  }
  return value;
}

function isUpdateHostTenantPackageRequest(value: unknown): value is UpdateHostTenantPackageRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostTenantRequest(value: unknown): UpdateHostTenantRequest {
  if (!(isUpdateHostTenantRequest(value))) {
    throw new Error('client.invalid_update_host_tenant_request');
  }
  return value;
}

function isUpdateHostTenantRequest(value: unknown): value is UpdateHostTenantRequest {
  return isRecord(value) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateHostUserRequest(value: unknown): UpdateHostUserRequest {
  if (!(isUpdateHostUserRequest(value))) {
    throw new Error('client.invalid_update_host_user_request');
  }
  return value;
}

function isUpdateHostUserRequest(value: unknown): value is UpdateHostUserRequest {
  return isRecord(value) && (value["accountType"] === undefined || ((value["accountType"] === null) || (typeof value["accountType"] === 'string'))) && (typeof value["displayName"] === 'string') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"])))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateLocaleRequest(value: unknown): UpdateLocaleRequest {
  if (!(isUpdateLocaleRequest(value))) {
    throw new Error('client.invalid_update_locale_request');
  }
  return value;
}

function isUpdateLocaleRequest(value: unknown): value is UpdateLocaleRequest {
  return isRecord(value) && (typeof value["locale"] === 'string') && (typeof value["profileVersion"] === 'number' && Number.isInteger(value["profileVersion"]));
}

export function readUpdateNotificationBindingRequest(value: unknown): UpdateNotificationBindingRequest {
  if (!(isUpdateNotificationBindingRequest(value))) {
    throw new Error('client.invalid_update_notification_binding_request');
  }
  return value;
}

function isUpdateNotificationBindingRequest(value: unknown): value is UpdateNotificationBindingRequest {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["dispatchModeKey"] === 'string') && (typeof value["producerKey"] === 'string') && (typeof value["sceneKey"] === 'string') && (Array.isArray(value["targets"]) && value["targets"].every(item16 => isNotificationBindingTargetInput(item16))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateNotificationProviderProfileRequest(value: unknown): UpdateNotificationProviderProfileRequest {
  if (!(isUpdateNotificationProviderProfileRequest(value))) {
    throw new Error('client.invalid_update_notification_provider_profile_request');
  }
  return value;
}

function isUpdateNotificationProviderProfileRequest(value: unknown): value is UpdateNotificationProviderProfileRequest {
  return isRecord(value) && (isJsonElement(value["nonSecretConfig"])) && ((value["secretReference"] === null) || (typeof value["secretReference"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateNotificationTemplateRequest(value: unknown): UpdateNotificationTemplateRequest {
  if (!(isUpdateNotificationTemplateRequest(value))) {
    throw new Error('client.invalid_update_notification_template_request');
  }
  return value;
}

function isUpdateNotificationTemplateRequest(value: unknown): value is UpdateNotificationTemplateRequest {
  return isRecord(value) && (isNotificationTemplateBody(value["draftBody"])) && (typeof value["draftSubject"] === 'string') && (isNotificationTemplateParameterSchema(value["parameterSchema"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateOrganizationPositionLevelRequest(value: unknown): UpdateOrganizationPositionLevelRequest {
  if (!(isUpdateOrganizationPositionLevelRequest(value))) {
    throw new Error('client.invalid_update_organization_position_level_request');
  }
  return value;
}

function isUpdateOrganizationPositionLevelRequest(value: unknown): value is UpdateOrganizationPositionLevelRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateOrganizationPositionRequest(value: unknown): UpdateOrganizationPositionRequest {
  if (!(isUpdateOrganizationPositionRequest(value))) {
    throw new Error('client.invalid_update_organization_position_request');
  }
  return value;
}

function isUpdateOrganizationPositionRequest(value: unknown): value is UpdateOrganizationPositionRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateOrganizationUnitRequest(value: unknown): UpdateOrganizationUnitRequest {
  if (!(isUpdateOrganizationUnitRequest(value))) {
    throw new Error('client.invalid_update_organization_unit_request');
  }
  return value;
}

function isUpdateOrganizationUnitRequest(value: unknown): value is UpdateOrganizationUnitRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateOrganizationUserPositionRequest(value: unknown): UpdateOrganizationUserPositionRequest {
  if (!(isUpdateOrganizationUserPositionRequest(value))) {
    throw new Error('client.invalid_update_organization_user_position_request');
  }
  return value;
}

function isUpdateOrganizationUserPositionRequest(value: unknown): value is UpdateOrganizationUserPositionRequest {
  return isRecord(value) && (typeof value["isPrimary"] === 'boolean') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateOrganizationUserUnitRequest(value: unknown): UpdateOrganizationUserUnitRequest {
  if (!(isUpdateOrganizationUserUnitRequest(value))) {
    throw new Error('client.invalid_update_organization_user_unit_request');
  }
  return value;
}

function isUpdateOrganizationUserUnitRequest(value: unknown): value is UpdateOrganizationUserUnitRequest {
  return isRecord(value) && (typeof value["isPrimary"] === 'boolean') && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateSerialNumberRuleRequest(value: unknown): UpdateSerialNumberRuleRequest {
  if (!(isUpdateSerialNumberRuleRequest(value))) {
    throw new Error('client.invalid_update_serial_number_rule_request');
  }
  return value;
}

function isUpdateSerialNumberRuleRequest(value: unknown): value is UpdateSerialNumberRuleRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maximumValue"] === 'number' && Number.isInteger(value["maximumValue"])) && (typeof value["minimumValue"] === 'number' && Number.isInteger(value["minimumValue"])) && (typeof value["pattern"] === 'string') && (isSerialNumberResetInterval(value["resetInterval"])) && (isSerialNumberRuleScope(value["scope"])) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readUpdateWorkflowDefinitionDraftRequest(value: unknown): UpdateWorkflowDefinitionDraftRequest {
  if (!(isUpdateWorkflowDefinitionDraftRequest(value))) {
    throw new Error('client.invalid_update_workflow_definition_draft_request');
  }
  return value;
}

function isUpdateWorkflowDefinitionDraftRequest(value: unknown): value is UpdateWorkflowDefinitionDraftRequest {
  return isRecord(value) && (isWorkflowDefinitionDraft(value["draft"])) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"]));
}

export function readUpdateWorkflowFormDraftRequest(value: unknown): UpdateWorkflowFormDraftRequest {
  if (!(isUpdateWorkflowFormDraftRequest(value))) {
    throw new Error('client.invalid_update_workflow_form_draft_request');
  }
  return value;
}

function isUpdateWorkflowFormDraftRequest(value: unknown): value is UpdateWorkflowFormDraftRequest {
  return isRecord(value) && (isWorkflowFormSchema(value["draft"])) && (typeof value["expectedRevision"] === 'number' && Number.isInteger(value["expectedRevision"]));
}

export function readVerifyRecipientEndpointCodeRequest(value: unknown): VerifyRecipientEndpointCodeRequest {
  if (!(isVerifyRecipientEndpointCodeRequest(value))) {
    throw new Error('client.invalid_verify_recipient_endpoint_code_request');
  }
  return value;
}

function isVerifyRecipientEndpointCodeRequest(value: unknown): value is VerifyRecipientEndpointCodeRequest {
  return isRecord(value) && (typeof value["code"] === 'string');
}

export function readWorkflowCcReadResponse(value: unknown): WorkflowCcReadResponse {
  if (!(isWorkflowCcReadResponse(value))) {
    throw new Error('client.invalid_workflow_cc_read_response');
  }
  return value;
}

function isWorkflowCcReadResponse(value: unknown): value is WorkflowCcReadResponse {
  return isRecord(value) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["readAtUtc"] === 'string');
}

export function readWorkflowCcResponse(value: unknown): WorkflowCcResponse {
  if (!(isWorkflowCcResponse(value))) {
    throw new Error('client.invalid_workflow_cc_response');
  }
  return value;
}

function isWorkflowCcResponse(value: unknown): value is WorkflowCcResponse {
  return isRecord(value) && (typeof value["businessId"] === 'string') && (typeof value["businessType"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["nodeKey"] === 'string') && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && ((value["stepId"] === null) || (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"])));
}

export function readWorkflowDefinitionDraft(value: unknown): WorkflowDefinitionDraft {
  if (!(isWorkflowDefinitionDraft(value))) {
    throw new Error('client.invalid_workflow_definition_draft');
  }
  return value;
}

function isWorkflowDefinitionDraft(value: unknown): value is WorkflowDefinitionDraft {
  return isRecord(value) && (Array.isArray(value["nodes"]) && value["nodes"].every(item14 => isWorkflowNodeDraft(item14))) && (typeof value["schemaVersion"] === 'number' && Number.isInteger(value["schemaVersion"]));
}

export function readWorkflowDefinitionResponse(value: unknown): WorkflowDefinitionResponse {
  if (!(isWorkflowDefinitionResponse(value))) {
    throw new Error('client.invalid_workflow_definition_response');
  }
  return value;
}

function isWorkflowDefinitionResponse(value: unknown): value is WorkflowDefinitionResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["definitionKey"] === 'string') && (isWorkflowDefinitionDraft(value["draft"])) && (typeof value["draftRevision"] === 'number' && Number.isInteger(value["draftRevision"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
}

export function readWorkflowDefinitionVersionResponse(value: unknown): WorkflowDefinitionVersionResponse {
  if (!(isWorkflowDefinitionVersionResponse(value))) {
    throw new Error('client.invalid_workflow_definition_version_response');
  }
  return value;
}

function isWorkflowDefinitionVersionResponse(value: unknown): value is WorkflowDefinitionVersionResponse {
  return isRecord(value) && (typeof value["canonicalJson"] === 'string') && (typeof value["contentHash"] === 'string') && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedById"] === 'string' && guidPattern.test(value["publishedById"])) && (typeof value["schemaVersion"] === 'number' && Number.isInteger(value["schemaVersion"])) && (typeof value["versionNumber"] === 'number' && Number.isInteger(value["versionNumber"]));
}

export function readWorkflowExecutionLogResponse(value: unknown): WorkflowExecutionLogResponse {
  if (!(isWorkflowExecutionLogResponse(value))) {
    throw new Error('client.invalid_workflow_execution_log_response');
  }
  return value;
}

function isWorkflowExecutionLogResponse(value: unknown): value is WorkflowExecutionLogResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && ((value["fromStatusKey"] === null) || (typeof value["fromStatusKey"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && ((value["stepId"] === null) || (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"]))) && (typeof value["toStatusKey"] === 'string') && (typeof value["transitionKey"] === 'string');
}

export function readWorkflowFormComponentCatalogResponse(value: unknown): WorkflowFormComponentCatalogResponse {
  if (!(isWorkflowFormComponentCatalogResponse(value))) {
    throw new Error('client.invalid_workflow_form_component_catalog_response');
  }
  return value;
}

function isWorkflowFormComponentCatalogResponse(value: unknown): value is WorkflowFormComponentCatalogResponse {
  return isRecord(value) && (typeof value["adapterVersion"] === 'number' && Number.isInteger(value["adapterVersion"])) && (typeof value["catalogVersion"] === 'number' && Number.isInteger(value["catalogVersion"])) && (Array.isArray(value["components"]) && value["components"].every(item19 => isWorkflowFormComponentResponse(item19))) && (typeof value["schemaVersion"] === 'number' && Number.isInteger(value["schemaVersion"]));
}

export function readWorkflowFormComponentResponse(value: unknown): WorkflowFormComponentResponse {
  if (!(isWorkflowFormComponentResponse(value))) {
    throw new Error('client.invalid_workflow_form_component_response');
  }
  return value;
}

function isWorkflowFormComponentResponse(value: unknown): value is WorkflowFormComponentResponse {
  return isRecord(value) && (Array.isArray(value["constraintKeys"]) && value["constraintKeys"].every(item23 => typeof item23 === 'string')) && (typeof value["designable"] === 'boolean') && (typeof value["executable"] === 'boolean') && (typeof value["fieldTypeKey"] === 'string') && (typeof value["publishable"] === 'boolean');
}

export function readWorkflowFormField(value: unknown): WorkflowFormField {
  if (!(isWorkflowFormField(value))) {
    throw new Error('client.invalid_workflow_form_field');
  }
  return value;
}

function isWorkflowFormField(value: unknown): value is WorkflowFormField {
  return isRecord(value) && (isRecord(value["constraints"])) && (typeof value["fieldKey"] === 'string') && (typeof value["fieldTypeKey"] === 'string') && (typeof value["required"] === 'boolean');
}

export function readWorkflowFormResponse(value: unknown): WorkflowFormResponse {
  if (!(isWorkflowFormResponse(value))) {
    throw new Error('client.invalid_workflow_form_response');
  }
  return value;
}

function isWorkflowFormResponse(value: unknown): value is WorkflowFormResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (isWorkflowFormSchema(value["draft"])) && (typeof value["draftRevision"] === 'number' && Number.isInteger(value["draftRevision"])) && (typeof value["formKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string'));
}

export function readWorkflowFormSchema(value: unknown): WorkflowFormSchema {
  if (!(isWorkflowFormSchema(value))) {
    throw new Error('client.invalid_workflow_form_schema');
  }
  return value;
}

function isWorkflowFormSchema(value: unknown): value is WorkflowFormSchema {
  return isRecord(value) && (typeof value["adapterVersion"] === 'number' && Number.isInteger(value["adapterVersion"])) && (typeof value["schemaVersion"] === 'number' && Number.isInteger(value["schemaVersion"])) && (Array.isArray(value["sections"]) && value["sections"].every(item17 => isWorkflowFormSection(item17)));
}

export function readWorkflowFormSection(value: unknown): WorkflowFormSection {
  if (!(isWorkflowFormSection(value))) {
    throw new Error('client.invalid_workflow_form_section');
  }
  return value;
}

function isWorkflowFormSection(value: unknown): value is WorkflowFormSection {
  return isRecord(value) && (Array.isArray(value["fields"]) && value["fields"].every(item15 => isWorkflowFormField(item15))) && (typeof value["sectionKey"] === 'string');
}

export function readWorkflowFormVersionResponse(value: unknown): WorkflowFormVersionResponse {
  if (!(isWorkflowFormVersionResponse(value))) {
    throw new Error('client.invalid_workflow_form_version_response');
  }
  return value;
}

function isWorkflowFormVersionResponse(value: unknown): value is WorkflowFormVersionResponse {
  return isRecord(value) && (typeof value["adapterVersion"] === 'number' && Number.isInteger(value["adapterVersion"])) && (typeof value["componentCatalogVersion"] === 'number' && Number.isInteger(value["componentCatalogVersion"])) && (typeof value["contentHash"] === 'string') && (typeof value["formDefinitionId"] === 'string' && guidPattern.test(value["formDefinitionId"])) && (typeof value["formSchemaJson"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedById"] === 'string' && guidPattern.test(value["publishedById"])) && (typeof value["schemaVersion"] === 'number' && Number.isInteger(value["schemaVersion"])) && (typeof value["versionNumber"] === 'number' && Number.isInteger(value["versionNumber"])) && (typeof value["webRenderSchemaJson"] === 'string');
}

export function readWorkflowInstanceResponse(value: unknown): WorkflowInstanceResponse {
  if (!(isWorkflowInstanceResponse(value))) {
    throw new Error('client.invalid_workflow_instance_response');
  }
  return value;
}

function isWorkflowInstanceResponse(value: unknown): value is WorkflowInstanceResponse {
  return isRecord(value) && ((value["activeTodoId"] === null) || (typeof value["activeTodoId"] === 'string' && guidPattern.test(value["activeTodoId"]))) && (typeof value["businessId"] === 'string') && (typeof value["businessType"] === 'string') && (typeof value["definitionVersionId"] === 'string' && guidPattern.test(value["definitionVersionId"])) && (value["dueAtUtc"] === undefined || ((value["dueAtUtc"] === null) || (typeof value["dueAtUtc"] === 'string'))) && (value["escalatedAtUtc"] === undefined || ((value["escalatedAtUtc"] === null) || (typeof value["escalatedAtUtc"] === 'string'))) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (value["reminderCount"] === undefined || (typeof value["reminderCount"] === 'number' && Number.isInteger(value["reminderCount"]))) && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"])) && (typeof value["startedAtUtc"] === 'string') && (typeof value["statusKey"] === 'string') && (value["timeoutStatusKey"] === undefined || (typeof value["timeoutStatusKey"] === 'string'));
}

export function readWorkflowNodeDraft(value: unknown): WorkflowNodeDraft {
  if (!(isWorkflowNodeDraft(value))) {
    throw new Error('client.invalid_workflow_node_draft');
  }
  return value;
}

function isWorkflowNodeDraft(value: unknown): value is WorkflowNodeDraft {
  return isRecord(value) && (isJsonElement(value["config"])) && (typeof value["nodeKey"] === 'string') && (typeof value["nodeSchemaVersion"] === 'number' && Number.isInteger(value["nodeSchemaVersion"])) && (typeof value["nodeTypeKey"] === 'string');
}

export function readWorkflowNodeTypeCatalogResponse(value: unknown): WorkflowNodeTypeCatalogResponse {
  if (!(isWorkflowNodeTypeCatalogResponse(value))) {
    throw new Error('client.invalid_workflow_node_type_catalog_response');
  }
  return value;
}

function isWorkflowNodeTypeCatalogResponse(value: unknown): value is WorkflowNodeTypeCatalogResponse {
  return isRecord(value) && (typeof value["catalogVersion"] === 'number' && Number.isInteger(value["catalogVersion"])) && (typeof value["definitionSchemaVersion"] === 'number' && Number.isInteger(value["definitionSchemaVersion"])) && (Array.isArray(value["nodeTypes"]) && value["nodeTypes"].every(item18 => isWorkflowNodeTypeResponse(item18)));
}

export function readWorkflowNodeTypeResponse(value: unknown): WorkflowNodeTypeResponse {
  if (!(isWorkflowNodeTypeResponse(value))) {
    throw new Error('client.invalid_workflow_node_type_response');
  }
  return value;
}

function isWorkflowNodeTypeResponse(value: unknown): value is WorkflowNodeTypeResponse {
  return isRecord(value) && (typeof value["designable"] === 'boolean') && (typeof value["executable"] === 'boolean') && (typeof value["nodeSchemaVersion"] === 'number' && Number.isInteger(value["nodeSchemaVersion"])) && (typeof value["nodeTypeKey"] === 'string') && (typeof value["publishable"] === 'boolean') && (typeof value["supportsFieldPolicies"] === 'boolean');
}

export function readWorkflowRecipientCandidatePageResponse(value: unknown): WorkflowRecipientCandidatePageResponse {
  if (!(isWorkflowRecipientCandidatePageResponse(value))) {
    throw new Error('client.invalid_workflow_recipient_candidate_page_response');
  }
  return value;
}

function isWorkflowRecipientCandidatePageResponse(value: unknown): value is WorkflowRecipientCandidatePageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWorkflowRecipientCandidateResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
}

export function readWorkflowRecipientCandidateResponse(value: unknown): WorkflowRecipientCandidateResponse {
  if (!(isWorkflowRecipientCandidateResponse(value))) {
    throw new Error('client.invalid_workflow_recipient_candidate_response');
  }
  return value;
}

function isWorkflowRecipientCandidateResponse(value: unknown): value is WorkflowRecipientCandidateResponse {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["username"] === 'string');
}

export function readWorkflowRecoveryTaskResponse(value: unknown): WorkflowRecoveryTaskResponse {
  if (!(isWorkflowRecoveryTaskResponse(value))) {
    throw new Error('client.invalid_workflow_recovery_task_response');
  }
  return value;
}

function isWorkflowRecoveryTaskResponse(value: unknown): value is WorkflowRecoveryTaskResponse {
  return isRecord(value) && (typeof value["attemptCount"] === 'number' && Number.isInteger(value["attemptCount"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["kindKey"] === 'string') && ((value["lastError"] === null) || (typeof value["lastError"] === 'string')) && ((value["leaseExpiresAtUtc"] === null) || (typeof value["leaseExpiresAtUtc"] === 'string')) && (typeof value["leaseGeneration"] === 'number' && Number.isInteger(value["leaseGeneration"])) && ((value["leaseOwnerKey"] === null) || (typeof value["leaseOwnerKey"] === 'string')) && ((value["nextAttemptAtUtc"] === null) || (typeof value["nextAttemptAtUtc"] === 'string')) && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && ((value["stepId"] === null) || (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"]))) && (typeof value["updatedAtUtc"] === 'string');
}

export function readWorkflowTodoDetailResponse(value: unknown): WorkflowTodoDetailResponse {
  if (!(isWorkflowTodoDetailResponse(value))) {
    throw new Error('client.invalid_workflow_todo_detail_response');
  }
  return value;
}

function isWorkflowTodoDetailResponse(value: unknown): value is WorkflowTodoDetailResponse {
  return isRecord(value) && (typeof value["approvalModeKey"] === 'string') && (typeof value["approvedCount"] === 'number' && Number.isInteger(value["approvedCount"])) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (isRecord(value["fieldPolicies"])) && (isJsonElement(value["formSchema"])) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["pendingCount"] === 'number' && Number.isInteger(value["pendingCount"])) && (typeof value["rejectedCount"] === 'number' && Number.isInteger(value["rejectedCount"])) && (typeof value["requiredApprovalCount"] === 'number' && Number.isInteger(value["requiredApprovalCount"])) && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"])) && (isJsonElement(value["submission"])) && (typeof value["submissionRevision"] === 'number' && Number.isInteger(value["submissionRevision"]));
}

export function readWorkflowTodoResponse(value: unknown): WorkflowTodoResponse {
  if (!(isWorkflowTodoResponse(value))) {
    throw new Error('client.invalid_workflow_todo_response');
  }
  return value;
}

function isWorkflowTodoResponse(value: unknown): value is WorkflowTodoResponse {
  return isRecord(value) && (typeof value["arrivedAtUtc"] === 'string') && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && ((value["resultActionKey"] === null) || (typeof value["resultActionKey"] === 'string')) && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"]));
}

export function readWorkflowTodoReturnTargetResponse(value: unknown): WorkflowTodoReturnTargetResponse {
  if (!(isWorkflowTodoReturnTargetResponse(value))) {
    throw new Error('client.invalid_workflow_todo_return_target_response');
  }
  return value;
}

function isWorkflowTodoReturnTargetResponse(value: unknown): value is WorkflowTodoReturnTargetResponse {
  return isRecord(value) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (typeof value["completedAtUtc"] === 'string') && (typeof value["nodeKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"]));
}

export function readWorkflowTodoRuntimeResponse(value: unknown): WorkflowTodoRuntimeResponse {
  if (!(isWorkflowTodoRuntimeResponse(value))) {
    throw new Error('client.invalid_workflow_todo_runtime_response');
  }
  return value;
}

function isWorkflowTodoRuntimeResponse(value: unknown): value is WorkflowTodoRuntimeResponse {
  return isRecord(value) && (typeof value["approvalModeKey"] === 'string') && (typeof value["approvedCount"] === 'number' && Number.isInteger(value["approvedCount"])) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (isRecord(value["fieldPolicies"])) && (isJsonElement(value["formSchema"])) && (typeof value["formSchemaHash"] === 'string') && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["pendingCount"] === 'number' && Number.isInteger(value["pendingCount"])) && (typeof value["rejectedCount"] === 'number' && Number.isInteger(value["rejectedCount"])) && (typeof value["requiredApprovalCount"] === 'number' && Number.isInteger(value["requiredApprovalCount"])) && (typeof value["revision"] === 'number' && Number.isInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"])) && (isJsonElement(value["submission"])) && (typeof value["submissionRevision"] === 'number' && Number.isInteger(value["submissionRevision"]));
}

export function readCodeGenerationListCatalogTablesResponse(value: unknown): Array<CodeGenerationCatalogTableResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isCodeGenerationCatalogTableResponse(item5)))) {
    throw new Error('client.invalid_code_generation_list_catalog_tables_response');
  }
  return value as Array<CodeGenerationCatalogTableResponse>;
}

export function readDocumentHostDeleteCategoryResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_document_host_delete_category_response');
  }
  return value as boolean;
}

export function readDocumentHostDeleteItemResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_document_host_delete_item_response');
  }
  return value as boolean;
}

export function readDocumentHostDeleteTagResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_document_host_delete_tag_response');
  }
  return value as boolean;
}

export function readDocumentHostListCategoriesResponse(value: unknown): Array<HostDocumentCategoryResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostDocumentCategoryResponse(item5)))) {
    throw new Error('client.invalid_document_host_list_categories_response');
  }
  return value as Array<HostDocumentCategoryResponse>;
}

export function readDocumentHostListDocumentPermissionsResponse(value: unknown): Array<HostDocumentPermissionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostDocumentPermissionResponse(item5)))) {
    throw new Error('client.invalid_document_host_list_document_permissions_response');
  }
  return value as Array<HostDocumentPermissionResponse>;
}

export function readDocumentHostListItemVersionsResponse(value: unknown): Array<HostDocumentVersionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostDocumentVersionResponse(item5)))) {
    throw new Error('client.invalid_document_host_list_item_versions_response');
  }
  return value as Array<HostDocumentVersionResponse>;
}

export function readDocumentHostListTagsResponse(value: unknown): Array<HostDocumentTagResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostDocumentTagResponse(item5)))) {
    throw new Error('client.invalid_document_host_list_tags_response');
  }
  return value as Array<HostDocumentTagResponse>;
}

export function readDocumentHostPurgeRecycleBinItemResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_document_host_purge_recycle_bin_item_response');
  }
  return value as boolean;
}

export function readDocumentHostSetDocumentPermissionsResponse(value: unknown): Array<HostDocumentPermissionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostDocumentPermissionResponse(item5)))) {
    throw new Error('client.invalid_document_host_set_document_permissions_response');
  }
  return value as Array<HostDocumentPermissionResponse>;
}

export function readIdentityExportHostUsersResponse(value: unknown): Array<HostUserResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostUserResponse(item5)))) {
    throw new Error('client.invalid_identity_export_host_users_response');
  }
  return value as Array<HostUserResponse>;
}

export function readIdentityGetAuthorizationTreeResponse(value: unknown): Array<AuthorizationTreeModuleResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isAuthorizationTreeModuleResponse(item5)))) {
    throw new Error('client.invalid_identity_get_authorization_tree_response');
  }
  return value as Array<AuthorizationTreeModuleResponse>;
}

export function readIdentityListAllHostMenusResponse(value: unknown): Array<HostMenuResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostMenuResponse(item5)))) {
    throw new Error('client.invalid_identity_list_all_host_menus_response');
  }
  return value as Array<HostMenuResponse>;
}

export function readIdentityListFieldProjectionCatalogResponse(value: unknown): Array<FieldProjectionResourceDefinition> {
  if (!(Array.isArray(value) && value.every(item5 => isFieldProjectionResourceDefinition(item5)))) {
    throw new Error('client.invalid_identity_list_field_projection_catalog_response');
  }
  return value as Array<FieldProjectionResourceDefinition>;
}

export function readIdentityListHostMenuPermissionOptionsResponse(value: unknown): Array<HostMenuPermissionOptionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostMenuPermissionOptionResponse(item5)))) {
    throw new Error('client.invalid_identity_list_host_menu_permission_options_response');
  }
  return value as Array<HostMenuPermissionOptionResponse>;
}

export function readIdentityListHostModulesResponse(value: unknown): Array<ModuleCatalogEntryResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isModuleCatalogEntryResponse(item5)))) {
    throw new Error('client.invalid_identity_list_host_modules_response');
  }
  return value as Array<ModuleCatalogEntryResponse>;
}

export function readIdentityListSuperAdministratorAuditsResponse(value: unknown): Array<SuperAdministratorAuditResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isSuperAdministratorAuditResponse(item5)))) {
    throw new Error('client.invalid_identity_list_super_administrator_audits_response');
  }
  return value as Array<SuperAdministratorAuditResponse>;
}

export function readIdentityListSuperAdministratorsResponse(value: unknown): Array<SuperAdministratorResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isSuperAdministratorResponse(item5)))) {
    throw new Error('client.invalid_identity_list_super_administrators_response');
  }
  return value as Array<SuperAdministratorResponse>;
}

export function readJobsListHostJobGroupsResponse(value: unknown): Array<HostJobGroupResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostJobGroupResponse(item5)))) {
    throw new Error('client.invalid_jobs_list_host_job_groups_response');
  }
  return value as Array<HostJobGroupResponse>;
}

export function readJobsListHostJobScheduleDefinitionOptionsResponse(value: unknown): Array<HostJobScheduleDefinitionOptionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostJobScheduleDefinitionOptionResponse(item5)))) {
    throw new Error('client.invalid_jobs_list_host_job_schedule_definition_options_response');
  }
  return value as Array<HostJobScheduleDefinitionOptionResponse>;
}

export function readNotificationsListMyRecipientEndpointsResponse(value: unknown): Array<RecipientEndpointResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isRecipientEndpointResponse(item5)))) {
    throw new Error('client.invalid_notifications_list_my_recipient_endpoints_response');
  }
  return value as Array<RecipientEndpointResponse>;
}

export function readNotificationsListProviderTypesResponse(value: unknown): Array<NotificationProviderTypeDescriptor> {
  if (!(Array.isArray(value) && value.every(item5 => isNotificationProviderTypeDescriptor(item5)))) {
    throw new Error('client.invalid_notifications_list_provider_types_response');
  }
  return value as Array<NotificationProviderTypeDescriptor>;
}

export function readObservabilityListLogFilesResponse(value: unknown): Array<LogFileSummary> {
  if (!(Array.isArray(value) && value.every(item5 => isLogFileSummary(item5)))) {
    throw new Error('client.invalid_observability_list_log_files_response');
  }
  return value as Array<LogFileSummary>;
}

export function readSettingsBatchUpdateHostConfigEntryValuesResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_settings_batch_update_host_config_entry_values_response');
  }
  return value as boolean;
}

export function readSettingsListAllHostConfigEntriesResponse(value: unknown): Array<ConfigEntryResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isConfigEntryResponse(item5)))) {
    throw new Error('client.invalid_settings_list_all_host_config_entries_response');
  }
  return value as Array<ConfigEntryResponse>;
}

export function readSettingsListAllHostDictTypesResponse(value: unknown): Array<DictTypeResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isDictTypeResponse(item5)))) {
    throw new Error('client.invalid_settings_list_all_host_dict_types_response');
  }
  return value as Array<DictTypeResponse>;
}

export function readSettingsListAllTenantDictTypesResponse(value: unknown): Array<DictTypeResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isDictTypeResponse(item5)))) {
    throw new Error('client.invalid_settings_list_all_tenant_dict_types_response');
  }
  return value as Array<DictTypeResponse>;
}

export function readSettingsListHostConfigEntryGroupsResponse(value: unknown): Array<string> {
  if (!(Array.isArray(value) && value.every(item5 => typeof item5 === 'string'))) {
    throw new Error('client.invalid_settings_list_host_config_entry_groups_response');
  }
  return value as Array<string>;
}

export function readSettingsListHostDictItemsByTypeCodeResponse(value: unknown): Array<DictItemResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isDictItemResponse(item5)))) {
    throw new Error('client.invalid_settings_list_host_dict_items_by_type_code_response');
  }
  return value as Array<DictItemResponse>;
}

export function readSettingsListHostEnumCatalogsResponse(value: unknown): Array<EnumCatalogSummary> {
  if (!(Array.isArray(value) && value.every(item5 => isEnumCatalogSummary(item5)))) {
    throw new Error('client.invalid_settings_list_host_enum_catalogs_response');
  }
  return value as Array<EnumCatalogSummary>;
}

export function readSettingsListTenantDictItemsByTypeCodeResponse(value: unknown): Array<DictItemResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isDictItemResponse(item5)))) {
    throw new Error('client.invalid_settings_list_tenant_dict_items_by_type_code_response');
  }
  return value as Array<DictItemResponse>;
}

export function readWorkflowListDefinitionsResponse(value: unknown): Array<WorkflowDefinitionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowDefinitionResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_definitions_response');
  }
  return value as Array<WorkflowDefinitionResponse>;
}

export function readWorkflowListDefinitionVersionsResponse(value: unknown): Array<WorkflowDefinitionVersionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowDefinitionVersionResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_definition_versions_response');
  }
  return value as Array<WorkflowDefinitionVersionResponse>;
}

export function readWorkflowListFormsResponse(value: unknown): Array<WorkflowFormResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowFormResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_forms_response');
  }
  return value as Array<WorkflowFormResponse>;
}

export function readWorkflowListInstanceExecutionLogsResponse(value: unknown): Array<WorkflowExecutionLogResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowExecutionLogResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_instance_execution_logs_response');
  }
  return value as Array<WorkflowExecutionLogResponse>;
}

export function readWorkflowListMyCcResponse(value: unknown): Array<WorkflowCcResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowCcResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_my_cc_response');
  }
  return value as Array<WorkflowCcResponse>;
}

export function readWorkflowListMyTodosResponse(value: unknown): Array<WorkflowTodoResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowTodoResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_my_todos_response');
  }
  return value as Array<WorkflowTodoResponse>;
}

export function readWorkflowListTodoReturnTargetsResponse(value: unknown): Array<WorkflowTodoReturnTargetResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowTodoReturnTargetResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_todo_return_targets_response');
  }
  return value as Array<WorkflowTodoReturnTargetResponse>;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isJsonValue(value: unknown): boolean {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') {
    return true;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value);
  }
  if (Array.isArray(value)) {
    return value.every(isJsonValue);
  }
  return isRecord(value) && Object.values(value).every(isJsonValue);
}
