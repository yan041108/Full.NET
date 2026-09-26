// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 运行时响应守卫。

import type {
  AccessHostDocumentShareRequest,
  AccessLogCursorPageResponse,
  AccessLogResponse,
  ActWorkflowTodoRequest,
  AddHostDocumentVersionRequest,
  AdministrativeRegionChildResponse,
  AdministrativeRegionDatasetManifestResponse,
  AdministrativeRegionResponse,
  AdministrativeRegionTreeNodeResponse,
  AiAgentApprovalResponse,
  AiAgentDelegationResponse,
  AiAgentRunResponse,
  AiAgentToolCallListItem,
  AiAgentToolCatalogItem,
  AiChatMessageResponse,
  AiChatSessionListItem,
  AiChatSessionResponse,
  AiMcpRemoteConnectionListItem,
  AiMcpRemoteConnectionResponse,
  AiMcpRemoteDiscoveredToolItem,
  AiMcpRemoteToolApprovalItem,
  AiModelConfigListItem,
  AiModelConfigResponse,
  AiTenantQuotaListItem,
  AiTenantQuotaResponse,
  ApproveAiMcpRemoteToolRequest,
  AssignHostTenantPackageRequest,
  AssignOrganizationPositionLevelRequest,
  AssignOrganizationPositionUnitRequest,
  AuthenticationEventCursorPage,
  AuthenticationEventResponse,
  AuthorizationTreeActionResponse,
  AuthorizationTreeModuleResponse,
  AuthorizationTreePageResponse,
  BatchChangeHostJobScheduleStateItem,
  BatchChangeHostJobScheduleStateRequest,
  BatchChangeHostJobScheduleStateResponse,
  BatchChangeHostJobScheduleStateResultItem,
  BatchCreateHostDocumentShareItem,
  BatchCreateHostDocumentSharesRequest,
  BatchCreateHostDocumentSharesResponse,
  BatchDeleteConfigEntriesRequest,
  BatchDeleteHostFileItem,
  BatchDeleteHostFilesRequest,
  BatchDeleteHostFilesResponse,
  BatchHostUserIdsRequest,
  BatchHostUserStatusItem,
  BatchHostUserStatusResponse,
  BatchUpdateConfigValuesRequest,
  BatchUploadHostFileItem,
  BatchUploadHostFilesResponse,
  BeginTotpEnrollmentResponse,
  CacheInvalidationOperationSummary,
  CacheInvalidationParameterSummary,
  CacheInvalidationRequest,
  CacheInvalidationResult,
  CachePolicySummary,
  CancelDataApprovalRequestBody,
  CancelWorkflowInstanceRequest,
  ChangeHostJobScheduleStateRequest,
  ChangePasswordRequest,
  ChangePersonalScheduleRequest,
  ChangeSerialNumberRuleStatusRequest,
  CodeGenerationCatalogColumnListResponse,
  CodeGenerationCatalogColumnSyncRequest,
  CodeGenerationCatalogColumnSyncResponse,
  CodeGenerationCatalogMetadataColumnResponse,
  CodeGenerationCatalogMetadataResponse,
  CodeGenerationCatalogMigrationDraftRequest,
  CodeGenerationCatalogMigrationDraftResponse,
  CodeGenerationCatalogObjectResponse,
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
  ConfirmOcrIdCardTaskRequest,
  ConfirmTotpEnrollmentRequest,
  CopyHostRoleRequest,
  CreateAdministrativeRegionRequest,
  CreateAiAgentApprovalRequest,
  CreateAiAgentApprovalResponse,
  CreateAiAgentDelegationRequest,
  CreateAiAgentDelegationResponse,
  CreateAiAgentRunRequest,
  CreateAiAgentRunResponse,
  CreateAiChatSessionRequest,
  CreateAiMcpRemoteConnectionRequest,
  CreateAiModelConfigRequest,
  CreateCodeGenerationTemplateRequest,
  CreateConfigEntryRequest,
  CreateDataApprovalRequestBody,
  CreateDictItemRequest,
  CreateDictTypeRequest,
  CreateEnterpriseRequestRequest,
  CreateGoViewProjectRequest,
  CreateHostAnnouncementRequest,
  CreateHostApiKeyRequest,
  CreateHostApiKeyResponse,
  CreateHostDocumentCategoryRequest,
  CreateHostDocumentItemRequest,
  CreateHostDocumentPreviewTaskRequest,
  CreateHostDocumentShareRequest,
  CreateHostDocumentTagRequest,
  CreateHostFolderRequest,
  CreateHostJobDefinitionRequest,
  CreateHostJobScheduleRequest,
  CreateHostMenuRequest,
  CreateHostReleaseNoteRequest,
  CreateHostRoleRequest,
  CreateHostTenantPackageRequest,
  CreateHostUserRequest,
  CreateK3CloudConnectionConfigRequest,
  CreateK3CloudDocumentSyncRequest,
  CreateMyRecipientEndpointRequest,
  CreateNotificationBindingRequest,
  CreateNotificationProviderProfileRequest,
  CreateNotificationTemplateRequest,
  CreateOcrIdCardTaskRequest,
  CreateOrganizationPositionLevelRequest,
  CreateOrganizationPositionRequest,
  CreateOrganizationUnitRequest,
  CreateOrganizationUserPositionRequest,
  CreateOrganizationUserUnitRequest,
  CreatePaymentMerchantConfigRequest,
  CreatePaymentOrderRequest,
  CreatePaymentRefundRequest,
  CreatePersonalScheduleRequest,
  CreatePrintingTemplateRequest,
  CreateReportingDataSourceRequest,
  CreateReportingDefinitionRequest,
  CreateReportingExportTaskRequest,
  CreateReportingGroupRequest,
  CreateSerialNumberRuleRequest,
  CreateWorkflowDefinitionRequest,
  CreateWorkflowFormRequest,
  CurrentUserResponse,
  DataApprovalRequestResponse,
  DataApprovalScenarioResponse,
  DecideAiAgentApprovalRequest,
  DeleteAdministrativeRegionRequest,
  DeleteCodeGenerationTemplateRequest,
  DeleteConfigEntryRequest,
  DeleteDictItemRequest,
  DeleteDictTypeRequest,
  DeleteEnterpriseRequestRequest,
  DeleteHostDocumentCategoryRequest,
  DeleteHostDocumentItemRequest,
  DeleteHostDocumentTagRequest,
  DeleteHostDocumentVersionRequest,
  DeleteHostFolderRequest,
  DeleteHostJobDefinitionRequest,
  DeleteHostReleaseNoteRequest,
  DiagnosticPolicyResponse,
  DiagnosticPolicyRuleRequest,
  DiagnosticPolicyRuleResponse,
  DictItemResponse,
  DictTypeResponse,
  DisableHostJobDefinitionRequest,
  EnterpriseRequestResponse,
  EnumCatalogDetail,
  EnumCatalogDictGenerationItemPreview,
  EnumCatalogDictGenerationPreview,
  EnumCatalogDictGenerationResult,
  EnumCatalogDictGenerationUnmanagedItem,
  EnumCatalogMember,
  EnumCatalogSummary,
  ExceptionLogResponse,
  ExecuteReportingDefinitionRequest,
  FieldProjectionDefaultVisibility,
  FieldProjectionFieldDefinition,
  FieldProjectionResourceDefinition,
  FieldProjectionSensitivity,
  GoViewProjectPreviewResponse,
  GoViewProjectResponse,
  GoViewProjectVersionResponse,
  GrantSuperAdministratorRequest,
  HostAnnouncementReadReceiptResponse,
  HostAnnouncementReadStatsResponse,
  HostAnnouncementResponse,
  HostAnnouncementTargetOrganization,
  HostAnnouncementUnreadCountResponse,
  HostApiKeyResponse,
  HostDashboardActivityResponse,
  HostDashboardBusinessEntryResponse,
  HostDashboardSummaryResponse,
  HostDashboardTrafficTrendBucketResponse,
  HostDashboardTrafficTrendResponse,
  HostDocumentAccessLogResponse,
  HostDocumentCategoryResponse,
  HostDocumentItemResponse,
  HostDocumentPermissionEntry,
  HostDocumentPermissionResponse,
  HostDocumentPreviewTaskResponse,
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
  HostDocumentVersionRetentionSettingsResponse,
  HostFileReferenceClaimResponse,
  HostFileResponse,
  HostFolderResponse,
  HostFolderTreeNode,
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
  HostReleaseNoteResponse,
  HostRoleDataScopeResponse,
  HostRoleFieldGrantsResponse,
  HostRoleMemberResponse,
  HostRoleMembersAssignmentResponse,
  HostRoleMembersPageResponse,
  HostRoleResponse,
  HostTenantAdministratorsPageResponse,
  HostTenantMemberResponse,
  HostTenantMembersPageResponse,
  HostUserManagementOrganizationReferenceResponse,
  HostUserProfileResponse,
  HostUserProfileWriteRequest,
  HostUserProjectedFieldsResponse,
  HostUserResponse,
  HostUserRolesResponse,
  HttpJobArgs,
  HttpJobSecretHeaderRef,
  IdentitySessionLoginPolicy,
  IdentitySessionPolicyResponse,
  IFormFile,
  IFormFileCollection,
  ImportAdministrativeRegionAddedSummary,
  ImportAdministrativeRegionItem,
  ImportAdministrativeRegionRemovedSummary,
  ImportAdministrativeRegionsApplyResponse,
  ImportAdministrativeRegionsPreviewResponse,
  ImportAdministrativeRegionsRequest,
  ImportAdministrativeRegionUpdatedSummary,
  ImportExportTaskDetailResponse,
  ImportExportTaskResponse,
  ImportHostUserRowResult,
  ImportHostUsersRequest,
  ImportHostUsersResponse,
  ImportOrganizationPositionRow,
  ImportOrganizationPositionRowResult,
  ImportOrganizationPositionsRequest,
  ImportOrganizationPositionsResponse,
  InboxMessageResponse,
  InboxUnreadCountResponse,
  JsonElement,
  K3CloudConnectionConfigResponse,
  K3CloudDocumentSyncResponse,
  LocalePreferenceResponse,
  LogFileSummary,
  LogFileTail,
  LoginRequest,
  ModuleCatalogEntryResponse,
  ModuleSelectionAnalysisResponse,
  ModuleSelectionIssueResponse,
  ModuleSelectionModuleStateResponse,
  ModuleSelectionValidateRequest,
  MyReleaseNoteResponse,
  NotificationBindingResponse,
  NotificationBindingTargetInput,
  NotificationDeliveryAttemptResponse,
  NotificationDeliveryReceiptResponse,
  NotificationDeliveryResponse,
  NotificationProviderConfigField,
  NotificationProviderProfileResponse,
  NotificationProviderTypeDescriptor,
  NotificationTemplateBody,
  NotificationTemplateParameterDefinition,
  NotificationTemplateParameterSchema,
  NotificationTemplateResponse,
  OcrIdCardTaskResponse,
  OcrProviderConfigResponse,
  OperationLogResponse,
  OrganizationAssignableUserResponse,
  OrganizationPositionLevelResponse,
  OrganizationPositionResponse,
  OrganizationUnitResponse,
  OrganizationUserPositionResponse,
  OrganizationUserUnitResponse,
  OutboundCallLogResponse,
  PagedResultOfAccessLogResponse,
  PagedResultOfAdministrativeRegionResponse,
  PagedResultOfAiAgentToolCallListItem,
  PagedResultOfAiChatSessionListItem,
  PagedResultOfAiModelConfigListItem,
  PagedResultOfAiTenantQuotaListItem,
  PagedResultOfCodeGenerationRunResponse,
  PagedResultOfCodeGenerationTemplateResponse,
  PagedResultOfConfigEntryResponse,
  PagedResultOfDataApprovalRequestResponse,
  PagedResultOfDictItemResponse,
  PagedResultOfDictTypeResponse,
  PagedResultOfEnterpriseRequestResponse,
  PagedResultOfExceptionLogResponse,
  PagedResultOfHostAnnouncementReadReceiptResponse,
  PagedResultOfHostAnnouncementResponse,
  PagedResultOfHostApiKeyResponse,
  PagedResultOfHostDocumentAccessLogResponse,
  PagedResultOfHostDocumentItemResponse,
  PagedResultOfHostDocumentPreviewTaskResponse,
  PagedResultOfHostDocumentShareResponse,
  PagedResultOfHostFileReferenceClaimResponse,
  PagedResultOfHostFileResponse,
  PagedResultOfHostJobDefinitionResponse,
  PagedResultOfHostJobExecutionResponse,
  PagedResultOfHostJobScheduleResponse,
  PagedResultOfHostMenuResponse,
  PagedResultOfHostOnlineSessionResponse,
  PagedResultOfHostReleaseNoteResponse,
  PagedResultOfHostRoleResponse,
  PagedResultOfHostUserResponse,
  PagedResultOfImportExportTaskResponse,
  PagedResultOfInboxMessageResponse,
  PagedResultOfK3CloudDocumentSyncResponse,
  PagedResultOfMyReleaseNoteResponse,
  PagedResultOfNotificationBindingResponse,
  PagedResultOfNotificationDeliveryResponse,
  PagedResultOfNotificationProviderProfileResponse,
  PagedResultOfNotificationTemplateResponse,
  PagedResultOfOcrIdCardTaskResponse,
  PagedResultOfOperationLogResponse,
  PagedResultOfOrganizationAssignableUserResponse,
  PagedResultOfOrganizationPositionLevelResponse,
  PagedResultOfOrganizationPositionResponse,
  PagedResultOfOrganizationUnitResponse,
  PagedResultOfOrganizationUserPositionResponse,
  PagedResultOfOrganizationUserUnitResponse,
  PagedResultOfOutboundCallLogResponse,
  PagedResultOfPaymentMerchantConfigListItem,
  PagedResultOfPaymentOrderListItem,
  PagedResultOfPaymentRefundListItem,
  PagedResultOfPersonalScheduleResponse,
  PagedResultOfReceivedHostAnnouncementListItemResponse,
  PagedResultOfReportingDataSourceListItem,
  PagedResultOfReportingExportTaskResponse,
  PagedResultOfSerialNumberRuleResponse,
  PagedResultOfTenantPackageSummary,
  PagedResultOfTenantSummary,
  PagedResultOfWorkflowInstanceListItemResponse,
  PagedResultOfWorkflowRecoveryTaskResponse,
  PagedResultOfWorkflowTodoListItemResponse,
  PauseWorkflowInstanceRequest,
  PaymentMerchantConfigListItem,
  PaymentMerchantConfigResponse,
  PaymentOrderListItem,
  PaymentOrderResponse,
  PaymentRefundListItem,
  PaymentRefundResponse,
  PersonalScheduleResponse,
  PreviewGoViewProjectRequest,
  PreviewPrintingTemplateRequest,
  PreviewSerialNumberRequest,
  PreviewWorkflowAssigneeRequest,
  PrintingFormFieldDefinition,
  PrintingFormSchemaDefinition,
  PrintingTemplatePreviewResponse,
  PrintingTemplateResponse,
  PrintingTemplateVersionResponse,
  ProblemDetails,
  ProvisionTenantRequest,
  PublishGoViewProjectRequest,
  PublishHostAnnouncementRequest,
  PublishHostReleaseNoteRequest,
  PublishNotificationBindingRequest,
  PublishNotificationProviderProfileRequest,
  PublishNotificationTemplateRequest,
  PublishPrintingTemplateRequest,
  PublishReportingDefinitionRequest,
  PublishWorkflowDefinitionRequest,
  PublishWorkflowFormRequest,
  ReassignWorkflowInstanceRequest,
  ReceivedHostAnnouncementDetailResponse,
  ReceivedHostAnnouncementListItemResponse,
  RecipientEndpointResponse,
  ReconcileWorkflowRecoveryTaskRequest,
  RecoverWorkflowInstanceRequest,
  ReplaceHostRoleFieldGrantsRequest,
  ReplaceHostRoleMembersRequest,
  ReplaceHostRolePermissionsRequest,
  ReplaceHostUserRolesRequest,
  ReportingDataSourceListItem,
  ReportingDataSourceResponse,
  ReportingDefinitionResponse,
  ReportingDefinitionVersionResponse,
  ReportingExecutionColumnDefinition,
  ReportingExecutionPageResponse,
  ReportingExecutionParameterValue,
  ReportingExecutionRow,
  ReportingExportTaskDetailResponse,
  ReportingExportTaskResponse,
  ReportingGroupResponse,
  ReportingParameterSchemaEntry,
  ReportingQueryPortDefinition,
  ReportingQueryPortParameterDefinition,
  ResetHostUserPasswordRequest,
  RestoreDiagnosticPolicyRequest,
  RestoreHostDocumentItemRequest,
  ResumeWorkflowInstanceRequest,
  RetractHostReleaseNoteRequest,
  RetryDataApprovalRequestBody,
  RetryNotificationDeliveryRequest,
  RetryWorkflowRecoveryTaskRequest,
  ReturnWorkflowTodoRequest,
  RevealHostUserProfileFieldsRequest,
  RevealHostUserProfileFieldsResponse,
  RevokeAiAgentDelegationRequest,
  RevokeAllHostUserSessionsResponse,
  RevokeSuperAdministratorRequest,
  RollbackHostDocumentVersionRequest,
  SelfServiceProfileResponse,
  SendHostInboxMessageRequest,
  SendRecipientEndpointVerificationResponse,
  SerialNumberPreviewResponse,
  SerialNumberResetInterval,
  SerialNumberRuleResponse,
  SerialNumberRuleScope,
  SerialRuleDisableApprovalPreviewResponse,
  SerialRuleDisableApprovalSubmissionResponse,
  SerialRuleFieldChange,
  SerialRuleUpdateApprovalPreviewResponse,
  SerialRuleUpdateApprovalSubmissionResponse,
  ServerInstanceCatalogEntry,
  ServerRuntimeMetric,
  ServerRuntimeSnapshot,
  SetHostDocumentPermissionsRequest,
  SetNotificationProviderProfileEnabledRequest,
  SetPersonalScheduleStatusRequest,
  SetWorkflowDefinitionStatusRequest,
  SetWorkflowFormStatusRequest,
  StartWorkflowInstanceRequest,
  StaticImportRowPreviewResult,
  StaticImportSchemaDefinition,
  StaticImportWorksheetDefinition,
  Stream,
  StreamAiChatMessageRequest,
  SubmitSerialRuleDisableApprovalRequest,
  SubmitSerialRuleUpdateApprovalRequest,
  SuperAdministratorAuditResponse,
  SuperAdministratorChangeResponse,
  SuperAdministratorResponse,
  TenantBrandingResponse,
  TenantPackageSummary,
  TenantRuntimeBrandingResponse,
  TenantSummary,
  TestAiModelConfigResult,
  TestAiModelEmbeddingRequest,
  TestAiModelEmbeddingResult,
  TestK3CloudConnectionConfigResult,
  TestOcrProviderConfigResult,
  TestReportingDataSourceResult,
  TokenResponse,
  TotpEnrollmentStatusResponse,
  UpdateAdministrativeRegionRequest,
  UpdateAiChatSessionRequest,
  UpdateAiMcpRemoteConnectionRequest,
  UpdateAiModelConfigRequest,
  UpdateAiTenantQuotaRequest,
  UpdateCodeGenerationTemplateRequest,
  UpdateConfigEntryRequest,
  UpdateDataApprovalScenarioBindingBody,
  UpdateDiagnosticPolicyRequest,
  UpdateDictItemRequest,
  UpdateDictTypeRequest,
  UpdateEnterpriseRequestRequest,
  UpdateGoViewProjectRequest,
  UpdateHostAnnouncementRequest,
  UpdateHostDocumentCategoryRequest,
  UpdateHostDocumentItemRequest,
  UpdateHostDocumentShareStatusRequest,
  UpdateHostDocumentTagRequest,
  UpdateHostDocumentVersionRetentionRequest,
  UpdateHostFileMetadataRequest,
  UpdateHostFolderRequest,
  UpdateHostJobDefinitionRequest,
  UpdateHostJobScheduleRequest,
  UpdateHostMenuRequest,
  UpdateHostReleaseNoteRequest,
  UpdateHostRoleDataScopeRequest,
  UpdateHostRoleRequest,
  UpdateHostTenantPackageRequest,
  UpdateHostTenantRequest,
  UpdateHostUserRequest,
  UpdateK3CloudConnectionConfigRequest,
  UpdateLocaleRequest,
  UpdateNotificationBindingRequest,
  UpdateNotificationProviderProfileRequest,
  UpdateNotificationTemplateRequest,
  UpdateOcrProviderConfigRequest,
  UpdateOrganizationPositionLevelRequest,
  UpdateOrganizationPositionRequest,
  UpdateOrganizationUnitRequest,
  UpdateOrganizationUserPositionRequest,
  UpdateOrganizationUserUnitRequest,
  UpdatePaymentMerchantConfigRequest,
  UpdatePersonalScheduleRequest,
  UpdatePrintingTemplateRequest,
  UpdateReportingDataSourceRequest,
  UpdateReportingDefinitionRequest,
  UpdateReportingGroupRequest,
  UpdateSelfServiceProfileRequest,
  UpdateSerialNumberRuleRequest,
  UpdateTenantBrandingRequest,
  UpdateWorkflowDefinitionDraftRequest,
  UpdateWorkflowFormDraftRequest,
  VerifyRecipientEndpointCodeRequest,
  WorkflowAssigneePreviewResponse,
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
  WorkflowGatewayJoinBranchResponse,
  WorkflowGatewayJoinResponse,
  WorkflowInstanceListItemResponse,
  WorkflowInstanceResponse,
  WorkflowNodeDraft,
  WorkflowNodeTypeCatalogResponse,
  WorkflowNodeTypeResponse,
  WorkflowRecipientCandidatePageResponse,
  WorkflowRecipientCandidateResponse,
  WorkflowRecoveryTaskResponse,
  WorkflowTodoDetailResponse,
  WorkflowTodoListItemResponse,
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
  const normalizedValue = normalizeAccessLogCursorPageResponseIntegerJson(value);
  if (!(isAccessLogCursorPageResponse(normalizedValue))) {
    throw new Error('client.invalid_access_log_cursor_page_response');
  }
  return normalizedValue;
}

function isAccessLogCursorPageResponse(value: unknown): value is AccessLogCursorPageResponse {
  return isRecord(value) && (typeof value["hasMore"] === 'boolean') && (Array.isArray(value["items"]) && value["items"].every(item14 => isAccessLogResponse(item14))) && ((value["nextCursor"] === null) || (typeof value["nextCursor"] === 'string'));
}

export function readAccessLogResponse(value: unknown): AccessLogResponse {
  const normalizedValue = normalizeAccessLogResponseIntegerJson(value);
  if (!(isAccessLogResponse(normalizedValue))) {
    throw new Error('client.invalid_access_log_response');
  }
  return normalizedValue;
}

function isAccessLogResponse(value: unknown): value is AccessLogResponse {
  return isRecord(value) && ((value["clientIpFingerprint"] === null) || (typeof value["clientIpFingerprint"] === 'string')) && (typeof value["durationMs"] === 'number' && Number.isSafeInteger(value["durationMs"])) && (typeof value["httpMethod"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isAuthenticated"] === 'boolean') && (typeof value["occurredAtUtc"] === 'string') && (typeof value["requestPath"] === 'string') && (typeof value["statusCode"] === 'number' && Number.isSafeInteger(value["statusCode"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readActWorkflowTodoRequest(value: unknown): ActWorkflowTodoRequest {
  const normalizedValue = normalizeActWorkflowTodoRequestIntegerJson(value);
  if (!(isActWorkflowTodoRequest(normalizedValue))) {
    throw new Error('client.invalid_act_workflow_todo_request');
  }
  return normalizedValue;
}

function isActWorkflowTodoRequest(value: unknown): value is ActWorkflowTodoRequest {
  return isRecord(value) && ((value["comment"] === null) || (typeof value["comment"] === 'string')) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (isJsonElement(value["fieldPatch"])) && (typeof value["idempotencyKey"] === 'string');
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

export function readAdministrativeRegionChildResponse(value: unknown): AdministrativeRegionChildResponse {
  const normalizedValue = normalizeAdministrativeRegionChildResponseIntegerJson(value);
  if (!(isAdministrativeRegionChildResponse(normalizedValue))) {
    throw new Error('client.invalid_administrative_region_child_response');
  }
  return normalizedValue;
}

function isAdministrativeRegionChildResponse(value: unknown): value is AdministrativeRegionChildResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["hasChildren"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"])));
}

export function readAdministrativeRegionDatasetManifestResponse(value: unknown): AdministrativeRegionDatasetManifestResponse {
  const normalizedValue = normalizeAdministrativeRegionDatasetManifestResponseIntegerJson(value);
  if (!(isAdministrativeRegionDatasetManifestResponse(normalizedValue))) {
    throw new Error('client.invalid_administrative_region_dataset_manifest_response');
  }
  return normalizedValue;
}

function isAdministrativeRegionDatasetManifestResponse(value: unknown): value is AdministrativeRegionDatasetManifestResponse {
  return isRecord(value) && (typeof value["appliedAtUtc"] === 'string') && (typeof value["appliedByUserId"] === 'string' && guidPattern.test(value["appliedByUserId"])) && (typeof value["datasetKey"] === 'string') && (typeof value["datasetVersion"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["recordCount"] === 'number' && Number.isSafeInteger(value["recordCount"])) && (typeof value["sourceDigest"] === 'string');
}

export function readAdministrativeRegionResponse(value: unknown): AdministrativeRegionResponse {
  const normalizedValue = normalizeAdministrativeRegionResponseIntegerJson(value);
  if (!(isAdministrativeRegionResponse(normalizedValue))) {
    throw new Error('client.invalid_administrative_region_response');
  }
  return normalizedValue;
}

function isAdministrativeRegionResponse(value: unknown): value is AdministrativeRegionResponse {
  return isRecord(value) && ((value["cityCode"] === null) || (typeof value["cityCode"] === 'string')) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latitude"] === null) || (typeof value["latitude"] === 'number' && Number.isFinite(value["latitude"])) || (typeof value["latitude"] === 'string')) && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && ((value["longitude"] === null) || (typeof value["longitude"] === 'number' && Number.isFinite(value["longitude"])) || (typeof value["longitude"] === 'string')) && ((value["mergerName"] === null) || (typeof value["mergerName"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && ((value["pinYin"] === null) || (typeof value["pinYin"] === 'string')) && ((value["regionType"] === null) || (typeof value["regionType"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["shortName"] === null) || (typeof value["shortName"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && ((value["zipCode"] === null) || (typeof value["zipCode"] === 'string'));
}

export function readAdministrativeRegionTreeNodeResponse(value: unknown): AdministrativeRegionTreeNodeResponse {
  const normalizedValue = normalizeAdministrativeRegionTreeNodeResponseIntegerJson(value);
  if (!(isAdministrativeRegionTreeNodeResponse(normalizedValue))) {
    throw new Error('client.invalid_administrative_region_tree_node_response');
  }
  return normalizedValue;
}

function isAdministrativeRegionTreeNodeResponse(value: unknown): value is AdministrativeRegionTreeNodeResponse {
  return isRecord(value) && (Array.isArray(value["children"]) && value["children"].every(item17 => isAdministrativeRegionTreeNodeResponse(item17))) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"])));
}

export function readAiAgentApprovalResponse(value: unknown): AiAgentApprovalResponse {
  const normalizedValue = normalizeAiAgentApprovalResponseIntegerJson(value);
  if (!(isAiAgentApprovalResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_agent_approval_response');
  }
  return normalizedValue;
}

function isAiAgentApprovalResponse(value: unknown): value is AiAgentApprovalResponse {
  return isRecord(value) && (typeof value["actionSummary"] === 'string') && (typeof value["argumentsHash"] === 'string') && (typeof value["changeSummary"] === 'string') && ((value["consumedAtUtc"] === null) || (typeof value["consumedAtUtc"] === 'string')) && ((value["costCeiling"] === null) || (typeof value["costCeiling"] === 'number' && Number.isFinite(value["costCeiling"])) || (typeof value["costCeiling"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["currency"] === null) || (typeof value["currency"] === 'string')) && (typeof value["decisionKey"] === 'string') && (typeof value["expiresAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["operationId"] === 'string' && guidPattern.test(value["operationId"])) && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"])) && (typeof value["scopeSummary"] === 'string') && (typeof value["targetSummary"] === 'string') && (typeof value["toolName"] === 'string') && (typeof value["toolVersion"] === 'number' && Number.isSafeInteger(value["toolVersion"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiAgentDelegationResponse(value: unknown): AiAgentDelegationResponse {
  const normalizedValue = normalizeAiAgentDelegationResponseIntegerJson(value);
  if (!(isAiAgentDelegationResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_agent_delegation_response');
  }
  return normalizedValue;
}

function isAiAgentDelegationResponse(value: unknown): value is AiAgentDelegationResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["expiresAtUtc"] === 'string') && (typeof value["granteeUserId"] === 'string' && guidPattern.test(value["granteeUserId"])) && (typeof value["grantorUserId"] === 'string' && guidPattern.test(value["grantorUserId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["permissionCode"] === null) || (typeof value["permissionCode"] === 'string')) && ((value["revokedAtUtc"] === null) || (typeof value["revokedAtUtc"] === 'string')) && ((value["toolName"] === null) || (typeof value["toolName"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiAgentRunResponse(value: unknown): AiAgentRunResponse {
  const normalizedValue = normalizeAiAgentRunResponseIntegerJson(value);
  if (!(isAiAgentRunResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_agent_run_response');
  }
  return normalizedValue;
}

function isAiAgentRunResponse(value: unknown): value is AiAgentRunResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["deadlineAtUtc"] === 'string') && (typeof value["definitionKey"] === 'string') && (typeof value["definitionVersion"] === 'number' && Number.isSafeInteger(value["definitionVersion"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["statusKey"] === 'string') && (typeof value["updatedAtUtc"] === 'string');
}

export function readAiAgentToolCallListItem(value: unknown): AiAgentToolCallListItem {
  const normalizedValue = normalizeAiAgentToolCallListItemIntegerJson(value);
  if (!(isAiAgentToolCallListItem(normalizedValue))) {
    throw new Error('client.invalid_ai_agent_tool_call_list_item');
  }
  return normalizedValue;
}

function isAiAgentToolCallListItem(value: unknown): value is AiAgentToolCallListItem {
  return isRecord(value) && (typeof value["actorUserId"] === 'string' && guidPattern.test(value["actorUserId"])) && ((value["approvalId"] === null) || (typeof value["approvalId"] === 'string' && guidPattern.test(value["approvalId"]))) && ((value["argumentsHash"] === null) || (typeof value["argumentsHash"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["durationMs"] === null) || (typeof value["durationMs"] === 'number' && Number.isSafeInteger(value["durationMs"]))) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["inputSummary"] === 'string') && ((value["outputSummary"] === null) || (typeof value["outputSummary"] === 'string')) && (typeof value["permissionCode"] === 'string') && ((value["runId"] === null) || (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]))) && (typeof value["statusKey"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["toolName"] === 'string') && ((value["traceId"] === null) || (typeof value["traceId"] === 'string'));
}

export function readAiAgentToolCatalogItem(value: unknown): AiAgentToolCatalogItem {
  if (!(isAiAgentToolCatalogItem(value))) {
    throw new Error('client.invalid_ai_agent_tool_catalog_item');
  }
  return value;
}

function isAiAgentToolCatalogItem(value: unknown): value is AiAgentToolCatalogItem {
  return isRecord(value) && (typeof value["description"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["inputSchemaJson"] === 'string') && (typeof value["isEnabled"] === 'boolean') && (typeof value["mcpExposureKey"] === 'string') && (typeof value["outputSchemaJson"] === 'string') && (typeof value["permissionCode"] === 'string') && (typeof value["sideEffectKey"] === 'string') && (typeof value["toolName"] === 'string');
}

export function readAiChatMessageResponse(value: unknown): AiChatMessageResponse {
  const normalizedValue = normalizeAiChatMessageResponseIntegerJson(value);
  if (!(isAiChatMessageResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_chat_message_response');
  }
  return normalizedValue;
}

function isAiChatMessageResponse(value: unknown): value is AiChatMessageResponse {
  return isRecord(value) && ((value["completionTokens"] === null) || (typeof value["completionTokens"] === 'number' && Number.isSafeInteger(value["completionTokens"]))) && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["promptTokens"] === null) || (typeof value["promptTokens"] === 'number' && Number.isSafeInteger(value["promptTokens"]))) && (typeof value["roleKey"] === 'string') && (typeof value["sessionId"] === 'string' && guidPattern.test(value["sessionId"])) && (typeof value["statusKey"] === 'string');
}

export function readAiChatSessionListItem(value: unknown): AiChatSessionListItem {
  const normalizedValue = normalizeAiChatSessionListItemIntegerJson(value);
  if (!(isAiChatSessionListItem(normalizedValue))) {
    throw new Error('client.invalid_ai_chat_session_list_item');
  }
  return normalizedValue;
}

function isAiChatSessionListItem(value: unknown): value is AiChatSessionListItem {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["lastMessageAtUtc"] === null) || (typeof value["lastMessageAtUtc"] === 'string')) && (typeof value["messageCount"] === 'number' && Number.isSafeInteger(value["messageCount"])) && (typeof value["modelConfigId"] === 'string' && guidPattern.test(value["modelConfigId"])) && (typeof value["modelName"] === 'string') && (typeof value["ownerUserId"] === 'string' && guidPattern.test(value["ownerUserId"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiChatSessionResponse(value: unknown): AiChatSessionResponse {
  const normalizedValue = normalizeAiChatSessionResponseIntegerJson(value);
  if (!(isAiChatSessionResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_chat_session_response');
  }
  return normalizedValue;
}

function isAiChatSessionResponse(value: unknown): value is AiChatSessionResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isGenerating"] === 'boolean') && (Array.isArray(value["messages"]) && value["messages"].every(item17 => isAiChatMessageResponse(item17))) && (typeof value["modelConfigId"] === 'string' && guidPattern.test(value["modelConfigId"])) && (typeof value["modelName"] === 'string') && (typeof value["ownerUserId"] === 'string' && guidPattern.test(value["ownerUserId"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiMcpRemoteConnectionListItem(value: unknown): AiMcpRemoteConnectionListItem {
  const normalizedValue = normalizeAiMcpRemoteConnectionListItemIntegerJson(value);
  if (!(isAiMcpRemoteConnectionListItem(normalizedValue))) {
    throw new Error('client.invalid_ai_mcp_remote_connection_list_item');
  }
  return normalizedValue;
}

function isAiMcpRemoteConnectionListItem(value: unknown): value is AiMcpRemoteConnectionListItem {
  return isRecord(value) && (typeof value["connectionKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["hasServiceToken"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maskedEndpointUrl"] === 'string') && (typeof value["updatedAtUtc"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiMcpRemoteConnectionResponse(value: unknown): AiMcpRemoteConnectionResponse {
  const normalizedValue = normalizeAiMcpRemoteConnectionResponseIntegerJson(value);
  if (!(isAiMcpRemoteConnectionResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_mcp_remote_connection_response');
  }
  return normalizedValue;
}

function isAiMcpRemoteConnectionResponse(value: unknown): value is AiMcpRemoteConnectionResponse {
  return isRecord(value) && (typeof value["connectionKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["endpointUrl"] === 'string') && (typeof value["hasServiceToken"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["oAuthScopesJson"] === null) || (typeof value["oAuthScopesJson"] === 'string')) && (typeof value["updatedAtUtc"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiMcpRemoteDiscoveredToolItem(value: unknown): AiMcpRemoteDiscoveredToolItem {
  if (!(isAiMcpRemoteDiscoveredToolItem(value))) {
    throw new Error('client.invalid_ai_mcp_remote_discovered_tool_item');
  }
  return value;
}

function isAiMcpRemoteDiscoveredToolItem(value: unknown): value is AiMcpRemoteDiscoveredToolItem {
  return isRecord(value) && ((value["approvalStatusKey"] === null) || (typeof value["approvalStatusKey"] === 'string')) && (typeof value["inputSchemaJson"] === 'string') && (typeof value["isApproved"] === 'boolean') && (typeof value["remoteToolName"] === 'string');
}

export function readAiMcpRemoteToolApprovalItem(value: unknown): AiMcpRemoteToolApprovalItem {
  const normalizedValue = normalizeAiMcpRemoteToolApprovalItemIntegerJson(value);
  if (!(isAiMcpRemoteToolApprovalItem(normalizedValue))) {
    throw new Error('client.invalid_ai_mcp_remote_tool_approval_item');
  }
  return normalizedValue;
}

function isAiMcpRemoteToolApprovalItem(value: unknown): value is AiMcpRemoteToolApprovalItem {
  return isRecord(value) && (typeof value["approvalStatusKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["localToolName"] === 'string') && (typeof value["permissionCode"] === 'string') && (typeof value["remoteToolName"] === 'string') && (typeof value["sideEffectKey"] === 'string') && (typeof value["toolVersion"] === 'number' && Number.isSafeInteger(value["toolVersion"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiModelConfigListItem(value: unknown): AiModelConfigListItem {
  const normalizedValue = normalizeAiModelConfigListItemIntegerJson(value);
  if (!(isAiModelConfigListItem(normalizedValue))) {
    throw new Error('client.invalid_ai_model_config_list_item');
  }
  return normalizedValue;
}

function isAiModelConfigListItem(value: unknown): value is AiModelConfigListItem {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["hasApiKey"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && ((value["lastTestedAtUtc"] === null) || (typeof value["lastTestedAtUtc"] === 'string')) && ((value["lastTestMessage"] === null) || (typeof value["lastTestMessage"] === 'string')) && ((value["lastTestStatusKey"] === null) || (typeof value["lastTestStatusKey"] === 'string')) && (typeof value["maskedEndpointBaseUrl"] === 'string') && (typeof value["modelId"] === 'string') && (typeof value["name"] === 'string') && (typeof value["providerKey"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiModelConfigResponse(value: unknown): AiModelConfigResponse {
  const normalizedValue = normalizeAiModelConfigResponseIntegerJson(value);
  if (!(isAiModelConfigResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_model_config_response');
  }
  return normalizedValue;
}

function isAiModelConfigResponse(value: unknown): value is AiModelConfigResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["endpointBaseUrl"] === 'string') && (typeof value["hasApiKey"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && ((value["lastTestedAtUtc"] === null) || (typeof value["lastTestedAtUtc"] === 'string')) && ((value["lastTestMessage"] === null) || (typeof value["lastTestMessage"] === 'string')) && ((value["lastTestStatusKey"] === null) || (typeof value["lastTestStatusKey"] === 'string')) && (typeof value["modelId"] === 'string') && (typeof value["name"] === 'string') && ((value["organizationId"] === null) || (typeof value["organizationId"] === 'string')) && (typeof value["providerKey"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiTenantQuotaListItem(value: unknown): AiTenantQuotaListItem {
  const normalizedValue = normalizeAiTenantQuotaListItemIntegerJson(value);
  if (!(isAiTenantQuotaListItem(normalizedValue))) {
    throw new Error('client.invalid_ai_tenant_quota_list_item');
  }
  return normalizedValue;
}

function isAiTenantQuotaListItem(value: unknown): value is AiTenantQuotaListItem {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["monthlyRequestLimit"] === null) || (typeof value["monthlyRequestLimit"] === 'number' && Number.isSafeInteger(value["monthlyRequestLimit"]))) && ((value["monthlyTokenLimit"] === null) || (typeof value["monthlyTokenLimit"] === 'number' && Number.isSafeInteger(value["monthlyTokenLimit"]))) && (typeof value["quotaMonthKey"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["usedRequestsThisMonth"] === 'number' && Number.isSafeInteger(value["usedRequestsThisMonth"])) && (typeof value["usedTokensThisMonth"] === 'number' && Number.isSafeInteger(value["usedTokensThisMonth"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAiTenantQuotaResponse(value: unknown): AiTenantQuotaResponse {
  const normalizedValue = normalizeAiTenantQuotaResponseIntegerJson(value);
  if (!(isAiTenantQuotaResponse(normalizedValue))) {
    throw new Error('client.invalid_ai_tenant_quota_response');
  }
  return normalizedValue;
}

function isAiTenantQuotaResponse(value: unknown): value is AiTenantQuotaResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["monthlyRequestLimit"] === null) || (typeof value["monthlyRequestLimit"] === 'number' && Number.isSafeInteger(value["monthlyRequestLimit"]))) && ((value["monthlyTokenLimit"] === null) || (typeof value["monthlyTokenLimit"] === 'number' && Number.isSafeInteger(value["monthlyTokenLimit"]))) && (typeof value["quotaMonthKey"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["usedRequestsThisMonth"] === 'number' && Number.isSafeInteger(value["usedRequestsThisMonth"])) && (typeof value["usedTokensThisMonth"] === 'number' && Number.isSafeInteger(value["usedTokensThisMonth"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readApproveAiMcpRemoteToolRequest(value: unknown): ApproveAiMcpRemoteToolRequest {
  if (!(isApproveAiMcpRemoteToolRequest(value))) {
    throw new Error('client.invalid_approve_ai_mcp_remote_tool_request');
  }
  return value;
}

function isApproveAiMcpRemoteToolRequest(value: unknown): value is ApproveAiMcpRemoteToolRequest {
  return isRecord(value) && (typeof value["permissionCode"] === 'string') && (typeof value["remoteToolName"] === 'string') && (typeof value["sideEffectKey"] === 'string');
}

export function readAssignHostTenantPackageRequest(value: unknown): AssignHostTenantPackageRequest {
  const normalizedValue = normalizeAssignHostTenantPackageRequestIntegerJson(value);
  if (!(isAssignHostTenantPackageRequest(normalizedValue))) {
    throw new Error('client.invalid_assign_host_tenant_package_request');
  }
  return normalizedValue;
}

function isAssignHostTenantPackageRequest(value: unknown): value is AssignHostTenantPackageRequest {
  return isRecord(value) && ((value["tenantPackageId"] === null) || (typeof value["tenantPackageId"] === 'string' && guidPattern.test(value["tenantPackageId"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAssignOrganizationPositionLevelRequest(value: unknown): AssignOrganizationPositionLevelRequest {
  const normalizedValue = normalizeAssignOrganizationPositionLevelRequestIntegerJson(value);
  if (!(isAssignOrganizationPositionLevelRequest(normalizedValue))) {
    throw new Error('client.invalid_assign_organization_position_level_request');
  }
  return normalizedValue;
}

function isAssignOrganizationPositionLevelRequest(value: unknown): value is AssignOrganizationPositionLevelRequest {
  return isRecord(value) && ((value["positionLevelId"] === null) || (typeof value["positionLevelId"] === 'string' && guidPattern.test(value["positionLevelId"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAssignOrganizationPositionUnitRequest(value: unknown): AssignOrganizationPositionUnitRequest {
  const normalizedValue = normalizeAssignOrganizationPositionUnitRequestIntegerJson(value);
  if (!(isAssignOrganizationPositionUnitRequest(normalizedValue))) {
    throw new Error('client.invalid_assign_organization_position_unit_request');
  }
  return normalizedValue;
}

function isAssignOrganizationPositionUnitRequest(value: unknown): value is AssignOrganizationPositionUnitRequest {
  return isRecord(value) && ((value["unitId"] === null) || (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readAuthenticationEventCursorPage(value: unknown): AuthenticationEventCursorPage {
  if (!(isAuthenticationEventCursorPage(value))) {
    throw new Error('client.invalid_authentication_event_cursor_page');
  }
  return value;
}

function isAuthenticationEventCursorPage(value: unknown): value is AuthenticationEventCursorPage {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAuthenticationEventResponse(item14))) && ((value["nextCursor"] === null) || (typeof value["nextCursor"] === 'string'));
}

export function readAuthenticationEventResponse(value: unknown): AuthenticationEventResponse {
  if (!(isAuthenticationEventResponse(value))) {
    throw new Error('client.invalid_authentication_event_response');
  }
  return value;
}

function isAuthenticationEventResponse(value: unknown): value is AuthenticationEventResponse {
  return isRecord(value) && ((value["actorUserId"] === null) || (typeof value["actorUserId"] === 'string' && guidPattern.test(value["actorUserId"]))) && ((value["applicationSessionId"] === null) || (typeof value["applicationSessionId"] === 'string' && guidPattern.test(value["applicationSessionId"]))) && ((value["authenticationMethod"] === null) || (typeof value["authenticationMethod"] === 'string')) && ((value["centerSessionId"] === null) || (typeof value["centerSessionId"] === 'string' && guidPattern.test(value["centerSessionId"]))) && ((value["clientId"] === null) || (typeof value["clientId"] === 'string')) && ((value["contextTenantId"] === null) || (typeof value["contextTenantId"] === 'string' && guidPattern.test(value["contextTenantId"]))) && (typeof value["eventType"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && (typeof value["resultCode"] === 'string') && ((value["sessionId"] === null) || (typeof value["sessionId"] === 'string' && guidPattern.test(value["sessionId"]))) && (typeof value["succeeded"] === 'boolean') && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readAuthorizationTreeActionResponse(value: unknown): AuthorizationTreeActionResponse {
  const normalizedValue = normalizeAuthorizationTreeActionResponseIntegerJson(value);
  if (!(isAuthorizationTreeActionResponse(normalizedValue))) {
    throw new Error('client.invalid_authorization_tree_action_response');
  }
  return normalizedValue;
}

function isAuthorizationTreeActionResponse(value: unknown): value is AuthorizationTreeActionResponse {
  return isRecord(value) && (typeof value["id"] === 'string') && (typeof value["name"] === 'string') && (typeof value["order"] === 'number' && Number.isSafeInteger(value["order"])) && (typeof value["permissionCode"] === 'string');
}

export function readAuthorizationTreeModuleResponse(value: unknown): AuthorizationTreeModuleResponse {
  const normalizedValue = normalizeAuthorizationTreeModuleResponseIntegerJson(value);
  if (!(isAuthorizationTreeModuleResponse(normalizedValue))) {
    throw new Error('client.invalid_authorization_tree_module_response');
  }
  return normalizedValue;
}

function isAuthorizationTreeModuleResponse(value: unknown): value is AuthorizationTreeModuleResponse {
  return isRecord(value) && (typeof value["id"] === 'string') && (typeof value["order"] === 'number' && Number.isSafeInteger(value["order"])) && (Array.isArray(value["pages"]) && value["pages"].every(item14 => isAuthorizationTreePageResponse(item14))) && (typeof value["title"] === 'string');
}

export function readAuthorizationTreePageResponse(value: unknown): AuthorizationTreePageResponse {
  const normalizedValue = normalizeAuthorizationTreePageResponseIntegerJson(value);
  if (!(isAuthorizationTreePageResponse(normalizedValue))) {
    throw new Error('client.invalid_authorization_tree_page_response');
  }
  return normalizedValue;
}

function isAuthorizationTreePageResponse(value: unknown): value is AuthorizationTreePageResponse {
  return isRecord(value) && (Array.isArray(value["actions"]) && value["actions"].every(item16 => isAuthorizationTreeActionResponse(item16))) && (Array.isArray(value["children"]) && value["children"].every(item17 => isAuthorizationTreePageResponse(item17))) && (typeof value["id"] === 'string') && (typeof value["order"] === 'number' && Number.isSafeInteger(value["order"])) && (typeof value["permissionCode"] === 'string') && (typeof value["title"] === 'string');
}

export function readBatchChangeHostJobScheduleStateItem(value: unknown): BatchChangeHostJobScheduleStateItem {
  const normalizedValue = normalizeBatchChangeHostJobScheduleStateItemIntegerJson(value);
  if (!(isBatchChangeHostJobScheduleStateItem(normalizedValue))) {
    throw new Error('client.invalid_batch_change_host_job_schedule_state_item');
  }
  return normalizedValue;
}

function isBatchChangeHostJobScheduleStateItem(value: unknown): value is BatchChangeHostJobScheduleStateItem {
  return isRecord(value) && (typeof value["scheduleId"] === 'string' && guidPattern.test(value["scheduleId"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readBatchChangeHostJobScheduleStateRequest(value: unknown): BatchChangeHostJobScheduleStateRequest {
  const normalizedValue = normalizeBatchChangeHostJobScheduleStateRequestIntegerJson(value);
  if (!(isBatchChangeHostJobScheduleStateRequest(normalizedValue))) {
    throw new Error('client.invalid_batch_change_host_job_schedule_state_request');
  }
  return normalizedValue;
}

function isBatchChangeHostJobScheduleStateRequest(value: unknown): value is BatchChangeHostJobScheduleStateRequest {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isBatchChangeHostJobScheduleStateItem(item14)));
}

export function readBatchChangeHostJobScheduleStateResponse(value: unknown): BatchChangeHostJobScheduleStateResponse {
  const normalizedValue = normalizeBatchChangeHostJobScheduleStateResponseIntegerJson(value);
  if (!(isBatchChangeHostJobScheduleStateResponse(normalizedValue))) {
    throw new Error('client.invalid_batch_change_host_job_schedule_state_response');
  }
  return normalizedValue;
}

function isBatchChangeHostJobScheduleStateResponse(value: unknown): value is BatchChangeHostJobScheduleStateResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isBatchChangeHostJobScheduleStateResultItem(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
}

export function readBatchChangeHostJobScheduleStateResultItem(value: unknown): BatchChangeHostJobScheduleStateResultItem {
  const normalizedValue = normalizeBatchChangeHostJobScheduleStateResultItemIntegerJson(value);
  if (!(isBatchChangeHostJobScheduleStateResultItem(normalizedValue))) {
    throw new Error('client.invalid_batch_change_host_job_schedule_state_result_item');
  }
  return normalizedValue;
}

function isBatchChangeHostJobScheduleStateResultItem(value: unknown): value is BatchChangeHostJobScheduleStateResultItem {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["message"] === null) || (typeof value["message"] === 'string')) && ((value["schedule"] === null) || (isHostJobScheduleResponse(value["schedule"]))) && (typeof value["scheduleId"] === 'string' && guidPattern.test(value["scheduleId"])) && (typeof value["succeeded"] === 'boolean');
}

export function readBatchCreateHostDocumentShareItem(value: unknown): BatchCreateHostDocumentShareItem {
  const normalizedValue = normalizeBatchCreateHostDocumentShareItemIntegerJson(value);
  if (!(isBatchCreateHostDocumentShareItem(normalizedValue))) {
    throw new Error('client.invalid_batch_create_host_document_share_item');
  }
  return normalizedValue;
}

function isBatchCreateHostDocumentShareItem(value: unknown): value is BatchCreateHostDocumentShareItem {
  return isRecord(value) && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["message"] === null) || (typeof value["message"] === 'string')) && ((value["share"] === null) || (isHostDocumentShareResponse(value["share"]))) && (typeof value["succeeded"] === 'boolean');
}

export function readBatchCreateHostDocumentSharesRequest(value: unknown): BatchCreateHostDocumentSharesRequest {
  const normalizedValue = normalizeBatchCreateHostDocumentSharesRequestIntegerJson(value);
  if (!(isBatchCreateHostDocumentSharesRequest(normalizedValue))) {
    throw new Error('client.invalid_batch_create_host_document_shares_request');
  }
  return normalizedValue;
}

function isBatchCreateHostDocumentSharesRequest(value: unknown): value is BatchCreateHostDocumentSharesRequest {
  return isRecord(value) && (Array.isArray(value["documentIds"]) && value["documentIds"].every(item20 => typeof item20 === 'string' && guidPattern.test(item20))) && (value["maxAccessCount"] === undefined || ((value["maxAccessCount"] === null) || (typeof value["maxAccessCount"] === 'number' && Number.isSafeInteger(value["maxAccessCount"])))) && (value["password"] === undefined || ((value["password"] === null) || (typeof value["password"] === 'string'))) && (typeof value["validDays"] === 'number' && Number.isSafeInteger(value["validDays"]));
}

export function readBatchCreateHostDocumentSharesResponse(value: unknown): BatchCreateHostDocumentSharesResponse {
  const normalizedValue = normalizeBatchCreateHostDocumentSharesResponseIntegerJson(value);
  if (!(isBatchCreateHostDocumentSharesResponse(normalizedValue))) {
    throw new Error('client.invalid_batch_create_host_document_shares_response');
  }
  return normalizedValue;
}

function isBatchCreateHostDocumentSharesResponse(value: unknown): value is BatchCreateHostDocumentSharesResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isBatchCreateHostDocumentShareItem(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
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

export function readBatchDeleteHostFileItem(value: unknown): BatchDeleteHostFileItem {
  if (!(isBatchDeleteHostFileItem(value))) {
    throw new Error('client.invalid_batch_delete_host_file_item');
  }
  return value;
}

function isBatchDeleteHostFileItem(value: unknown): value is BatchDeleteHostFileItem {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["fileId"] === 'string' && guidPattern.test(value["fileId"])) && ((value["message"] === null) || (typeof value["message"] === 'string')) && (typeof value["succeeded"] === 'boolean');
}

export function readBatchDeleteHostFilesRequest(value: unknown): BatchDeleteHostFilesRequest {
  if (!(isBatchDeleteHostFilesRequest(value))) {
    throw new Error('client.invalid_batch_delete_host_files_request');
  }
  return value;
}

function isBatchDeleteHostFilesRequest(value: unknown): value is BatchDeleteHostFilesRequest {
  return isRecord(value) && (Array.isArray(value["fileIds"]) && value["fileIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16)));
}

export function readBatchDeleteHostFilesResponse(value: unknown): BatchDeleteHostFilesResponse {
  const normalizedValue = normalizeBatchDeleteHostFilesResponseIntegerJson(value);
  if (!(isBatchDeleteHostFilesResponse(normalizedValue))) {
    throw new Error('client.invalid_batch_delete_host_files_response');
  }
  return normalizedValue;
}

function isBatchDeleteHostFilesResponse(value: unknown): value is BatchDeleteHostFilesResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isBatchDeleteHostFileItem(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
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
  const normalizedValue = normalizeBatchHostUserStatusResponseIntegerJson(value);
  if (!(isBatchHostUserStatusResponse(normalizedValue))) {
    throw new Error('client.invalid_batch_host_user_status_response');
  }
  return normalizedValue;
}

function isBatchHostUserStatusResponse(value: unknown): value is BatchHostUserStatusResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isBatchHostUserStatusItem(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
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

export function readBatchUploadHostFileItem(value: unknown): BatchUploadHostFileItem {
  const normalizedValue = normalizeBatchUploadHostFileItemIntegerJson(value);
  if (!(isBatchUploadHostFileItem(normalizedValue))) {
    throw new Error('client.invalid_batch_upload_host_file_item');
  }
  return normalizedValue;
}

function isBatchUploadHostFileItem(value: unknown): value is BatchUploadHostFileItem {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["file"] === null) || (isHostFileResponse(value["file"]))) && ((value["message"] === null) || (typeof value["message"] === 'string')) && (typeof value["originalFileName"] === 'string') && (typeof value["succeeded"] === 'boolean');
}

export function readBatchUploadHostFilesResponse(value: unknown): BatchUploadHostFilesResponse {
  const normalizedValue = normalizeBatchUploadHostFilesResponseIntegerJson(value);
  if (!(isBatchUploadHostFilesResponse(normalizedValue))) {
    throw new Error('client.invalid_batch_upload_host_files_response');
  }
  return normalizedValue;
}

function isBatchUploadHostFilesResponse(value: unknown): value is BatchUploadHostFilesResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isBatchUploadHostFileItem(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
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

export function readCacheInvalidationOperationSummary(value: unknown): CacheInvalidationOperationSummary {
  if (!(isCacheInvalidationOperationSummary(value))) {
    throw new Error('client.invalid_cache_invalidation_operation_summary');
  }
  return value;
}

function isCacheInvalidationOperationSummary(value: unknown): value is CacheInvalidationOperationSummary {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["operationKey"] === 'string') && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isCacheInvalidationParameterSummary(item19)));
}

export function readCacheInvalidationParameterSummary(value: unknown): CacheInvalidationParameterSummary {
  if (!(isCacheInvalidationParameterSummary(value))) {
    throw new Error('client.invalid_cache_invalidation_parameter_summary');
  }
  return value;
}

function isCacheInvalidationParameterSummary(value: unknown): value is CacheInvalidationParameterSummary {
  return isRecord(value) && (typeof value["name"] === 'string') && (typeof value["required"] === 'boolean') && (typeof value["valueType"] === 'string');
}

export function readCacheInvalidationRequest(value: unknown): CacheInvalidationRequest {
  if (!(isCacheInvalidationRequest(value))) {
    throw new Error('client.invalid_cache_invalidation_request');
  }
  return value;
}

function isCacheInvalidationRequest(value: unknown): value is CacheInvalidationRequest {
  return isRecord(value) && (typeof value["operationKey"] === 'string') && ((value["parameters"] === null) || (isRecord(value["parameters"]))) && ((value["scope"] === null) || (typeof value["scope"] === 'string'));
}

export function readCacheInvalidationResult(value: unknown): CacheInvalidationResult {
  if (!(isCacheInvalidationResult(value))) {
    throw new Error('client.invalid_cache_invalidation_result');
  }
  return value;
}

function isCacheInvalidationResult(value: unknown): value is CacheInvalidationResult {
  return isRecord(value) && (typeof value["entryName"] === 'string') && (Array.isArray(value["invalidatedTargets"]) && value["invalidatedTargets"].every(item27 => typeof item27 === 'string')) && (typeof value["operationKey"] === 'string') && (typeof value["scope"] === 'string');
}

export function readCachePolicySummary(value: unknown): CachePolicySummary {
  const normalizedValue = normalizeCachePolicySummaryIntegerJson(value);
  if (!(isCachePolicySummary(normalizedValue))) {
    throw new Error('client.invalid_cache_policy_summary');
  }
  return normalizedValue;
}

function isCachePolicySummary(value: unknown): value is CachePolicySummary {
  return isRecord(value) && (typeof value["accessKind"] === 'string') && (typeof value["canInvalidate"] === 'boolean') && (typeof value["consistencyClass"] === 'string') && (typeof value["entryName"] === 'string') && (Array.isArray(value["invalidationOperations"]) && value["invalidationOperations"].every(item31 => isCacheInvalidationOperationSummary(item31))) && ((value["l1DurationSeconds"] === null) || (typeof value["l1DurationSeconds"] === 'number' && Number.isSafeInteger(value["l1DurationSeconds"]))) && ((value["l2DurationSeconds"] === null) || (typeof value["l2DurationSeconds"] === 'number' && Number.isSafeInteger(value["l2DurationSeconds"]))) && (typeof value["ownerModule"] === 'string') && (typeof value["requiresDirectInvalidation"] === 'boolean');
}

export function readCancelDataApprovalRequestBody(value: unknown): CancelDataApprovalRequestBody {
  if (!(isCancelDataApprovalRequestBody(value))) {
    throw new Error('client.invalid_cancel_data_approval_request_body');
  }
  return value;
}

function isCancelDataApprovalRequestBody(value: unknown): value is CancelDataApprovalRequestBody {
  return isRecord(value) && (typeof value["idempotencyKey"] === 'string');
}

export function readCancelWorkflowInstanceRequest(value: unknown): CancelWorkflowInstanceRequest {
  const normalizedValue = normalizeCancelWorkflowInstanceRequestIntegerJson(value);
  if (!(isCancelWorkflowInstanceRequest(normalizedValue))) {
    throw new Error('client.invalid_cancel_workflow_instance_request');
  }
  return normalizedValue;
}

function isCancelWorkflowInstanceRequest(value: unknown): value is CancelWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readChangeHostJobScheduleStateRequest(value: unknown): ChangeHostJobScheduleStateRequest {
  const normalizedValue = normalizeChangeHostJobScheduleStateRequestIntegerJson(value);
  if (!(isChangeHostJobScheduleStateRequest(normalizedValue))) {
    throw new Error('client.invalid_change_host_job_schedule_state_request');
  }
  return normalizedValue;
}

function isChangeHostJobScheduleStateRequest(value: unknown): value is ChangeHostJobScheduleStateRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readChangePasswordRequest(value: unknown): ChangePasswordRequest {
  if (!(isChangePasswordRequest(value))) {
    throw new Error('client.invalid_change_password_request');
  }
  return value;
}

function isChangePasswordRequest(value: unknown): value is ChangePasswordRequest {
  return isRecord(value) && (typeof value["currentPassword"] === 'string') && (typeof value["newPassword"] === 'string');
}

export function readChangePersonalScheduleRequest(value: unknown): ChangePersonalScheduleRequest {
  const normalizedValue = normalizeChangePersonalScheduleRequestIntegerJson(value);
  if (!(isChangePersonalScheduleRequest(normalizedValue))) {
    throw new Error('client.invalid_change_personal_schedule_request');
  }
  return normalizedValue;
}

function isChangePersonalScheduleRequest(value: unknown): value is ChangePersonalScheduleRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readChangeSerialNumberRuleStatusRequest(value: unknown): ChangeSerialNumberRuleStatusRequest {
  const normalizedValue = normalizeChangeSerialNumberRuleStatusRequestIntegerJson(value);
  if (!(isChangeSerialNumberRuleStatusRequest(normalizedValue))) {
    throw new Error('client.invalid_change_serial_number_rule_status_request');
  }
  return normalizedValue;
}

function isChangeSerialNumberRuleStatusRequest(value: unknown): value is ChangeSerialNumberRuleStatusRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readCodeGenerationCatalogColumnListResponse(value: unknown): CodeGenerationCatalogColumnListResponse {
  const normalizedValue = normalizeCodeGenerationCatalogColumnListResponseIntegerJson(value);
  if (!(isCodeGenerationCatalogColumnListResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_catalog_column_list_response');
  }
  return normalizedValue;
}

function isCodeGenerationCatalogColumnListResponse(value: unknown): value is CodeGenerationCatalogColumnListResponse {
  return isRecord(value) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (Array.isArray(value["skippedColumnNames"]) && value["skippedColumnNames"].every(item27 => typeof item27 === 'string')) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogColumnSyncRequest(value: unknown): CodeGenerationCatalogColumnSyncRequest {
  const normalizedValue = normalizeCodeGenerationCatalogColumnSyncRequestIntegerJson(value);
  if (!(isCodeGenerationCatalogColumnSyncRequest(normalizedValue))) {
    throw new Error('client.invalid_code_generation_catalog_column_sync_request');
  }
  return normalizedValue;
}

function isCodeGenerationCatalogColumnSyncRequest(value: unknown): value is CodeGenerationCatalogColumnSyncRequest {
  return isRecord(value) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogColumnSyncResponse(value: unknown): CodeGenerationCatalogColumnSyncResponse {
  const normalizedValue = normalizeCodeGenerationCatalogColumnSyncResponseIntegerJson(value);
  if (!(isCodeGenerationCatalogColumnSyncResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_catalog_column_sync_response');
  }
  return normalizedValue;
}

function isCodeGenerationCatalogColumnSyncResponse(value: unknown): value is CodeGenerationCatalogColumnSyncResponse {
  return isRecord(value) && (Array.isArray(value["addedColumnNames"]) && value["addedColumnNames"].every(item25 => typeof item25 === 'string')) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationPreviewColumnRequest(item16))) && (Array.isArray(value["removedColumnNames"]) && value["removedColumnNames"].every(item27 => typeof item27 === 'string')) && (Array.isArray(value["skippedColumnNames"]) && value["skippedColumnNames"].every(item27 => typeof item27 === 'string')) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogMetadataColumnResponse(value: unknown): CodeGenerationCatalogMetadataColumnResponse {
  const normalizedValue = normalizeCodeGenerationCatalogMetadataColumnResponseIntegerJson(value);
  if (!(isCodeGenerationCatalogMetadataColumnResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_catalog_metadata_column_response');
  }
  return normalizedValue;
}

function isCodeGenerationCatalogMetadataColumnResponse(value: unknown): value is CodeGenerationCatalogMetadataColumnResponse {
  return isRecord(value) && (typeof value["columnName"] === 'string') && (typeof value["columnType"] === 'string') && (typeof value["dataType"] === 'string') && (typeof value["isNullable"] === 'boolean') && ((value["maxLength"] === null) || (typeof value["maxLength"] === 'number' && Number.isSafeInteger(value["maxLength"]))) && ((value["numericPrecision"] === null) || (typeof value["numericPrecision"] === 'number' && Number.isSafeInteger(value["numericPrecision"]))) && ((value["numericScale"] === null) || (typeof value["numericScale"] === 'number' && Number.isSafeInteger(value["numericScale"]))) && (typeof value["ordinalPosition"] === 'number' && Number.isSafeInteger(value["ordinalPosition"]));
}

export function readCodeGenerationCatalogMetadataResponse(value: unknown): CodeGenerationCatalogMetadataResponse {
  const normalizedValue = normalizeCodeGenerationCatalogMetadataResponseIntegerJson(value);
  if (!(isCodeGenerationCatalogMetadataResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_catalog_metadata_response');
  }
  return normalizedValue;
}

function isCodeGenerationCatalogMetadataResponse(value: unknown): value is CodeGenerationCatalogMetadataResponse {
  return isRecord(value) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isCodeGenerationCatalogMetadataColumnResponse(item16))) && (typeof value["objectKind"] === 'string') && (typeof value["objectName"] === 'string');
}

export function readCodeGenerationCatalogMigrationDraftRequest(value: unknown): CodeGenerationCatalogMigrationDraftRequest {
  if (!(isCodeGenerationCatalogMigrationDraftRequest(value))) {
    throw new Error('client.invalid_code_generation_catalog_migration_draft_request');
  }
  return value;
}

function isCodeGenerationCatalogMigrationDraftRequest(value: unknown): value is CodeGenerationCatalogMigrationDraftRequest {
  return isRecord(value) && (typeof value["tableName"] === 'string');
}

export function readCodeGenerationCatalogMigrationDraftResponse(value: unknown): CodeGenerationCatalogMigrationDraftResponse {
  if (!(isCodeGenerationCatalogMigrationDraftResponse(value))) {
    throw new Error('client.invalid_code_generation_catalog_migration_draft_response');
  }
  return value;
}

function isCodeGenerationCatalogMigrationDraftResponse(value: unknown): value is CodeGenerationCatalogMigrationDraftResponse {
  return isRecord(value) && (typeof value["mySqlDraft"] === 'string') && (typeof value["sqlServerDraft"] === 'string') && (typeof value["tableName"] === 'string') && (Array.isArray(value["warnings"]) && value["warnings"].every(item17 => typeof item17 === 'string'));
}

export function readCodeGenerationCatalogObjectResponse(value: unknown): CodeGenerationCatalogObjectResponse {
  if (!(isCodeGenerationCatalogObjectResponse(value))) {
    throw new Error('client.invalid_code_generation_catalog_object_response');
  }
  return value;
}

function isCodeGenerationCatalogObjectResponse(value: unknown): value is CodeGenerationCatalogObjectResponse {
  return isRecord(value) && (typeof value["objectKind"] === 'string') && (typeof value["objectName"] === 'string');
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
  const normalizedValue = normalizeCodeGenerationPreviewColumnRequestIntegerJson(value);
  if (!(isCodeGenerationPreviewColumnRequest(normalizedValue))) {
    throw new Error('client.invalid_code_generation_preview_column_request');
  }
  return normalizedValue;
}

function isCodeGenerationPreviewColumnRequest(value: unknown): value is CodeGenerationPreviewColumnRequest {
  return isRecord(value) && (typeof value["clrPropertyName"] === 'string') && (typeof value["databaseName"] === 'string') && (typeof value["isNullable"] === 'boolean') && (typeof value["jsonPropertyName"] === 'string') && ((value["maxLength"] === null) || (typeof value["maxLength"] === 'number' && Number.isSafeInteger(value["maxLength"]))) && ((value["numericPrecision"] === null) || (typeof value["numericPrecision"] === 'number' && Number.isSafeInteger(value["numericPrecision"]))) && ((value["numericScale"] === null) || (typeof value["numericScale"] === 'number' && Number.isSafeInteger(value["numericScale"]))) && (typeof value["scalarType"] === 'string') && (value["ui"] === undefined || ((value["ui"] === null) || (isCodeGenerationPreviewColumnUiRequest(value["ui"]))));
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
  const normalizedValue = normalizeCodeGenerationPreviewRequestIntegerJson(value);
  if (!(isCodeGenerationPreviewRequest(normalizedValue))) {
    throw new Error('client.invalid_code_generation_preview_request');
  }
  return normalizedValue;
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
  const normalizedValue = normalizeCodeGenerationRunApplyResponseIntegerJson(value);
  if (!(isCodeGenerationRunApplyResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_run_apply_response');
  }
  return normalizedValue;
}

function isCodeGenerationRunApplyResponse(value: unknown): value is CodeGenerationRunApplyResponse {
  return isRecord(value) && (typeof value["artifactCount"] === 'number' && Number.isSafeInteger(value["artifactCount"])) && (typeof value["changedArtifactCount"] === 'number' && Number.isSafeInteger(value["changedArtifactCount"])) && (typeof value["manifestSha256"] === 'string') && (typeof value["previewRunId"] === 'string' && guidPattern.test(value["previewRunId"])) && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]));
}

export function readCodeGenerationRunPreviewRequest(value: unknown): CodeGenerationRunPreviewRequest {
  const normalizedValue = normalizeCodeGenerationRunPreviewRequestIntegerJson(value);
  if (!(isCodeGenerationRunPreviewRequest(normalizedValue))) {
    throw new Error('client.invalid_code_generation_run_preview_request');
  }
  return normalizedValue;
}

function isCodeGenerationRunPreviewRequest(value: unknown): value is CodeGenerationRunPreviewRequest {
  return isRecord(value) && ((value["schema"] === null) || (isCodeGenerationPreviewRequest(value["schema"]))) && ((value["templateId"] === null) || (typeof value["templateId"] === 'string' && guidPattern.test(value["templateId"]))) && ((value["templateVersion"] === null) || (typeof value["templateVersion"] === 'number' && Number.isSafeInteger(value["templateVersion"])));
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
  const normalizedValue = normalizeCodeGenerationRunResponseIntegerJson(value);
  if (!(isCodeGenerationRunResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_run_response');
  }
  return normalizedValue;
}

function isCodeGenerationRunResponse(value: unknown): value is CodeGenerationRunResponse {
  return isRecord(value) && (typeof value["artifactCount"] === 'number' && Number.isSafeInteger(value["artifactCount"])) && ((value["entityKey"] === null) || (typeof value["entityKey"] === 'string')) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["finishedAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["manifestSha256"] === null) || (typeof value["manifestSha256"] === 'string')) && ((value["moduleKey"] === null) || (typeof value["moduleKey"] === 'string')) && (typeof value["operationKind"] === 'string') && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && ((value["schemaSha256"] === null) || (typeof value["schemaSha256"] === 'string')) && ((value["sourceApplyRunId"] === null) || (typeof value["sourceApplyRunId"] === 'string' && guidPattern.test(value["sourceApplyRunId"]))) && (typeof value["startedAtUtc"] === 'string') && (typeof value["status"] === 'string') && ((value["templateId"] === null) || (typeof value["templateId"] === 'string' && guidPattern.test(value["templateId"]))) && ((value["templateVersion"] === null) || (typeof value["templateVersion"] === 'number' && Number.isSafeInteger(value["templateVersion"])));
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
  const normalizedValue = normalizeCodeGenerationRunRollbackChainResponseIntegerJson(value);
  if (!(isCodeGenerationRunRollbackChainResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_run_rollback_chain_response');
  }
  return normalizedValue;
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
  const normalizedValue = normalizeCodeGenerationRunRollbackResponseIntegerJson(value);
  if (!(isCodeGenerationRunRollbackResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_run_rollback_response');
  }
  return normalizedValue;
}

function isCodeGenerationRunRollbackResponse(value: unknown): value is CodeGenerationRunRollbackResponse {
  return isRecord(value) && (typeof value["applyRunId"] === 'string' && guidPattern.test(value["applyRunId"])) && (typeof value["artifactCount"] === 'number' && Number.isSafeInteger(value["artifactCount"])) && (typeof value["changedArtifactCount"] === 'number' && Number.isSafeInteger(value["changedArtifactCount"])) && (typeof value["manifestSha256"] === 'string') && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]));
}

export function readCodeGenerationTemplateResponse(value: unknown): CodeGenerationTemplateResponse {
  const normalizedValue = normalizeCodeGenerationTemplateResponseIntegerJson(value);
  if (!(isCodeGenerationTemplateResponse(normalizedValue))) {
    throw new Error('client.invalid_code_generation_template_response');
  }
  return normalizedValue;
}

function isCodeGenerationTemplateResponse(value: unknown): value is CodeGenerationTemplateResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && (isCodeGenerationPreviewRequest(value["schema"])) && (typeof value["schemaSha256"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readConfigEntryResponse(value: unknown): ConfigEntryResponse {
  const normalizedValue = normalizeConfigEntryResponseIntegerJson(value);
  if (!(isConfigEntryResponse(normalizedValue))) {
    throw new Error('client.invalid_config_entry_response');
  }
  return normalizedValue;
}

function isConfigEntryResponse(value: unknown): value is ConfigEntryResponse {
  return isRecord(value) && (typeof value["configKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["hasValue"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["valueKind"] === 'string' && ["string", "boolean", "integer", "decimal", "json", "secret"].includes(value["valueKind"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
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

export function readConfirmOcrIdCardTaskRequest(value: unknown): ConfirmOcrIdCardTaskRequest {
  const normalizedValue = normalizeConfirmOcrIdCardTaskRequestIntegerJson(value);
  if (!(isConfirmOcrIdCardTaskRequest(normalizedValue))) {
    throw new Error('client.invalid_confirm_ocr_id_card_task_request');
  }
  return normalizedValue;
}

function isConfirmOcrIdCardTaskRequest(value: unknown): value is ConfirmOcrIdCardTaskRequest {
  return isRecord(value) && ((value["address"] === null) || (typeof value["address"] === 'string')) && ((value["birthDate"] === null) || (typeof value["birthDate"] === 'string')) && ((value["gender"] === null) || (typeof value["gender"] === 'string')) && (typeof value["idNumber"] === 'string') && (typeof value["name"] === 'string') && ((value["nation"] === null) || (typeof value["nation"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
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

export function readCopyHostRoleRequest(value: unknown): CopyHostRoleRequest {
  if (!(isCopyHostRoleRequest(value))) {
    throw new Error('client.invalid_copy_host_role_request');
  }
  return value;
}

function isCopyHostRoleRequest(value: unknown): value is CopyHostRoleRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["name"] === 'string');
}

export function readCreateAdministrativeRegionRequest(value: unknown): CreateAdministrativeRegionRequest {
  const normalizedValue = normalizeCreateAdministrativeRegionRequestIntegerJson(value);
  if (!(isCreateAdministrativeRegionRequest(normalizedValue))) {
    throw new Error('client.invalid_create_administrative_region_request');
  }
  return normalizedValue;
}

function isCreateAdministrativeRegionRequest(value: unknown): value is CreateAdministrativeRegionRequest {
  return isRecord(value) && ((value["cityCode"] === null) || (typeof value["cityCode"] === 'string')) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && ((value["latitude"] === null) || (typeof value["latitude"] === 'number' && Number.isFinite(value["latitude"])) || (typeof value["latitude"] === 'string')) && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && ((value["longitude"] === null) || (typeof value["longitude"] === 'number' && Number.isFinite(value["longitude"])) || (typeof value["longitude"] === 'string')) && ((value["mergerName"] === null) || (typeof value["mergerName"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && ((value["pinYin"] === null) || (typeof value["pinYin"] === 'string')) && ((value["regionType"] === null) || (typeof value["regionType"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["shortName"] === null) || (typeof value["shortName"] === 'string')) && ((value["zipCode"] === null) || (typeof value["zipCode"] === 'string'));
}

export function readCreateAiAgentApprovalRequest(value: unknown): CreateAiAgentApprovalRequest {
  const normalizedValue = normalizeCreateAiAgentApprovalRequestIntegerJson(value);
  if (!(isCreateAiAgentApprovalRequest(normalizedValue))) {
    throw new Error('client.invalid_create_ai_agent_approval_request');
  }
  return normalizedValue;
}

function isCreateAiAgentApprovalRequest(value: unknown): value is CreateAiAgentApprovalRequest {
  return isRecord(value) && (typeof value["argumentsJson"] === 'string') && (typeof value["operationId"] === 'string' && guidPattern.test(value["operationId"])) && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"])) && (typeof value["toolName"] === 'string') && (typeof value["toolVersion"] === 'number' && Number.isSafeInteger(value["toolVersion"]));
}

export function readCreateAiAgentApprovalResponse(value: unknown): CreateAiAgentApprovalResponse {
  if (!(isCreateAiAgentApprovalResponse(value))) {
    throw new Error('client.invalid_create_ai_agent_approval_response');
  }
  return value;
}

function isCreateAiAgentApprovalResponse(value: unknown): value is CreateAiAgentApprovalResponse {
  return isRecord(value) && (typeof value["approvalId"] === 'string' && guidPattern.test(value["approvalId"]));
}

export function readCreateAiAgentDelegationRequest(value: unknown): CreateAiAgentDelegationRequest {
  if (!(isCreateAiAgentDelegationRequest(value))) {
    throw new Error('client.invalid_create_ai_agent_delegation_request');
  }
  return value;
}

function isCreateAiAgentDelegationRequest(value: unknown): value is CreateAiAgentDelegationRequest {
  return isRecord(value) && (typeof value["expiresAtUtc"] === 'string') && (typeof value["granteeUserId"] === 'string' && guidPattern.test(value["granteeUserId"])) && ((value["permissionCode"] === null) || (typeof value["permissionCode"] === 'string')) && ((value["toolName"] === null) || (typeof value["toolName"] === 'string'));
}

export function readCreateAiAgentDelegationResponse(value: unknown): CreateAiAgentDelegationResponse {
  if (!(isCreateAiAgentDelegationResponse(value))) {
    throw new Error('client.invalid_create_ai_agent_delegation_response');
  }
  return value;
}

function isCreateAiAgentDelegationResponse(value: unknown): value is CreateAiAgentDelegationResponse {
  return isRecord(value) && (typeof value["delegationId"] === 'string' && guidPattern.test(value["delegationId"]));
}

export function readCreateAiAgentRunRequest(value: unknown): CreateAiAgentRunRequest {
  const normalizedValue = normalizeCreateAiAgentRunRequestIntegerJson(value);
  if (!(isCreateAiAgentRunRequest(normalizedValue))) {
    throw new Error('client.invalid_create_ai_agent_run_request');
  }
  return normalizedValue;
}

function isCreateAiAgentRunRequest(value: unknown): value is CreateAiAgentRunRequest {
  return isRecord(value) && (typeof value["clientRequestId"] === 'string' && guidPattern.test(value["clientRequestId"])) && (typeof value["definitionKey"] === 'string') && (typeof value["inputTokenLimit"] === 'number' && Number.isSafeInteger(value["inputTokenLimit"])) && (typeof value["modelConfigId"] === 'string' && guidPattern.test(value["modelConfigId"])) && (typeof value["outputTokenLimit"] === 'number' && Number.isSafeInteger(value["outputTokenLimit"])) && (typeof value["prompt"] === 'string');
}

export function readCreateAiAgentRunResponse(value: unknown): CreateAiAgentRunResponse {
  if (!(isCreateAiAgentRunResponse(value))) {
    throw new Error('client.invalid_create_ai_agent_run_response');
  }
  return value;
}

function isCreateAiAgentRunResponse(value: unknown): value is CreateAiAgentRunResponse {
  return isRecord(value) && (typeof value["runId"] === 'string' && guidPattern.test(value["runId"]));
}

export function readCreateAiChatSessionRequest(value: unknown): CreateAiChatSessionRequest {
  if (!(isCreateAiChatSessionRequest(value))) {
    throw new Error('client.invalid_create_ai_chat_session_request');
  }
  return value;
}

function isCreateAiChatSessionRequest(value: unknown): value is CreateAiChatSessionRequest {
  return isRecord(value) && (typeof value["modelConfigId"] === 'string' && guidPattern.test(value["modelConfigId"])) && ((value["title"] === null) || (typeof value["title"] === 'string'));
}

export function readCreateAiMcpRemoteConnectionRequest(value: unknown): CreateAiMcpRemoteConnectionRequest {
  if (!(isCreateAiMcpRemoteConnectionRequest(value))) {
    throw new Error('client.invalid_create_ai_mcp_remote_connection_request');
  }
  return value;
}

function isCreateAiMcpRemoteConnectionRequest(value: unknown): value is CreateAiMcpRemoteConnectionRequest {
  return isRecord(value) && (typeof value["connectionKey"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["endpointUrl"] === 'string') && ((value["oAuthScopesJson"] === null) || (typeof value["oAuthScopesJson"] === 'string')) && (typeof value["serviceToken"] === 'string');
}

export function readCreateAiModelConfigRequest(value: unknown): CreateAiModelConfigRequest {
  if (!(isCreateAiModelConfigRequest(value))) {
    throw new Error('client.invalid_create_ai_model_config_request');
  }
  return value;
}

function isCreateAiModelConfigRequest(value: unknown): value is CreateAiModelConfigRequest {
  return isRecord(value) && ((value["apiKey"] === null) || (typeof value["apiKey"] === 'string')) && (typeof value["endpointBaseUrl"] === 'string') && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["modelId"] === 'string') && (typeof value["name"] === 'string') && ((value["organizationId"] === null) || (typeof value["organizationId"] === 'string')) && (typeof value["providerKey"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])));
}

export function readCreateCodeGenerationTemplateRequest(value: unknown): CreateCodeGenerationTemplateRequest {
  const normalizedValue = normalizeCreateCodeGenerationTemplateRequestIntegerJson(value);
  if (!(isCreateCodeGenerationTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_create_code_generation_template_request');
  }
  return normalizedValue;
}

function isCreateCodeGenerationTemplateRequest(value: unknown): value is CreateCodeGenerationTemplateRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string') && (isCodeGenerationPreviewRequest(value["schema"]));
}

export function readCreateConfigEntryRequest(value: unknown): CreateConfigEntryRequest {
  const normalizedValue = normalizeCreateConfigEntryRequestIntegerJson(value);
  if (!(isCreateConfigEntryRequest(normalizedValue))) {
    throw new Error('client.invalid_create_config_entry_request');
  }
  return normalizedValue;
}

function isCreateConfigEntryRequest(value: unknown): value is CreateConfigEntryRequest {
  return isRecord(value) && (typeof value["configKey"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["valueKind"] === 'string' && ["string", "boolean", "integer", "decimal", "json", "secret"].includes(value["valueKind"]));
}

export function readCreateDataApprovalRequestBody(value: unknown): CreateDataApprovalRequestBody {
  if (!(isCreateDataApprovalRequestBody(value))) {
    throw new Error('client.invalid_create_data_approval_request_body');
  }
  return value;
}

function isCreateDataApprovalRequestBody(value: unknown): value is CreateDataApprovalRequestBody {
  return isRecord(value) && (typeof value["idempotencyKey"] === 'string') && (typeof value["proposedChangeJson"] === 'string') && (typeof value["scenarioKey"] === 'string') && (typeof value["targetEntityId"] === 'string' && guidPattern.test(value["targetEntityId"]));
}

export function readCreateDictItemRequest(value: unknown): CreateDictItemRequest {
  const normalizedValue = normalizeCreateDictItemRequestIntegerJson(value);
  if (!(isCreateDictItemRequest(normalizedValue))) {
    throw new Error('client.invalid_create_dict_item_request');
  }
  return normalizedValue;
}

function isCreateDictItemRequest(value: unknown): value is CreateDictItemRequest {
  return isRecord(value) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["label"] === 'string') && (typeof value["value"] === 'string');
}

export function readCreateDictTypeRequest(value: unknown): CreateDictTypeRequest {
  const normalizedValue = normalizeCreateDictTypeRequestIntegerJson(value);
  if (!(isCreateDictTypeRequest(normalizedValue))) {
    throw new Error('client.invalid_create_dict_type_request');
  }
  return normalizedValue;
}

function isCreateDictTypeRequest(value: unknown): value is CreateDictTypeRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string');
}

export function readCreateEnterpriseRequestRequest(value: unknown): CreateEnterpriseRequestRequest {
  if (!(isCreateEnterpriseRequestRequest(value))) {
    throw new Error('client.invalid_create_enterprise_request_request');
  }
  return value;
}

function isCreateEnterpriseRequestRequest(value: unknown): value is CreateEnterpriseRequestRequest {
  return isRecord(value) && (typeof value["applicantUserId"] === 'string' && guidPattern.test(value["applicantUserId"])) && (typeof value["requestNumber"] === 'string') && (typeof value["status"] === 'string') && (typeof value["title"] === 'string') && ((typeof value["totalAmount"] === 'number' && Number.isFinite(value["totalAmount"])) || (typeof value["totalAmount"] === 'string'));
}

export function readCreateGoViewProjectRequest(value: unknown): CreateGoViewProjectRequest {
  if (!(isCreateGoViewProjectRequest(value))) {
    throw new Error('client.invalid_create_go_view_project_request');
  }
  return value;
}

function isCreateGoViewProjectRequest(value: unknown): value is CreateGoViewProjectRequest {
  return isRecord(value) && ((value["canvasJson"] === null) || (typeof value["canvasJson"] === 'string')) && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && (typeof value["projectKey"] === 'string');
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
  const normalizedValue = normalizeCreateHostDocumentCategoryRequestIntegerJson(value);
  if (!(isCreateHostDocumentCategoryRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_document_category_request');
  }
  return normalizedValue;
}

function isCreateHostDocumentCategoryRequest(value: unknown): value is CreateHostDocumentCategoryRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"]));
}

export function readCreateHostDocumentItemRequest(value: unknown): CreateHostDocumentItemRequest {
  const normalizedValue = normalizeCreateHostDocumentItemRequestIntegerJson(value);
  if (!(isCreateHostDocumentItemRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_document_item_request');
  }
  return normalizedValue;
}

function isCreateHostDocumentItemRequest(value: unknown): value is CreateHostDocumentItemRequest {
  return isRecord(value) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (isHostDocumentType(value["documentType"])) && (typeof value["sort"] === 'number' && Number.isSafeInteger(value["sort"])) && (isHostDocumentStatus(value["status"])) && ((value["tagIds"] === null) || (Array.isArray(value["tagIds"]) && value["tagIds"].every(item15 => typeof item15 === 'string' && guidPattern.test(item15)))) && ((value["thumbnail"] === null) || (typeof value["thumbnail"] === 'string')) && (typeof value["title"] === 'string');
}

export function readCreateHostDocumentPreviewTaskRequest(value: unknown): CreateHostDocumentPreviewTaskRequest {
  if (!(isCreateHostDocumentPreviewTaskRequest(value))) {
    throw new Error('client.invalid_create_host_document_preview_task_request');
  }
  return value;
}

function isCreateHostDocumentPreviewTaskRequest(value: unknown): value is CreateHostDocumentPreviewTaskRequest {
  return isRecord(value) && (typeof value["documentItemId"] === 'string' && guidPattern.test(value["documentItemId"])) && ((value["versionId"] === null) || (typeof value["versionId"] === 'string' && guidPattern.test(value["versionId"])));
}

export function readCreateHostDocumentShareRequest(value: unknown): CreateHostDocumentShareRequest {
  const normalizedValue = normalizeCreateHostDocumentShareRequestIntegerJson(value);
  if (!(isCreateHostDocumentShareRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_document_share_request');
  }
  return normalizedValue;
}

function isCreateHostDocumentShareRequest(value: unknown): value is CreateHostDocumentShareRequest {
  return isRecord(value) && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && (value["maxAccessCount"] === undefined || ((value["maxAccessCount"] === null) || (typeof value["maxAccessCount"] === 'number' && Number.isSafeInteger(value["maxAccessCount"])))) && (value["password"] === undefined || ((value["password"] === null) || (typeof value["password"] === 'string'))) && (typeof value["validDays"] === 'number' && Number.isSafeInteger(value["validDays"]));
}

export function readCreateHostDocumentTagRequest(value: unknown): CreateHostDocumentTagRequest {
  if (!(isCreateHostDocumentTagRequest(value))) {
    throw new Error('client.invalid_create_host_document_tag_request');
  }
  return value;
}

function isCreateHostDocumentTagRequest(value: unknown): value is CreateHostDocumentTagRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (value["isHot"] === undefined || (typeof value["isHot"] === 'boolean')) && (value["isRecommended"] === undefined || (typeof value["isRecommended"] === 'boolean')) && (typeof value["name"] === 'string');
}

export function readCreateHostFolderRequest(value: unknown): CreateHostFolderRequest {
  const normalizedValue = normalizeCreateHostFolderRequestIntegerJson(value);
  if (!(isCreateHostFolderRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_folder_request');
  }
  return normalizedValue;
}

function isCreateHostFolderRequest(value: unknown): value is CreateHostFolderRequest {
  return isRecord(value) && (value["displayOrder"] === undefined || (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"]))) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"])));
}

export function readCreateHostJobDefinitionRequest(value: unknown): CreateHostJobDefinitionRequest {
  const normalizedValue = normalizeCreateHostJobDefinitionRequestIntegerJson(value);
  if (!(isCreateHostJobDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_job_definition_request');
  }
  return normalizedValue;
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
  const normalizedValue = normalizeCreateHostMenuRequestIntegerJson(value);
  if (!(isCreateHostMenuRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_menu_request');
  }
  return normalizedValue;
}

function isCreateHostMenuRequest(value: unknown): value is CreateHostMenuRequest {
  return isRecord(value) && (typeof value["caption"] === 'string') && (typeof value["componentKey"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["icon"] === 'string') && (value["isAffix"] === undefined || (typeof value["isAffix"] === 'boolean')) && (value["isEmbedded"] === undefined || (typeof value["isEmbedded"] === 'boolean')) && (value["isHidden"] === undefined || (typeof value["isHidden"] === 'boolean')) && (value["isKeepAlive"] === undefined || (typeof value["isKeepAlive"] === 'boolean')) && (value["linkUrl"] === undefined || ((value["linkUrl"] === null) || (typeof value["linkUrl"] === 'string'))) && (value["menuType"] === undefined || (typeof value["menuType"] === 'string')) && ((value["parentId"] === null) || (typeof value["parentId"] === 'string')) && (typeof value["path"] === 'string') && (value["redirect"] === undefined || ((value["redirect"] === null) || (typeof value["redirect"] === 'string'))) && (value["remark"] === undefined || ((value["remark"] === null) || (typeof value["remark"] === 'string'))) && (typeof value["requiredPermission"] === 'string') && (typeof value["routeName"] === 'string') && (typeof value["title"] === 'string');
}

export function readCreateHostReleaseNoteRequest(value: unknown): CreateHostReleaseNoteRequest {
  if (!(isCreateHostReleaseNoteRequest(value))) {
    throw new Error('client.invalid_create_host_release_note_request');
  }
  return value;
}

function isCreateHostReleaseNoteRequest(value: unknown): value is CreateHostReleaseNoteRequest {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["title"] === 'string') && (typeof value["versionLabel"] === 'string');
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
  const normalizedValue = normalizeCreateHostUserRequestIntegerJson(value);
  if (!(isCreateHostUserRequest(normalizedValue))) {
    throw new Error('client.invalid_create_host_user_request');
  }
  return normalizedValue;
}

function isCreateHostUserRequest(value: unknown): value is CreateHostUserRequest {
  return isRecord(value) && (value["accountType"] === undefined || ((value["accountType"] === null) || (typeof value["accountType"] === 'string'))) && (typeof value["displayName"] === 'string') && (typeof value["password"] === 'string') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"])))) && (typeof value["username"] === 'string');
}

export function readCreateK3CloudConnectionConfigRequest(value: unknown): CreateK3CloudConnectionConfigRequest {
  const normalizedValue = normalizeCreateK3CloudConnectionConfigRequestIntegerJson(value);
  if (!(isCreateK3CloudConnectionConfigRequest(normalizedValue))) {
    throw new Error('client.invalid_create_k3_cloud_connection_config_request');
  }
  return normalizedValue;
}

function isCreateK3CloudConnectionConfigRequest(value: unknown): value is CreateK3CloudConnectionConfigRequest {
  return isRecord(value) && (typeof value["acctId"] === 'string') && (typeof value["baseUrl"] === 'string') && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["lcid"] === 'number' && Number.isSafeInteger(value["lcid"])) && (typeof value["name"] === 'string') && (typeof value["password"] === 'string') && (typeof value["username"] === 'string');
}

export function readCreateK3CloudDocumentSyncRequest(value: unknown): CreateK3CloudDocumentSyncRequest {
  if (!(isCreateK3CloudDocumentSyncRequest(value))) {
    throw new Error('client.invalid_create_k3_cloud_document_sync_request');
  }
  return value;
}

function isCreateK3CloudDocumentSyncRequest(value: unknown): value is CreateK3CloudDocumentSyncRequest {
  return isRecord(value) && (typeof value["businessKey"] === 'string') && (typeof value["connectionConfigId"] === 'string' && guidPattern.test(value["connectionConfigId"])) && (typeof value["documentTypeKey"] === 'string') && (typeof value["payloadJson"] === 'string');
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
  const normalizedValue = normalizeCreateNotificationBindingRequestIntegerJson(value);
  if (!(isCreateNotificationBindingRequest(normalizedValue))) {
    throw new Error('client.invalid_create_notification_binding_request');
  }
  return normalizedValue;
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
  const normalizedValue = normalizeCreateNotificationTemplateRequestIntegerJson(value);
  if (!(isCreateNotificationTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_create_notification_template_request');
  }
  return normalizedValue;
}

function isCreateNotificationTemplateRequest(value: unknown): value is CreateNotificationTemplateRequest {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["contentCategoryKey"] === 'string') && (value["defaultLocaleTag"] === undefined || ((value["defaultLocaleTag"] === null) || (typeof value["defaultLocaleTag"] === 'string'))) && (isNotificationTemplateBody(value["draftBody"])) && (typeof value["draftSubject"] === 'string') && (value["localeTag"] === undefined || ((value["localeTag"] === null) || (typeof value["localeTag"] === 'string'))) && (isNotificationTemplateParameterSchema(value["parameterSchema"])) && (typeof value["templateKey"] === 'string');
}

export function readCreateOcrIdCardTaskRequest(value: unknown): CreateOcrIdCardTaskRequest {
  if (!(isCreateOcrIdCardTaskRequest(value))) {
    throw new Error('client.invalid_create_ocr_id_card_task_request');
  }
  return value;
}

function isCreateOcrIdCardTaskRequest(value: unknown): value is CreateOcrIdCardTaskRequest {
  return isRecord(value) && (typeof value["sourceFileId"] === 'string' && guidPattern.test(value["sourceFileId"]));
}

export function readCreateOrganizationPositionLevelRequest(value: unknown): CreateOrganizationPositionLevelRequest {
  const normalizedValue = normalizeCreateOrganizationPositionLevelRequestIntegerJson(value);
  if (!(isCreateOrganizationPositionLevelRequest(normalizedValue))) {
    throw new Error('client.invalid_create_organization_position_level_request');
  }
  return normalizedValue;
}

function isCreateOrganizationPositionLevelRequest(value: unknown): value is CreateOrganizationPositionLevelRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string');
}

export function readCreateOrganizationPositionRequest(value: unknown): CreateOrganizationPositionRequest {
  const normalizedValue = normalizeCreateOrganizationPositionRequestIntegerJson(value);
  if (!(isCreateOrganizationPositionRequest(normalizedValue))) {
    throw new Error('client.invalid_create_organization_position_request');
  }
  return normalizedValue;
}

function isCreateOrganizationPositionRequest(value: unknown): value is CreateOrganizationPositionRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string');
}

export function readCreateOrganizationUnitRequest(value: unknown): CreateOrganizationUnitRequest {
  const normalizedValue = normalizeCreateOrganizationUnitRequestIntegerJson(value);
  if (!(isCreateOrganizationUnitRequest(normalizedValue))) {
    throw new Error('client.invalid_create_organization_unit_request');
  }
  return normalizedValue;
}

function isCreateOrganizationUnitRequest(value: unknown): value is CreateOrganizationUnitRequest {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string'));
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

export function readCreatePaymentMerchantConfigRequest(value: unknown): CreatePaymentMerchantConfigRequest {
  if (!(isCreatePaymentMerchantConfigRequest(value))) {
    throw new Error('client.invalid_create_payment_merchant_config_request');
  }
  return value;
}

function isCreatePaymentMerchantConfigRequest(value: unknown): value is CreatePaymentMerchantConfigRequest {
  return isRecord(value) && ((value["apiV3Key"] === null) || (typeof value["apiV3Key"] === 'string')) && (typeof value["appId"] === 'string') && (typeof value["certificateSerialNo"] === 'string') && (typeof value["channelKey"] === 'string') && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["merchantId"] === 'string') && (typeof value["name"] === 'string') && (typeof value["notifyUrl"] === 'string') && ((value["privateKeyPem"] === null) || (typeof value["privateKeyPem"] === 'string')) && (typeof value["returnUrl"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])));
}

export function readCreatePaymentOrderRequest(value: unknown): CreatePaymentOrderRequest {
  const normalizedValue = normalizeCreatePaymentOrderRequestIntegerJson(value);
  if (!(isCreatePaymentOrderRequest(normalizedValue))) {
    throw new Error('client.invalid_create_payment_order_request');
  }
  return normalizedValue;
}

function isCreatePaymentOrderRequest(value: unknown): value is CreatePaymentOrderRequest {
  return isRecord(value) && (typeof value["amountMinor"] === 'number' && Number.isSafeInteger(value["amountMinor"])) && ((value["channelKey"] === null) || (typeof value["channelKey"] === 'string')) && (typeof value["currency"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["merchantConfigId"] === null) || (typeof value["merchantConfigId"] === 'string' && guidPattern.test(value["merchantConfigId"]))) && (typeof value["subject"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]));
}

export function readCreatePaymentRefundRequest(value: unknown): CreatePaymentRefundRequest {
  const normalizedValue = normalizeCreatePaymentRefundRequestIntegerJson(value);
  if (!(isCreatePaymentRefundRequest(normalizedValue))) {
    throw new Error('client.invalid_create_payment_refund_request');
  }
  return normalizedValue;
}

function isCreatePaymentRefundRequest(value: unknown): value is CreatePaymentRefundRequest {
  return isRecord(value) && ((value["amountMinor"] === null) || (typeof value["amountMinor"] === 'number' && Number.isSafeInteger(value["amountMinor"]))) && (typeof value["reason"] === 'string');
}

export function readCreatePersonalScheduleRequest(value: unknown): CreatePersonalScheduleRequest {
  if (!(isCreatePersonalScheduleRequest(value))) {
    throw new Error('client.invalid_create_personal_schedule_request');
  }
  return value;
}

function isCreatePersonalScheduleRequest(value: unknown): value is CreatePersonalScheduleRequest {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["endAtUtc"] === 'string') && (typeof value["startAtUtc"] === 'string');
}

export function readCreatePrintingTemplateRequest(value: unknown): CreatePrintingTemplateRequest {
  if (!(isCreatePrintingTemplateRequest(value))) {
    throw new Error('client.invalid_create_printing_template_request');
  }
  return value;
}

function isCreatePrintingTemplateRequest(value: unknown): value is CreatePrintingTemplateRequest {
  return isRecord(value) && (typeof value["formSchemaKey"] === 'string') && (typeof value["isEnabled"] === 'boolean') && (typeof value["layoutHtml"] === 'string') && (typeof value["name"] === 'string') && (typeof value["templateKey"] === 'string');
}

export function readCreateReportingDataSourceRequest(value: unknown): CreateReportingDataSourceRequest {
  const normalizedValue = normalizeCreateReportingDataSourceRequestIntegerJson(value);
  if (!(isCreateReportingDataSourceRequest(normalizedValue))) {
    throw new Error('client.invalid_create_reporting_data_source_request');
  }
  return normalizedValue;
}

function isCreateReportingDataSourceRequest(value: unknown): value is CreateReportingDataSourceRequest {
  return isRecord(value) && (typeof value["databaseName"] === 'string') && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && (typeof value["password"] === 'string') && (typeof value["port"] === 'number' && Number.isSafeInteger(value["port"])) && (typeof value["providerKey"] === 'string') && (typeof value["serverHost"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["trustServerCertificate"] === 'boolean') && (typeof value["username"] === 'string');
}

export function readCreateReportingDefinitionRequest(value: unknown): CreateReportingDefinitionRequest {
  if (!(isCreateReportingDefinitionRequest(value))) {
    throw new Error('client.invalid_create_reporting_definition_request');
  }
  return value;
}

function isCreateReportingDefinitionRequest(value: unknown): value is CreateReportingDefinitionRequest {
  return isRecord(value) && (typeof value["dataSourceId"] === 'string' && guidPattern.test(value["dataSourceId"])) && (typeof value["definitionKey"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["groupId"] === 'string' && guidPattern.test(value["groupId"])) && (typeof value["isEnabled"] === 'boolean') && ((value["layoutConfigJson"] === null) || (typeof value["layoutConfigJson"] === 'string')) && (typeof value["name"] === 'string') && (Array.isArray(value["parameterSchema"]) && value["parameterSchema"].every(item24 => isReportingParameterSchemaEntry(item24))) && (typeof value["queryPortKey"] === 'string');
}

export function readCreateReportingExportTaskRequest(value: unknown): CreateReportingExportTaskRequest {
  const normalizedValue = normalizeCreateReportingExportTaskRequestIntegerJson(value);
  if (!(isCreateReportingExportTaskRequest(normalizedValue))) {
    throw new Error('client.invalid_create_reporting_export_task_request');
  }
  return normalizedValue;
}

function isCreateReportingExportTaskRequest(value: unknown): value is CreateReportingExportTaskRequest {
  return isRecord(value) && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["formatKey"] === 'string') && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isReportingExecutionParameterValue(item19))) && ((value["versionNumber"] === null) || (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"])));
}

export function readCreateReportingGroupRequest(value: unknown): CreateReportingGroupRequest {
  const normalizedValue = normalizeCreateReportingGroupRequestIntegerJson(value);
  if (!(isCreateReportingGroupRequest(normalizedValue))) {
    throw new Error('client.invalid_create_reporting_group_request');
  }
  return normalizedValue;
}

function isCreateReportingGroupRequest(value: unknown): value is CreateReportingGroupRequest {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"]));
}

export function readCreateSerialNumberRuleRequest(value: unknown): CreateSerialNumberRuleRequest {
  const normalizedValue = normalizeCreateSerialNumberRuleRequestIntegerJson(value);
  if (!(isCreateSerialNumberRuleRequest(normalizedValue))) {
    throw new Error('client.invalid_create_serial_number_rule_request');
  }
  return normalizedValue;
}

function isCreateSerialNumberRuleRequest(value: unknown): value is CreateSerialNumberRuleRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maximumValue"] === 'number' && Number.isSafeInteger(value["maximumValue"])) && (typeof value["minimumValue"] === 'number' && Number.isSafeInteger(value["minimumValue"])) && (typeof value["pattern"] === 'string') && (isSerialNumberResetInterval(value["resetInterval"])) && (typeof value["ruleKey"] === 'string') && (isSerialNumberRuleScope(value["scope"]));
}

export function readCreateWorkflowDefinitionRequest(value: unknown): CreateWorkflowDefinitionRequest {
  const normalizedValue = normalizeCreateWorkflowDefinitionRequestIntegerJson(value);
  if (!(isCreateWorkflowDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_create_workflow_definition_request');
  }
  return normalizedValue;
}

function isCreateWorkflowDefinitionRequest(value: unknown): value is CreateWorkflowDefinitionRequest {
  return isRecord(value) && (value["businessTitleTemplate"] === undefined || ((value["businessTitleTemplate"] === null) || (typeof value["businessTitleTemplate"] === 'string'))) && (typeof value["definitionKey"] === 'string') && (isWorkflowDefinitionDraft(value["draft"]));
}

export function readCreateWorkflowFormRequest(value: unknown): CreateWorkflowFormRequest {
  const normalizedValue = normalizeCreateWorkflowFormRequestIntegerJson(value);
  if (!(isCreateWorkflowFormRequest(normalizedValue))) {
    throw new Error('client.invalid_create_workflow_form_request');
  }
  return normalizedValue;
}

function isCreateWorkflowFormRequest(value: unknown): value is CreateWorkflowFormRequest {
  return isRecord(value) && (isWorkflowFormSchema(value["draft"])) && (typeof value["formKey"] === 'string');
}

export function readCurrentUserResponse(value: unknown): CurrentUserResponse {
  const normalizedValue = normalizeCurrentUserResponseIntegerJson(value);
  if (!(isCurrentUserResponse(normalizedValue))) {
    throw new Error('client.invalid_current_user_response');
  }
  return normalizedValue;
}

function isCurrentUserResponse(value: unknown): value is CurrentUserResponse {
  return isRecord(value) && (typeof value["actorScope"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isSuperAdministrator"] === 'boolean') && (typeof value["passwordChangeRequired"] === 'boolean') && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => typeof item20 === 'string')) && (typeof value["preferredLocale"] === 'string') && (typeof value["profileVersion"] === 'number' && Number.isSafeInteger(value["profileVersion"])) && (typeof value["scope"] === 'string') && (typeof value["sessionId"] === 'string' && guidPattern.test(value["sessionId"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["username"] === 'string');
}

export function readDataApprovalRequestResponse(value: unknown): DataApprovalRequestResponse {
  const normalizedValue = normalizeDataApprovalRequestResponseIntegerJson(value);
  if (!(isDataApprovalRequestResponse(normalizedValue))) {
    throw new Error('client.invalid_data_approval_request_response');
  }
  return normalizedValue;
}

function isDataApprovalRequestResponse(value: unknown): value is DataApprovalRequestResponse {
  return isRecord(value) && (typeof value["afterSnapshotJson"] === 'string') && (typeof value["applicationAttemptCount"] === 'number' && Number.isSafeInteger(value["applicationAttemptCount"])) && (typeof value["applicationStatusKey"] === 'string') && ((value["beforeSnapshotJson"] === null) || (typeof value["beforeSnapshotJson"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["lastApplicationAttemptAtUtc"] === null) || (typeof value["lastApplicationAttemptAtUtc"] === 'string')) && ((value["lastApplicationFailureCode"] === null) || (typeof value["lastApplicationFailureCode"] === 'string')) && ((value["lastApplicationFailureMessage"] === null) || (typeof value["lastApplicationFailureMessage"] === 'string')) && ((value["lastFailureCode"] === null) || (typeof value["lastFailureCode"] === 'string')) && ((value["lastFailureMessage"] === null) || (typeof value["lastFailureMessage"] === 'string')) && ((value["lastRecoveryAttemptAtUtc"] === null) || (typeof value["lastRecoveryAttemptAtUtc"] === 'string')) && (typeof value["recoveryAttemptCount"] === 'number' && Number.isSafeInteger(value["recoveryAttemptCount"])) && (typeof value["recoveryStatusKey"] === 'string') && ((value["resolvedAtUtc"] === null) || (typeof value["resolvedAtUtc"] === 'string')) && (typeof value["scenarioKey"] === 'string') && (typeof value["statusKey"] === 'string') && (typeof value["submittedAtUtc"] === 'string') && (typeof value["submittedByUserId"] === 'string' && guidPattern.test(value["submittedByUserId"])) && (typeof value["targetEntityId"] === 'string' && guidPattern.test(value["targetEntityId"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && (typeof value["workflowDefinitionVersionId"] === 'string' && guidPattern.test(value["workflowDefinitionVersionId"])) && ((value["workflowInstanceId"] === null) || (typeof value["workflowInstanceId"] === 'string' && guidPattern.test(value["workflowInstanceId"]))) && ((value["workflowRevision"] === null) || (typeof value["workflowRevision"] === 'number' && Number.isSafeInteger(value["workflowRevision"])));
}

export function readDataApprovalScenarioResponse(value: unknown): DataApprovalScenarioResponse {
  const normalizedValue = normalizeDataApprovalScenarioResponseIntegerJson(value);
  if (!(isDataApprovalScenarioResponse(normalizedValue))) {
    throw new Error('client.invalid_data_approval_scenario_response');
  }
  return normalizedValue;
}

function isDataApprovalScenarioResponse(value: unknown): value is DataApprovalScenarioResponse {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["isRegistered"] === 'boolean') && (typeof value["scenarioKey"] === 'string') && (typeof value["scopeKey"] === 'string') && ((value["version"] === null) || (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]))) && ((value["workflowDefinitionKey"] === null) || (typeof value["workflowDefinitionKey"] === 'string')) && ((value["workflowDefinitionVersionId"] === null) || (typeof value["workflowDefinitionVersionId"] === 'string' && guidPattern.test(value["workflowDefinitionVersionId"])));
}

export function readDecideAiAgentApprovalRequest(value: unknown): DecideAiAgentApprovalRequest {
  const normalizedValue = normalizeDecideAiAgentApprovalRequestIntegerJson(value);
  if (!(isDecideAiAgentApprovalRequest(normalizedValue))) {
    throw new Error('client.invalid_decide_ai_agent_approval_request');
  }
  return normalizedValue;
}

function isDecideAiAgentApprovalRequest(value: unknown): value is DecideAiAgentApprovalRequest {
  return isRecord(value) && (typeof value["approve"] === 'boolean') && (typeof value["expectedVersion"] === 'number' && Number.isSafeInteger(value["expectedVersion"]));
}

export function readDeleteAdministrativeRegionRequest(value: unknown): DeleteAdministrativeRegionRequest {
  const normalizedValue = normalizeDeleteAdministrativeRegionRequestIntegerJson(value);
  if (!(isDeleteAdministrativeRegionRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_administrative_region_request');
  }
  return normalizedValue;
}

function isDeleteAdministrativeRegionRequest(value: unknown): value is DeleteAdministrativeRegionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteCodeGenerationTemplateRequest(value: unknown): DeleteCodeGenerationTemplateRequest {
  const normalizedValue = normalizeDeleteCodeGenerationTemplateRequestIntegerJson(value);
  if (!(isDeleteCodeGenerationTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_code_generation_template_request');
  }
  return normalizedValue;
}

function isDeleteCodeGenerationTemplateRequest(value: unknown): value is DeleteCodeGenerationTemplateRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteConfigEntryRequest(value: unknown): DeleteConfigEntryRequest {
  const normalizedValue = normalizeDeleteConfigEntryRequestIntegerJson(value);
  if (!(isDeleteConfigEntryRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_config_entry_request');
  }
  return normalizedValue;
}

function isDeleteConfigEntryRequest(value: unknown): value is DeleteConfigEntryRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteDictItemRequest(value: unknown): DeleteDictItemRequest {
  const normalizedValue = normalizeDeleteDictItemRequestIntegerJson(value);
  if (!(isDeleteDictItemRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_dict_item_request');
  }
  return normalizedValue;
}

function isDeleteDictItemRequest(value: unknown): value is DeleteDictItemRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteDictTypeRequest(value: unknown): DeleteDictTypeRequest {
  const normalizedValue = normalizeDeleteDictTypeRequestIntegerJson(value);
  if (!(isDeleteDictTypeRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_dict_type_request');
  }
  return normalizedValue;
}

function isDeleteDictTypeRequest(value: unknown): value is DeleteDictTypeRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteEnterpriseRequestRequest(value: unknown): DeleteEnterpriseRequestRequest {
  const normalizedValue = normalizeDeleteEnterpriseRequestRequestIntegerJson(value);
  if (!(isDeleteEnterpriseRequestRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_enterprise_request_request');
  }
  return normalizedValue;
}

function isDeleteEnterpriseRequestRequest(value: unknown): value is DeleteEnterpriseRequestRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteHostDocumentCategoryRequest(value: unknown): DeleteHostDocumentCategoryRequest {
  const normalizedValue = normalizeDeleteHostDocumentCategoryRequestIntegerJson(value);
  if (!(isDeleteHostDocumentCategoryRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_document_category_request');
  }
  return normalizedValue;
}

function isDeleteHostDocumentCategoryRequest(value: unknown): value is DeleteHostDocumentCategoryRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteHostDocumentItemRequest(value: unknown): DeleteHostDocumentItemRequest {
  const normalizedValue = normalizeDeleteHostDocumentItemRequestIntegerJson(value);
  if (!(isDeleteHostDocumentItemRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_document_item_request');
  }
  return normalizedValue;
}

function isDeleteHostDocumentItemRequest(value: unknown): value is DeleteHostDocumentItemRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteHostDocumentTagRequest(value: unknown): DeleteHostDocumentTagRequest {
  const normalizedValue = normalizeDeleteHostDocumentTagRequestIntegerJson(value);
  if (!(isDeleteHostDocumentTagRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_document_tag_request');
  }
  return normalizedValue;
}

function isDeleteHostDocumentTagRequest(value: unknown): value is DeleteHostDocumentTagRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteHostDocumentVersionRequest(value: unknown): DeleteHostDocumentVersionRequest {
  const normalizedValue = normalizeDeleteHostDocumentVersionRequestIntegerJson(value);
  if (!(isDeleteHostDocumentVersionRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_document_version_request');
  }
  return normalizedValue;
}

function isDeleteHostDocumentVersionRequest(value: unknown): value is DeleteHostDocumentVersionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteHostFolderRequest(value: unknown): DeleteHostFolderRequest {
  const normalizedValue = normalizeDeleteHostFolderRequestIntegerJson(value);
  if (!(isDeleteHostFolderRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_folder_request');
  }
  return normalizedValue;
}

function isDeleteHostFolderRequest(value: unknown): value is DeleteHostFolderRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"]));
}

export function readDeleteHostJobDefinitionRequest(value: unknown): DeleteHostJobDefinitionRequest {
  const normalizedValue = normalizeDeleteHostJobDefinitionRequestIntegerJson(value);
  if (!(isDeleteHostJobDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_job_definition_request');
  }
  return normalizedValue;
}

function isDeleteHostJobDefinitionRequest(value: unknown): value is DeleteHostJobDefinitionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDeleteHostReleaseNoteRequest(value: unknown): DeleteHostReleaseNoteRequest {
  const normalizedValue = normalizeDeleteHostReleaseNoteRequestIntegerJson(value);
  if (!(isDeleteHostReleaseNoteRequest(normalizedValue))) {
    throw new Error('client.invalid_delete_host_release_note_request');
  }
  return normalizedValue;
}

function isDeleteHostReleaseNoteRequest(value: unknown): value is DeleteHostReleaseNoteRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDiagnosticPolicyResponse(value: unknown): DiagnosticPolicyResponse {
  const normalizedValue = normalizeDiagnosticPolicyResponseIntegerJson(value);
  if (!(isDiagnosticPolicyResponse(normalizedValue))) {
    throw new Error('client.invalid_diagnostic_policy_response');
  }
  return normalizedValue;
}

function isDiagnosticPolicyResponse(value: unknown): value is DiagnosticPolicyResponse {
  return isRecord(value) && (Array.isArray(value["activeRules"]) && value["activeRules"].every(item20 => isDiagnosticPolicyRuleResponse(item20))) && (typeof value["configEntryVersion"] === 'number' && Number.isSafeInteger(value["configEntryVersion"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["loadedAtUtc"] === 'string') && (typeof value["pressureState"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDiagnosticPolicyRuleRequest(value: unknown): DiagnosticPolicyRuleRequest {
  const normalizedValue = normalizeDiagnosticPolicyRuleRequestIntegerJson(value);
  if (!(isDiagnosticPolicyRuleRequest(normalizedValue))) {
    throw new Error('client.invalid_diagnostic_policy_rule_request');
  }
  return normalizedValue;
}

function isDiagnosticPolicyRuleRequest(value: unknown): value is DiagnosticPolicyRuleRequest {
  return isRecord(value) && ((value["bestEffortCapacityOverride"] === null) || (typeof value["bestEffortCapacityOverride"] === 'number' && Number.isSafeInteger(value["bestEffortCapacityOverride"]))) && (typeof value["expiresAtUtc"] === 'string') && ((value["maxRequestPayloadBytesOverride"] === null) || (typeof value["maxRequestPayloadBytesOverride"] === 'number' && Number.isSafeInteger(value["maxRequestPayloadBytesOverride"]))) && ((value["maxResponsePayloadBytesOverride"] === null) || (typeof value["maxResponsePayloadBytesOverride"] === 'number' && Number.isSafeInteger(value["maxResponsePayloadBytesOverride"]))) && (typeof value["scopeKind"] === 'string') && (typeof value["scopeValue"] === 'string') && ((value["successSampleRateOverride"] === null) || (typeof value["successSampleRateOverride"] === 'number' && Number.isFinite(value["successSampleRateOverride"])) || (typeof value["successSampleRateOverride"] === 'string'));
}

export function readDiagnosticPolicyRuleResponse(value: unknown): DiagnosticPolicyRuleResponse {
  const normalizedValue = normalizeDiagnosticPolicyRuleResponseIntegerJson(value);
  if (!(isDiagnosticPolicyRuleResponse(normalizedValue))) {
    throw new Error('client.invalid_diagnostic_policy_rule_response');
  }
  return normalizedValue;
}

function isDiagnosticPolicyRuleResponse(value: unknown): value is DiagnosticPolicyRuleResponse {
  return isRecord(value) && ((value["bestEffortCapacityOverride"] === null) || (typeof value["bestEffortCapacityOverride"] === 'number' && Number.isSafeInteger(value["bestEffortCapacityOverride"]))) && (typeof value["expiresAtUtc"] === 'string') && ((value["maxRequestPayloadBytesOverride"] === null) || (typeof value["maxRequestPayloadBytesOverride"] === 'number' && Number.isSafeInteger(value["maxRequestPayloadBytesOverride"]))) && ((value["maxResponsePayloadBytesOverride"] === null) || (typeof value["maxResponsePayloadBytesOverride"] === 'number' && Number.isSafeInteger(value["maxResponsePayloadBytesOverride"]))) && (typeof value["scopeKind"] === 'string') && (typeof value["scopeValue"] === 'string') && ((value["successSampleRateOverride"] === null) || (typeof value["successSampleRateOverride"] === 'number' && Number.isFinite(value["successSampleRateOverride"])) || (typeof value["successSampleRateOverride"] === 'string'));
}

export function readDictItemResponse(value: unknown): DictItemResponse {
  const normalizedValue = normalizeDictItemResponseIntegerJson(value);
  if (!(isDictItemResponse(normalizedValue))) {
    throw new Error('client.invalid_dict_item_response');
  }
  return normalizedValue;
}

function isDictItemResponse(value: unknown): value is DictItemResponse {
  return isRecord(value) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["dictTypeId"] === 'string' && guidPattern.test(value["dictTypeId"])) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["label"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDictTypeResponse(value: unknown): DictTypeResponse {
  const normalizedValue = normalizeDictTypeResponseIntegerJson(value);
  if (!(isDictTypeResponse(normalizedValue))) {
    throw new Error('client.invalid_dict_type_response');
  }
  return normalizedValue;
}

function isDictTypeResponse(value: unknown): value is DictTypeResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readDisableHostJobDefinitionRequest(value: unknown): DisableHostJobDefinitionRequest {
  const normalizedValue = normalizeDisableHostJobDefinitionRequestIntegerJson(value);
  if (!(isDisableHostJobDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_disable_host_job_definition_request');
  }
  return normalizedValue;
}

function isDisableHostJobDefinitionRequest(value: unknown): value is DisableHostJobDefinitionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readEnterpriseRequestResponse(value: unknown): EnterpriseRequestResponse {
  const normalizedValue = normalizeEnterpriseRequestResponseIntegerJson(value);
  if (!(isEnterpriseRequestResponse(normalizedValue))) {
    throw new Error('client.invalid_enterprise_request_response');
  }
  return normalizedValue;
}

function isEnterpriseRequestResponse(value: unknown): value is EnterpriseRequestResponse {
  return isRecord(value) && (typeof value["applicantUserId"] === 'string' && guidPattern.test(value["applicantUserId"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdById"] === 'string' && guidPattern.test(value["createdById"])) && ((value["deletedAtUtc"] === null) || (typeof value["deletedAtUtc"] === 'string')) && ((value["deletedById"] === null) || (typeof value["deletedById"] === 'string' && guidPattern.test(value["deletedById"]))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDeleted"] === 'boolean') && (typeof value["organizationUnitId"] === 'string' && guidPattern.test(value["organizationUnitId"])) && (typeof value["requestNumber"] === 'string') && (typeof value["status"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["title"] === 'string') && ((typeof value["totalAmount"] === 'number' && Number.isFinite(value["totalAmount"])) || (typeof value["totalAmount"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedById"] === null) || (typeof value["updatedById"] === 'string' && guidPattern.test(value["updatedById"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readEnumCatalogDetail(value: unknown): EnumCatalogDetail {
  const normalizedValue = normalizeEnumCatalogDetailIntegerJson(value);
  if (!(isEnumCatalogDetail(normalizedValue))) {
    throw new Error('client.invalid_enum_catalog_detail');
  }
  return normalizedValue;
}

function isEnumCatalogDetail(value: unknown): value is EnumCatalogDetail {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["key"] === 'string') && (Array.isArray(value["members"]) && value["members"].every(item16 => isEnumCatalogMember(item16)));
}

export function readEnumCatalogDictGenerationItemPreview(value: unknown): EnumCatalogDictGenerationItemPreview {
  const normalizedValue = normalizeEnumCatalogDictGenerationItemPreviewIntegerJson(value);
  if (!(isEnumCatalogDictGenerationItemPreview(normalizedValue))) {
    throw new Error('client.invalid_enum_catalog_dict_generation_item_preview');
  }
  return normalizedValue;
}

function isEnumCatalogDictGenerationItemPreview(value: unknown): value is EnumCatalogDictGenerationItemPreview {
  return isRecord(value) && (typeof value["action"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && ((value["existingLabel"] === null) || (typeof value["existingLabel"] === 'string')) && (typeof value["proposedLabel"] === 'string') && (typeof value["value"] === 'string');
}

export function readEnumCatalogDictGenerationPreview(value: unknown): EnumCatalogDictGenerationPreview {
  const normalizedValue = normalizeEnumCatalogDictGenerationPreviewIntegerJson(value);
  if (!(isEnumCatalogDictGenerationPreview(normalizedValue))) {
    throw new Error('client.invalid_enum_catalog_dict_generation_preview');
  }
  return normalizedValue;
}

function isEnumCatalogDictGenerationPreview(value: unknown): value is EnumCatalogDictGenerationPreview {
  return isRecord(value) && (typeof value["catalogKey"] === 'string') && (typeof value["dictTypeCode"] === 'string') && (typeof value["dictTypeExists"] === 'boolean') && (typeof value["dictTypeName"] === 'string') && (Array.isArray(value["items"]) && value["items"].every(item14 => isEnumCatalogDictGenerationItemPreview(item14))) && (Array.isArray(value["unmanagedItems"]) && value["unmanagedItems"].every(item23 => isEnumCatalogDictGenerationUnmanagedItem(item23))) && (typeof value["willCreateDictType"] === 'boolean');
}

export function readEnumCatalogDictGenerationResult(value: unknown): EnumCatalogDictGenerationResult {
  const normalizedValue = normalizeEnumCatalogDictGenerationResultIntegerJson(value);
  if (!(isEnumCatalogDictGenerationResult(normalizedValue))) {
    throw new Error('client.invalid_enum_catalog_dict_generation_result');
  }
  return normalizedValue;
}

function isEnumCatalogDictGenerationResult(value: unknown): value is EnumCatalogDictGenerationResult {
  return isRecord(value) && (typeof value["catalogKey"] === 'string') && (typeof value["dictTypeCode"] === 'string') && (typeof value["dictTypeCreated"] === 'boolean') && ((value["dictTypeId"] === null) || (typeof value["dictTypeId"] === 'string' && guidPattern.test(value["dictTypeId"]))) && (Array.isArray(value["items"]) && value["items"].every(item14 => isEnumCatalogDictGenerationItemPreview(item14))) && (typeof value["itemsConflicted"] === 'number' && Number.isSafeInteger(value["itemsConflicted"])) && (typeof value["itemsCreated"] === 'number' && Number.isSafeInteger(value["itemsCreated"])) && (typeof value["itemsInvalid"] === 'number' && Number.isSafeInteger(value["itemsInvalid"])) && (typeof value["itemsSkipped"] === 'number' && Number.isSafeInteger(value["itemsSkipped"]));
}

export function readEnumCatalogDictGenerationUnmanagedItem(value: unknown): EnumCatalogDictGenerationUnmanagedItem {
  if (!(isEnumCatalogDictGenerationUnmanagedItem(value))) {
    throw new Error('client.invalid_enum_catalog_dict_generation_unmanaged_item');
  }
  return value;
}

function isEnumCatalogDictGenerationUnmanagedItem(value: unknown): value is EnumCatalogDictGenerationUnmanagedItem {
  return isRecord(value) && (typeof value["isActive"] === 'boolean') && (typeof value["label"] === 'string') && (typeof value["value"] === 'string');
}

export function readEnumCatalogMember(value: unknown): EnumCatalogMember {
  const normalizedValue = normalizeEnumCatalogMemberIntegerJson(value);
  if (!(isEnumCatalogMember(normalizedValue))) {
    throw new Error('client.invalid_enum_catalog_member');
  }
  return normalizedValue;
}

function isEnumCatalogMember(value: unknown): value is EnumCatalogMember {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["label"] === 'string');
}

export function readEnumCatalogSummary(value: unknown): EnumCatalogSummary {
  const normalizedValue = normalizeEnumCatalogSummaryIntegerJson(value);
  if (!(isEnumCatalogSummary(normalizedValue))) {
    throw new Error('client.invalid_enum_catalog_summary');
  }
  return normalizedValue;
}

function isEnumCatalogSummary(value: unknown): value is EnumCatalogSummary {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["key"] === 'string') && (typeof value["memberCount"] === 'number' && Number.isSafeInteger(value["memberCount"]));
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

export function readExecuteReportingDefinitionRequest(value: unknown): ExecuteReportingDefinitionRequest {
  const normalizedValue = normalizeExecuteReportingDefinitionRequestIntegerJson(value);
  if (!(isExecuteReportingDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_execute_reporting_definition_request');
  }
  return normalizedValue;
}

function isExecuteReportingDefinitionRequest(value: unknown): value is ExecuteReportingDefinitionRequest {
  return isRecord(value) && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isReportingExecutionParameterValue(item19))) && ((value["versionNumber"] === null) || (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"])));
}

export function readFieldProjectionDefaultVisibility(value: unknown): FieldProjectionDefaultVisibility {
  if (!(isFieldProjectionDefaultVisibility(value))) {
    throw new Error('client.invalid_field_projection_default_visibility');
  }
  return value;
}

function isFieldProjectionDefaultVisibility(value: unknown): value is FieldProjectionDefaultVisibility {
  return typeof value === 'number' && Number.isSafeInteger(value);
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
  return typeof value === 'number' && Number.isSafeInteger(value);
}

export function readGoViewProjectPreviewResponse(value: unknown): GoViewProjectPreviewResponse {
  const normalizedValue = normalizeGoViewProjectPreviewResponseIntegerJson(value);
  if (!(isGoViewProjectPreviewResponse(normalizedValue))) {
    throw new Error('client.invalid_go_view_project_preview_response');
  }
  return normalizedValue;
}

function isGoViewProjectPreviewResponse(value: unknown): value is GoViewProjectPreviewResponse {
  return isRecord(value) && (typeof value["canvasJson"] === 'string') && (typeof value["generatedAtUtc"] === 'string') && (typeof value["projectId"] === 'string' && guidPattern.test(value["projectId"])) && (typeof value["projectKey"] === 'string') && (typeof value["projectName"] === 'string') && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readGoViewProjectResponse(value: unknown): GoViewProjectResponse {
  const normalizedValue = normalizeGoViewProjectResponseIntegerJson(value);
  if (!(isGoViewProjectResponse(normalizedValue))) {
    throw new Error('client.invalid_go_view_project_response');
  }
  return normalizedValue;
}

function isGoViewProjectResponse(value: unknown): value is GoViewProjectResponse {
  return isRecord(value) && (typeof value["canvasJson"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isSafeInteger(value["latestPublishedVersionNumber"])) && (typeof value["name"] === 'string') && (typeof value["projectKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readGoViewProjectVersionResponse(value: unknown): GoViewProjectVersionResponse {
  const normalizedValue = normalizeGoViewProjectVersionResponseIntegerJson(value);
  if (!(isGoViewProjectVersionResponse(normalizedValue))) {
    throw new Error('client.invalid_go_view_project_version_response');
  }
  return normalizedValue;
}

function isGoViewProjectVersionResponse(value: unknown): value is GoViewProjectVersionResponse {
  return isRecord(value) && (typeof value["canvasJson"] === 'string') && ((value["changeNote"] === null) || (typeof value["changeNote"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["projectId"] === 'string' && guidPattern.test(value["projectId"])) && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"])) && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
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

export function readHostAnnouncementReadReceiptResponse(value: unknown): HostAnnouncementReadReceiptResponse {
  if (!(isHostAnnouncementReadReceiptResponse(value))) {
    throw new Error('client.invalid_host_announcement_read_receipt_response');
  }
  return value;
}

function isHostAnnouncementReadReceiptResponse(value: unknown): value is HostAnnouncementReadReceiptResponse {
  return isRecord(value) && ((value["displayName"] === null) || (typeof value["displayName"] === 'string')) && (typeof value["readAtUtc"] === 'string') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && ((value["username"] === null) || (typeof value["username"] === 'string'));
}

export function readHostAnnouncementReadStatsResponse(value: unknown): HostAnnouncementReadStatsResponse {
  const normalizedValue = normalizeHostAnnouncementReadStatsResponseIntegerJson(value);
  if (!(isHostAnnouncementReadStatsResponse(normalizedValue))) {
    throw new Error('client.invalid_host_announcement_read_stats_response');
  }
  return normalizedValue;
}

function isHostAnnouncementReadStatsResponse(value: unknown): value is HostAnnouncementReadStatsResponse {
  return isRecord(value) && (typeof value["eligibleRecipientCount"] === 'number' && Number.isSafeInteger(value["eligibleRecipientCount"])) && (typeof value["readCount"] === 'number' && Number.isSafeInteger(value["readCount"])) && (typeof value["unreadCount"] === 'number' && Number.isSafeInteger(value["unreadCount"]));
}

export function readHostAnnouncementResponse(value: unknown): HostAnnouncementResponse {
  const normalizedValue = normalizeHostAnnouncementResponseIntegerJson(value);
  if (!(isHostAnnouncementResponse(normalizedValue))) {
    throw new Error('client.invalid_host_announcement_response');
  }
  return normalizedValue;
}

function isHostAnnouncementResponse(value: unknown): value is HostAnnouncementResponse {
  return isRecord(value) && (typeof value["audienceKind"] === 'string') && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["kind"] === 'string') && ((value["publishedAtUtc"] === null) || (typeof value["publishedAtUtc"] === 'string')) && ((value["publishedByUserId"] === null) || (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"]))) && ((value["retractedAtUtc"] === null) || (typeof value["retractedAtUtc"] === 'string')) && ((value["retractedByUserId"] === null) || (typeof value["retractedByUserId"] === 'string' && guidPattern.test(value["retractedByUserId"]))) && (typeof value["status"] === 'string') && (Array.isArray(value["targetOrganizations"]) && value["targetOrganizations"].every(item28 => isHostAnnouncementTargetOrganization(item28))) && (Array.isArray(value["targetUserIds"]) && value["targetUserIds"].every(item22 => typeof item22 === 'string' && guidPattern.test(item22))) && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
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

export function readHostAnnouncementUnreadCountResponse(value: unknown): HostAnnouncementUnreadCountResponse {
  const normalizedValue = normalizeHostAnnouncementUnreadCountResponseIntegerJson(value);
  if (!(isHostAnnouncementUnreadCountResponse(normalizedValue))) {
    throw new Error('client.invalid_host_announcement_unread_count_response');
  }
  return normalizedValue;
}

function isHostAnnouncementUnreadCountResponse(value: unknown): value is HostAnnouncementUnreadCountResponse {
  return isRecord(value) && (typeof value["unreadCount"] === 'number' && Number.isSafeInteger(value["unreadCount"]));
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

export function readHostDashboardBusinessEntryResponse(value: unknown): HostDashboardBusinessEntryResponse {
  const normalizedValue = normalizeHostDashboardBusinessEntryResponseIntegerJson(value);
  if (!(isHostDashboardBusinessEntryResponse(normalizedValue))) {
    throw new Error('client.invalid_host_dashboard_business_entry_response');
  }
  return normalizedValue;
}

function isHostDashboardBusinessEntryResponse(value: unknown): value is HostDashboardBusinessEntryResponse {
  return isRecord(value) && (typeof value["count"] === 'number' && Number.isSafeInteger(value["count"])) && (typeof value["entryKey"] === 'string') && (typeof value["requiredPermission"] === 'string') && (typeof value["routePath"] === 'string');
}

export function readHostDashboardSummaryResponse(value: unknown): HostDashboardSummaryResponse {
  const normalizedValue = normalizeHostDashboardSummaryResponseIntegerJson(value);
  if (!(isHostDashboardSummaryResponse(normalizedValue))) {
    throw new Error('client.invalid_host_dashboard_summary_response');
  }
  return normalizedValue;
}

function isHostDashboardSummaryResponse(value: unknown): value is HostDashboardSummaryResponse {
  return isRecord(value) && ((value["accessTrafficTrend"] === null) || (isHostDashboardTrafficTrendResponse(value["accessTrafficTrend"]))) && ((value["activeTenantCount"] === null) || (typeof value["activeTenantCount"] === 'number' && Number.isSafeInteger(value["activeTenantCount"]))) && (Array.isArray(value["businessEntries"]) && value["businessEntries"].every(item24 => isHostDashboardBusinessEntryResponse(item24))) && ((value["onlineSessionCount"] === null) || (typeof value["onlineSessionCount"] === 'number' && Number.isSafeInteger(value["onlineSessionCount"]))) && ((value["recentActivities"] === null) || (Array.isArray(value["recentActivities"]) && value["recentActivities"].every(item25 => isHostDashboardActivityResponse(item25)))) && ((value["todayErrorRate"] === null) || (typeof value["todayErrorRate"] === 'number' && Number.isFinite(value["todayErrorRate"])) || (typeof value["todayErrorRate"] === 'string')) && ((value["todayRequestCount"] === null) || (typeof value["todayRequestCount"] === 'number' && Number.isSafeInteger(value["todayRequestCount"])));
}

export function readHostDashboardTrafficTrendBucketResponse(value: unknown): HostDashboardTrafficTrendBucketResponse {
  const normalizedValue = normalizeHostDashboardTrafficTrendBucketResponseIntegerJson(value);
  if (!(isHostDashboardTrafficTrendBucketResponse(normalizedValue))) {
    throw new Error('client.invalid_host_dashboard_traffic_trend_bucket_response');
  }
  return normalizedValue;
}

function isHostDashboardTrafficTrendBucketResponse(value: unknown): value is HostDashboardTrafficTrendBucketResponse {
  return isRecord(value) && (typeof value["bucketStartUtc"] === 'string') && (typeof value["errorCount"] === 'number' && Number.isSafeInteger(value["errorCount"])) && (typeof value["eventCount"] === 'number' && Number.isSafeInteger(value["eventCount"]));
}

export function readHostDashboardTrafficTrendResponse(value: unknown): HostDashboardTrafficTrendResponse {
  const normalizedValue = normalizeHostDashboardTrafficTrendResponseIntegerJson(value);
  if (!(isHostDashboardTrafficTrendResponse(normalizedValue))) {
    throw new Error('client.invalid_host_dashboard_traffic_trend_response');
  }
  return normalizedValue;
}

function isHostDashboardTrafficTrendResponse(value: unknown): value is HostDashboardTrafficTrendResponse {
  return isRecord(value) && (Array.isArray(value["buckets"]) && value["buckets"].every(item16 => isHostDashboardTrafficTrendBucketResponse(item16))) && (typeof value["bucketSizeMinutes"] === 'number' && Number.isSafeInteger(value["bucketSizeMinutes"])) && (typeof value["fromUtc"] === 'string') && (typeof value["toUtc"] === 'string');
}

export function readHostDocumentAccessLogResponse(value: unknown): HostDocumentAccessLogResponse {
  if (!(isHostDocumentAccessLogResponse(value))) {
    throw new Error('client.invalid_host_document_access_log_response');
  }
  return value;
}

function isHostDocumentAccessLogResponse(value: unknown): value is HostDocumentAccessLogResponse {
  return isRecord(value) && (typeof value["accessTypeKey"] === 'string') && ((value["actorUserId"] === null) || (typeof value["actorUserId"] === 'string' && guidPattern.test(value["actorUserId"]))) && ((value["clientIpFingerprint"] === null) || (typeof value["clientIpFingerprint"] === 'string')) && (typeof value["documentItemId"] === 'string' && guidPattern.test(value["documentItemId"])) && (typeof value["documentTitle"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && (typeof value["sourceKey"] === 'string');
}

export function readHostDocumentCategoryResponse(value: unknown): HostDocumentCategoryResponse {
  const normalizedValue = normalizeHostDocumentCategoryResponseIntegerJson(value);
  if (!(isHostDocumentCategoryResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_category_response');
  }
  return normalizedValue;
}

function isHostDocumentCategoryResponse(value: unknown): value is HostDocumentCategoryResponse {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostDocumentItemResponse(value: unknown): HostDocumentItemResponse {
  const normalizedValue = normalizeHostDocumentItemResponseIntegerJson(value);
  if (!(isHostDocumentItemResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_item_response');
  }
  return normalizedValue;
}

function isHostDocumentItemResponse(value: unknown): value is HostDocumentItemResponse {
  return isRecord(value) && (typeof value["accessCount"] === 'number' && Number.isSafeInteger(value["accessCount"])) && ((value["categoryColor"] === null) || (typeof value["categoryColor"] === 'string')) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["categoryName"] === null) || (typeof value["categoryName"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["currentVersion"] === null) || (isHostDocumentVersionResponse(value["currentVersion"]))) && ((value["deletedAtUtc"] === null) || (typeof value["deletedAtUtc"] === 'string')) && ((value["deletedByUserId"] === null) || (typeof value["deletedByUserId"] === 'string' && guidPattern.test(value["deletedByUserId"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["documentNo"] === 'string') && (isHostDocumentType(value["documentType"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["lastAccessTime"] === null) || (typeof value["lastAccessTime"] === 'string')) && (typeof value["sizeKb"] === 'number' && Number.isSafeInteger(value["sizeKb"])) && (typeof value["sort"] === 'number' && Number.isSafeInteger(value["sort"])) && (isHostDocumentStatus(value["status"])) && (Array.isArray(value["tags"]) && value["tags"].every(item13 => isHostDocumentTagAssignmentResponse(item13))) && ((value["thumbnail"] === null) || (typeof value["thumbnail"] === 'string')) && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
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

export function readHostDocumentPreviewTaskResponse(value: unknown): HostDocumentPreviewTaskResponse {
  const normalizedValue = normalizeHostDocumentPreviewTaskResponseIntegerJson(value);
  if (!(isHostDocumentPreviewTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_preview_task_response');
  }
  return normalizedValue;
}

function isHostDocumentPreviewTaskResponse(value: unknown): value is HostDocumentPreviewTaskResponse {
  return isRecord(value) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["documentItemId"] === 'string' && guidPattern.test(value["documentItemId"])) && (typeof value["documentTitle"] === 'string') && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["outputFileId"] === null) || (typeof value["outputFileId"] === 'string' && guidPattern.test(value["outputFileId"]))) && (typeof value["providerKey"] === 'string') && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && (typeof value["sourceFileId"] === 'string' && guidPattern.test(value["sourceFileId"])) && ((value["startedAtUtc"] === null) || (typeof value["startedAtUtc"] === 'string')) && (typeof value["statusKey"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && ((value["versionId"] === null) || (typeof value["versionId"] === 'string' && guidPattern.test(value["versionId"])));
}

export function readHostDocumentShareAccessResponse(value: unknown): HostDocumentShareAccessResponse {
  const normalizedValue = normalizeHostDocumentShareAccessResponseIntegerJson(value);
  if (!(isHostDocumentShareAccessResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_share_access_response');
  }
  return normalizedValue;
}

function isHostDocumentShareAccessResponse(value: unknown): value is HostDocumentShareAccessResponse {
  return isRecord(value) && (typeof value["accessCountRemaining"] === 'number' && Number.isSafeInteger(value["accessCountRemaining"])) && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && ((value["fileName"] === null) || (typeof value["fileName"] === 'string')) && (typeof value["fileSizeBytes"] === 'number' && Number.isSafeInteger(value["fileSizeBytes"])) && (typeof value["hasPassword"] === 'boolean') && ((value["mimeType"] === null) || (typeof value["mimeType"] === 'string')) && (typeof value["shareCode"] === 'string') && (typeof value["shareId"] === 'string' && guidPattern.test(value["shareId"])) && (typeof value["title"] === 'string');
}

export function readHostDocumentShareResponse(value: unknown): HostDocumentShareResponse {
  const normalizedValue = normalizeHostDocumentShareResponseIntegerJson(value);
  if (!(isHostDocumentShareResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_share_response');
  }
  return normalizedValue;
}

function isHostDocumentShareResponse(value: unknown): value is HostDocumentShareResponse {
  return isRecord(value) && (typeof value["accessCount"] === 'number' && Number.isSafeInteger(value["accessCount"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["documentId"] === 'string' && guidPattern.test(value["documentId"])) && (typeof value["expireTime"] === 'string') && (typeof value["hasPassword"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["maxAccessCount"] === null) || (typeof value["maxAccessCount"] === 'number' && Number.isSafeInteger(value["maxAccessCount"]))) && (typeof value["shareCode"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostDocumentStatisticsCategoryItem(value: unknown): HostDocumentStatisticsCategoryItem {
  const normalizedValue = normalizeHostDocumentStatisticsCategoryItemIntegerJson(value);
  if (!(isHostDocumentStatisticsCategoryItem(normalizedValue))) {
    throw new Error('client.invalid_host_document_statistics_category_item');
  }
  return normalizedValue;
}

function isHostDocumentStatisticsCategoryItem(value: unknown): value is HostDocumentStatisticsCategoryItem {
  return isRecord(value) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["categoryName"] === null) || (typeof value["categoryName"] === 'string')) && (typeof value["count"] === 'number' && Number.isSafeInteger(value["count"]));
}

export function readHostDocumentStatisticsResponse(value: unknown): HostDocumentStatisticsResponse {
  const normalizedValue = normalizeHostDocumentStatisticsResponseIntegerJson(value);
  if (!(isHostDocumentStatisticsResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_statistics_response');
  }
  return normalizedValue;
}

function isHostDocumentStatisticsResponse(value: unknown): value is HostDocumentStatisticsResponse {
  return isRecord(value) && (Array.isArray(value["byCategory"]) && value["byCategory"].every(item19 => isHostDocumentStatisticsCategoryItem(item19))) && (Array.isArray(value["byType"]) && value["byType"].every(item15 => isHostDocumentStatisticsTypeItem(item15))) && (typeof value["recycleBinCount"] === 'number' && Number.isSafeInteger(value["recycleBinCount"])) && (typeof value["shareCount"] === 'number' && Number.isSafeInteger(value["shareCount"])) && (isHostDocumentStatisticsSummaryResponse(value["summary"])) && (typeof value["todayAccessCount"] === 'number' && Number.isSafeInteger(value["todayAccessCount"])) && (typeof value["todayCreatedCount"] === 'number' && Number.isSafeInteger(value["todayCreatedCount"])) && (typeof value["todayDownloadCount"] === 'number' && Number.isSafeInteger(value["todayDownloadCount"]));
}

export function readHostDocumentStatisticsSummaryResponse(value: unknown): HostDocumentStatisticsSummaryResponse {
  const normalizedValue = normalizeHostDocumentStatisticsSummaryResponseIntegerJson(value);
  if (!(isHostDocumentStatisticsSummaryResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_statistics_summary_response');
  }
  return normalizedValue;
}

function isHostDocumentStatisticsSummaryResponse(value: unknown): value is HostDocumentStatisticsSummaryResponse {
  return isRecord(value) && (typeof value["totalItems"] === 'number' && Number.isSafeInteger(value["totalItems"])) && (typeof value["totalSizeInfo"] === 'string') && (typeof value["totalSizeKb"] === 'number' && Number.isSafeInteger(value["totalSizeKb"])) && (typeof value["totalVersions"] === 'number' && Number.isSafeInteger(value["totalVersions"]));
}

export function readHostDocumentStatisticsTypeItem(value: unknown): HostDocumentStatisticsTypeItem {
  const normalizedValue = normalizeHostDocumentStatisticsTypeItemIntegerJson(value);
  if (!(isHostDocumentStatisticsTypeItem(normalizedValue))) {
    throw new Error('client.invalid_host_document_statistics_type_item');
  }
  return normalizedValue;
}

function isHostDocumentStatisticsTypeItem(value: unknown): value is HostDocumentStatisticsTypeItem {
  return isRecord(value) && (typeof value["count"] === 'number' && Number.isSafeInteger(value["count"])) && ((value["extension"] === null) || (typeof value["extension"] === 'string')) && (typeof value["totalSizeKb"] === 'number' && Number.isSafeInteger(value["totalSizeKb"]));
}

export function readHostDocumentStatus(value: unknown): HostDocumentStatus {
  if (!(isHostDocumentStatus(value))) {
    throw new Error('client.invalid_host_document_status');
  }
  return value;
}

function isHostDocumentStatus(value: unknown): value is HostDocumentStatus {
  return typeof value === 'number' && Number.isSafeInteger(value);
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
  const normalizedValue = normalizeHostDocumentTagResponseIntegerJson(value);
  if (!(isHostDocumentTagResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_tag_response');
  }
  return normalizedValue;
}

function isHostDocumentTagResponse(value: unknown): value is HostDocumentTagResponse {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isHot"] === 'boolean') && (typeof value["isRecommended"] === 'boolean') && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["useCount"] === 'number' && Number.isSafeInteger(value["useCount"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostDocumentType(value: unknown): HostDocumentType {
  if (!(isHostDocumentType(value))) {
    throw new Error('client.invalid_host_document_type');
  }
  return value;
}

function isHostDocumentType(value: unknown): value is HostDocumentType {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

export function readHostDocumentVersionResponse(value: unknown): HostDocumentVersionResponse {
  const normalizedValue = normalizeHostDocumentVersionResponseIntegerJson(value);
  if (!(isHostDocumentVersionResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_version_response');
  }
  return normalizedValue;
}

function isHostDocumentVersionResponse(value: unknown): value is HostDocumentVersionResponse {
  return isRecord(value) && ((value["changeDescription"] === null) || (typeof value["changeDescription"] === 'string')) && ((value["contentHash"] === null) || (typeof value["contentHash"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["fileId"] === 'string' && guidPattern.test(value["fileId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["sizeBytes"] === 'number' && Number.isSafeInteger(value["sizeBytes"])) && (typeof value["uploadedByUserId"] === 'string' && guidPattern.test(value["uploadedByUserId"])) && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readHostDocumentVersionRetentionSettingsResponse(value: unknown): HostDocumentVersionRetentionSettingsResponse {
  const normalizedValue = normalizeHostDocumentVersionRetentionSettingsResponseIntegerJson(value);
  if (!(isHostDocumentVersionRetentionSettingsResponse(normalizedValue))) {
    throw new Error('client.invalid_host_document_version_retention_settings_response');
  }
  return normalizedValue;
}

function isHostDocumentVersionRetentionSettingsResponse(value: unknown): value is HostDocumentVersionRetentionSettingsResponse {
  return isRecord(value) && (typeof value["batchSize"] === 'number' && Number.isSafeInteger(value["batchSize"])) && (typeof value["maximumRetainedHistoryVersions"] === 'number' && Number.isSafeInteger(value["maximumRetainedHistoryVersions"])) && (typeof value["minimumRetainedVersionsPerItem"] === 'number' && Number.isSafeInteger(value["minimumRetainedVersionsPerItem"])) && (typeof value["pollSeconds"] === 'number' && Number.isSafeInteger(value["pollSeconds"]));
}

export function readHostFileReferenceClaimResponse(value: unknown): HostFileReferenceClaimResponse {
  if (!(isHostFileReferenceClaimResponse(value))) {
    throw new Error('client.invalid_host_file_reference_claim_response');
  }
  return value;
}

function isHostFileReferenceClaimResponse(value: unknown): value is HostFileReferenceClaimResponse {
  return isRecord(value) && ((value["confirmedAtUtc"] === null) || (typeof value["confirmedAtUtc"] === 'string')) && (typeof value["consumerModule"] === 'string') && (typeof value["consumerReferenceId"] === 'string' && guidPattern.test(value["consumerReferenceId"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["idempotencyKey"] === 'string') && ((value["releasedAtUtc"] === null) || (typeof value["releasedAtUtc"] === 'string')) && (typeof value["state"] === 'string') && (typeof value["updatedAtUtc"] === 'string');
}

export function readHostFileResponse(value: unknown): HostFileResponse {
  const normalizedValue = normalizeHostFileResponseIntegerJson(value);
  if (!(isHostFileResponse(normalizedValue))) {
    throw new Error('client.invalid_host_file_response');
  }
  return normalizedValue;
}

function isHostFileResponse(value: unknown): value is HostFileResponse {
  return isRecord(value) && ((value["contentHash"] === null) || (typeof value["contentHash"] === 'string')) && (typeof value["contentType"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["folderId"] === null) || (typeof value["folderId"] === 'string' && guidPattern.test(value["folderId"]))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["originalFileName"] === 'string') && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["sizeBytes"] === 'number' && Number.isSafeInteger(value["sizeBytes"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"])));
}

export function readHostFolderResponse(value: unknown): HostFolderResponse {
  const normalizedValue = normalizeHostFolderResponseIntegerJson(value);
  if (!(isHostFolderResponse(normalizedValue))) {
    throw new Error('client.invalid_host_folder_response');
  }
  return normalizedValue;
}

function isHostFolderResponse(value: unknown): value is HostFolderResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"])));
}

export function readHostFolderTreeNode(value: unknown): HostFolderTreeNode {
  const normalizedValue = normalizeHostFolderTreeNodeIntegerJson(value);
  if (!(isHostFolderTreeNode(normalizedValue))) {
    throw new Error('client.invalid_host_folder_tree_node');
  }
  return normalizedValue;
}

function isHostFolderTreeNode(value: unknown): value is HostFolderTreeNode {
  return isRecord(value) && (Array.isArray(value["children"]) && value["children"].every(item17 => isHostFolderTreeNode(item17))) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"]));
}

export function readHostJobDefinitionResponse(value: unknown): HostJobDefinitionResponse {
  const normalizedValue = normalizeHostJobDefinitionResponseIntegerJson(value);
  if (!(isHostJobDefinitionResponse(normalizedValue))) {
    throw new Error('client.invalid_host_job_definition_response');
  }
  return normalizedValue;
}

function isHostJobDefinitionResponse(value: unknown): value is HostJobDefinitionResponse {
  return isRecord(value) && (typeof value["allowConcurrentExecutions"] === 'boolean') && ((value["args"] === null) || (isHttpJobArgs(value["args"]))) && (typeof value["createdAtUtc"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["handlerKind"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["jobKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostJobExecutionResponse(value: unknown): HostJobExecutionResponse {
  const normalizedValue = normalizeHostJobExecutionResponseIntegerJson(value);
  if (!(isHostJobExecutionResponse(normalizedValue))) {
    throw new Error('client.invalid_host_job_execution_response');
  }
  return normalizedValue;
}

function isHostJobExecutionResponse(value: unknown): value is HostJobExecutionResponse {
  return isRecord(value) && (typeof value["attemptCount"] === 'number' && Number.isSafeInteger(value["attemptCount"])) && (typeof value["createdAtUtc"] === 'string') && ((value["errorMessage"] === null) || (typeof value["errorMessage"] === 'string')) && ((value["finishedAtUtc"] === null) || (typeof value["finishedAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["jobDefinitionId"] === 'string' && guidPattern.test(value["jobDefinitionId"])) && ((value["jobScheduleId"] === null) || (typeof value["jobScheduleId"] === 'string' && guidPattern.test(value["jobScheduleId"]))) && ((value["nextAttemptAtUtc"] === null) || (typeof value["nextAttemptAtUtc"] === 'string')) && ((value["scheduledForUtc"] === null) || (typeof value["scheduledForUtc"] === 'string')) && ((value["startedAtUtc"] === null) || (typeof value["startedAtUtc"] === 'string')) && (typeof value["status"] === 'string') && (typeof value["triggerKind"] === 'string');
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
  const normalizedValue = normalizeHostJobHealthBacklogSnapshotIntegerJson(value);
  if (!(isHostJobHealthBacklogSnapshot(normalizedValue))) {
    throw new Error('client.invalid_host_job_health_backlog_snapshot');
  }
  return normalizedValue;
}

function isHostJobHealthBacklogSnapshot(value: unknown): value is HostJobHealthBacklogSnapshot {
  return isRecord(value) && (typeof value["dueRetryCount"] === 'number' && Number.isSafeInteger(value["dueRetryCount"])) && ((value["oldestClaimableCreatedAtUtc"] === null) || (typeof value["oldestClaimableCreatedAtUtc"] === 'string')) && ((value["oldestDueRetryAtUtc"] === null) || (typeof value["oldestDueRetryAtUtc"] === 'string')) && (typeof value["pendingCount"] === 'number' && Number.isSafeInteger(value["pendingCount"]));
}

export function readHostJobHealthResponse(value: unknown): HostJobHealthResponse {
  const normalizedValue = normalizeHostJobHealthResponseIntegerJson(value);
  if (!(isHostJobHealthResponse(normalizedValue))) {
    throw new Error('client.invalid_host_job_health_response');
  }
  return normalizedValue;
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
  const normalizedValue = normalizeHostJobScheduleResponseIntegerJson(value);
  if (!(isHostJobScheduleResponse(normalizedValue))) {
    throw new Error('client.invalid_host_job_schedule_response');
  }
  return normalizedValue;
}

function isHostJobScheduleResponse(value: unknown): value is HostJobScheduleResponse {
  return isRecord(value) && ((value["args"] === null) || (typeof value["args"] === 'string')) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && ((value["cronExpression"] === null) || (typeof value["cronExpression"] === 'string')) && ((value["endTime"] === null) || (typeof value["endTime"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["jobDefinitionDisplayName"] === 'string') && (typeof value["jobDefinitionId"] === 'string' && guidPattern.test(value["jobDefinitionId"])) && (typeof value["jobDefinitionJobKey"] === 'string') && ((value["lastExecutionAtUtc"] === null) || (typeof value["lastExecutionAtUtc"] === 'string')) && (typeof value["misfirePolicy"] === 'string') && ((value["nextExecutionAtUtc"] === null) || (typeof value["nextExecutionAtUtc"] === 'string')) && (typeof value["numberOfErrors"] === 'number' && Number.isSafeInteger(value["numberOfErrors"])) && (typeof value["numberOfRuns"] === 'number' && Number.isSafeInteger(value["numberOfRuns"])) && ((value["oneTimeAtUtc"] === null) || (typeof value["oneTimeAtUtc"] === 'string')) && ((value["startTime"] === null) || (typeof value["startTime"] === 'string')) && (typeof value["timeZoneId"] === 'string') && (typeof value["triggerKind"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
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
  const normalizedValue = normalizeHostMenuResponseIntegerJson(value);
  if (!(isHostMenuResponse(normalizedValue))) {
    throw new Error('client.invalid_host_menu_response');
  }
  return normalizedValue;
}

function isHostMenuResponse(value: unknown): value is HostMenuResponse {
  return isRecord(value) && (typeof value["caption"] === 'string') && (typeof value["componentKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["icon"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isAffix"] === 'boolean') && (typeof value["isEmbedded"] === 'boolean') && (typeof value["isHidden"] === 'boolean') && (typeof value["isKeepAlive"] === 'boolean') && (typeof value["isSystem"] === 'boolean') && ((value["linkUrl"] === null) || (typeof value["linkUrl"] === 'string')) && (typeof value["menuType"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["path"] === 'string') && ((value["redirect"] === null) || (typeof value["redirect"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && (typeof value["requiredPermission"] === 'string') && (typeof value["routeName"] === 'string') && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostNavigationCatalogSyncResponse(value: unknown): HostNavigationCatalogSyncResponse {
  const normalizedValue = normalizeHostNavigationCatalogSyncResponseIntegerJson(value);
  if (!(isHostNavigationCatalogSyncResponse(normalizedValue))) {
    throw new Error('client.invalid_host_navigation_catalog_sync_response');
  }
  return normalizedValue;
}

function isHostNavigationCatalogSyncResponse(value: unknown): value is HostNavigationCatalogSyncResponse {
  return isRecord(value) && (typeof value["created"] === 'number' && Number.isSafeInteger(value["created"])) && (typeof value["reparented"] === 'number' && Number.isSafeInteger(value["reparented"])) && (typeof value["skipped"] === 'number' && Number.isSafeInteger(value["skipped"]));
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

export function readHostReleaseNoteResponse(value: unknown): HostReleaseNoteResponse {
  const normalizedValue = normalizeHostReleaseNoteResponseIntegerJson(value);
  if (!(isHostReleaseNoteResponse(normalizedValue))) {
    throw new Error('client.invalid_host_release_note_response');
  }
  return normalizedValue;
}

function isHostReleaseNoteResponse(value: unknown): value is HostReleaseNoteResponse {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["publishedAtUtc"] === null) || (typeof value["publishedAtUtc"] === 'string')) && ((value["publishedByUserId"] === null) || (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"]))) && ((value["retractedAtUtc"] === null) || (typeof value["retractedAtUtc"] === 'string')) && ((value["retractedByUserId"] === null) || (typeof value["retractedByUserId"] === 'string' && guidPattern.test(value["retractedByUserId"]))) && (typeof value["status"] === 'string') && (typeof value["title"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && (typeof value["versionLabel"] === 'string') && (typeof value["versionSortKey"] === 'number' && Number.isSafeInteger(value["versionSortKey"]));
}

export function readHostRoleDataScopeResponse(value: unknown): HostRoleDataScopeResponse {
  const normalizedValue = normalizeHostRoleDataScopeResponseIntegerJson(value);
  if (!(isHostRoleDataScopeResponse(normalizedValue))) {
    throw new Error('client.invalid_host_role_data_scope_response');
  }
  return normalizedValue;
}

function isHostRoleDataScopeResponse(value: unknown): value is HostRoleDataScopeResponse {
  return isRecord(value) && (typeof value["dataScopeKind"] === 'string') && (typeof value["roleId"] === 'string' && guidPattern.test(value["roleId"])) && (Array.isArray(value["unitIds"]) && value["unitIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostRoleFieldGrantsResponse(value: unknown): HostRoleFieldGrantsResponse {
  const normalizedValue = normalizeHostRoleFieldGrantsResponseIntegerJson(value);
  if (!(isHostRoleFieldGrantsResponse(normalizedValue))) {
    throw new Error('client.invalid_host_role_field_grants_response');
  }
  return normalizedValue;
}

function isHostRoleFieldGrantsResponse(value: unknown): value is HostRoleFieldGrantsResponse {
  return isRecord(value) && (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string')) && (typeof value["resourceKey"] === 'string') && (typeof value["roleId"] === 'string' && guidPattern.test(value["roleId"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostRoleMemberResponse(value: unknown): HostRoleMemberResponse {
  if (!(isHostRoleMemberResponse(value))) {
    throw new Error('client.invalid_host_role_member_response');
  }
  return value;
}

function isHostRoleMemberResponse(value: unknown): value is HostRoleMemberResponse {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["isActive"] === 'boolean') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
}

export function readHostRoleMembersAssignmentResponse(value: unknown): HostRoleMembersAssignmentResponse {
  const normalizedValue = normalizeHostRoleMembersAssignmentResponseIntegerJson(value);
  if (!(isHostRoleMembersAssignmentResponse(normalizedValue))) {
    throw new Error('client.invalid_host_role_members_assignment_response');
  }
  return normalizedValue;
}

function isHostRoleMembersAssignmentResponse(value: unknown): value is HostRoleMembersAssignmentResponse {
  return isRecord(value) && (typeof value["roleId"] === 'string' && guidPattern.test(value["roleId"])) && (Array.isArray(value["userIds"]) && value["userIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostRoleMembersPageResponse(value: unknown): HostRoleMembersPageResponse {
  const normalizedValue = normalizeHostRoleMembersPageResponseIntegerJson(value);
  if (!(isHostRoleMembersPageResponse(normalizedValue))) {
    throw new Error('client.invalid_host_role_members_page_response');
  }
  return normalizedValue;
}

function isHostRoleMembersPageResponse(value: unknown): value is HostRoleMembersPageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostRoleMemberResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["roleId"] === 'string' && guidPattern.test(value["roleId"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostRoleResponse(value: unknown): HostRoleResponse {
  const normalizedValue = normalizeHostRoleResponseIntegerJson(value);
  if (!(isHostRoleResponse(normalizedValue))) {
    throw new Error('client.invalid_host_role_response');
  }
  return normalizedValue;
}

function isHostRoleResponse(value: unknown): value is HostRoleResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isSuperAdministrator"] === 'boolean') && (typeof value["isSystem"] === 'boolean') && (typeof value["name"] === 'string') && (Array.isArray(value["permissionCodes"]) && value["permissionCodes"].every(item24 => typeof item24 === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostTenantAdministratorsPageResponse(value: unknown): HostTenantAdministratorsPageResponse {
  const normalizedValue = normalizeHostTenantAdministratorsPageResponseIntegerJson(value);
  if (!(isHostTenantAdministratorsPageResponse(normalizedValue))) {
    throw new Error('client.invalid_host_tenant_administrators_page_response');
  }
  return normalizedValue;
}

function isHostTenantAdministratorsPageResponse(value: unknown): value is HostTenantAdministratorsPageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostTenantMemberResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readHostTenantMemberResponse(value: unknown): HostTenantMemberResponse {
  if (!(isHostTenantMemberResponse(value))) {
    throw new Error('client.invalid_host_tenant_member_response');
  }
  return value;
}

function isHostTenantMemberResponse(value: unknown): value is HostTenantMemberResponse {
  return isRecord(value) && (typeof value["accountType"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["isActive"] === 'boolean') && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
}

export function readHostTenantMembersPageResponse(value: unknown): HostTenantMembersPageResponse {
  const normalizedValue = normalizeHostTenantMembersPageResponseIntegerJson(value);
  if (!(isHostTenantMembersPageResponse(normalizedValue))) {
    throw new Error('client.invalid_host_tenant_members_page_response');
  }
  return normalizedValue;
}

function isHostTenantMembersPageResponse(value: unknown): value is HostTenantMembersPageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostTenantMemberResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readHostUserManagementOrganizationReferenceResponse(value: unknown): HostUserManagementOrganizationReferenceResponse {
  const normalizedValue = normalizeHostUserManagementOrganizationReferenceResponseIntegerJson(value);
  if (!(isHostUserManagementOrganizationReferenceResponse(normalizedValue))) {
    throw new Error('client.invalid_host_user_management_organization_reference_response');
  }
  return normalizedValue;
}

function isHostUserManagementOrganizationReferenceResponse(value: unknown): value is HostUserManagementOrganizationReferenceResponse {
  return isRecord(value) && (Array.isArray(value["positions"]) && value["positions"].every(item18 => isOrganizationPositionResponse(item18))) && (Array.isArray(value["units"]) && value["units"].every(item14 => isOrganizationUnitResponse(item14))) && (Array.isArray(value["userPositions"]) && value["userPositions"].every(item22 => isOrganizationUserPositionResponse(item22))) && (Array.isArray(value["userUnits"]) && value["userUnits"].every(item18 => isOrganizationUserUnitResponse(item18)));
}

export function readHostUserProfileResponse(value: unknown): HostUserProfileResponse {
  const normalizedValue = normalizeHostUserProfileResponseIntegerJson(value);
  if (!(isHostUserProfileResponse(normalizedValue))) {
    throw new Error('client.invalid_host_user_profile_response');
  }
  return normalizedValue;
}

function isHostUserProfileResponse(value: unknown): value is HostUserProfileResponse {
  return isRecord(value) && ((value["address"] === null) || (typeof value["address"] === 'string')) && ((value["birthDate"] === null) || (typeof value["birthDate"] === 'string')) && ((value["educationLevel"] === null) || (typeof value["educationLevel"] === 'string')) && ((value["email"] === null) || (typeof value["email"] === 'string')) && ((value["emergencyContact"] === null) || (typeof value["emergencyContact"] === 'string')) && ((value["emergencyContactAddress"] === null) || (typeof value["emergencyContactAddress"] === 'string')) && ((value["emergencyContactPhone"] === null) || (typeof value["emergencyContactPhone"] === 'string')) && ((value["emergencyContactRelation"] === null) || (typeof value["emergencyContactRelation"] === 'string')) && ((value["employeeNumber"] === null) || (typeof value["employeeNumber"] === 'string')) && ((value["ethnicity"] === null) || (typeof value["ethnicity"] === 'string')) && ((value["gender"] === null) || (typeof value["gender"] === 'string')) && ((value["graduatedSchool"] === null) || (typeof value["graduatedSchool"] === 'string')) && ((value["idCardNumber"] === null) || (typeof value["idCardNumber"] === 'string')) && ((value["idCardType"] === null) || (typeof value["idCardType"] === 'string')) && ((value["joinDateUtc"] === null) || (typeof value["joinDateUtc"] === 'string')) && ((value["nickname"] === null) || (typeof value["nickname"] === 'string')) && ((value["officePhone"] === null) || (typeof value["officePhone"] === 'string')) && ((value["phoneNumber"] === null) || (typeof value["phoneNumber"] === 'string')) && ((value["politicalStatus"] === null) || (typeof value["politicalStatus"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["sortOrder"] === null) || (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostUserProfileWriteRequest(value: unknown): HostUserProfileWriteRequest {
  const normalizedValue = normalizeHostUserProfileWriteRequestIntegerJson(value);
  if (!(isHostUserProfileWriteRequest(normalizedValue))) {
    throw new Error('client.invalid_host_user_profile_write_request');
  }
  return normalizedValue;
}

function isHostUserProfileWriteRequest(value: unknown): value is HostUserProfileWriteRequest {
  return isRecord(value) && ((value["address"] === null) || (typeof value["address"] === 'string')) && ((value["birthDate"] === null) || (typeof value["birthDate"] === 'string')) && ((value["educationLevel"] === null) || (typeof value["educationLevel"] === 'string')) && ((value["email"] === null) || (typeof value["email"] === 'string')) && ((value["emergencyContact"] === null) || (typeof value["emergencyContact"] === 'string')) && ((value["emergencyContactAddress"] === null) || (typeof value["emergencyContactAddress"] === 'string')) && ((value["emergencyContactPhone"] === null) || (typeof value["emergencyContactPhone"] === 'string')) && ((value["emergencyContactRelation"] === null) || (typeof value["emergencyContactRelation"] === 'string')) && ((value["employeeNumber"] === null) || (typeof value["employeeNumber"] === 'string')) && ((value["ethnicity"] === null) || (typeof value["ethnicity"] === 'string')) && ((value["fieldKeys"] === null) || (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string'))) && ((value["gender"] === null) || (typeof value["gender"] === 'string')) && ((value["graduatedSchool"] === null) || (typeof value["graduatedSchool"] === 'string')) && ((value["idCardNumber"] === null) || (typeof value["idCardNumber"] === 'string')) && ((value["idCardType"] === null) || (typeof value["idCardType"] === 'string')) && ((value["joinDateUtc"] === null) || (typeof value["joinDateUtc"] === 'string')) && ((value["nickname"] === null) || (typeof value["nickname"] === 'string')) && ((value["officePhone"] === null) || (typeof value["officePhone"] === 'string')) && ((value["phoneNumber"] === null) || (typeof value["phoneNumber"] === 'string')) && ((value["politicalStatus"] === null) || (typeof value["politicalStatus"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["sortOrder"] === null) || (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"]))) && ((value["version"] === null) || (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])));
}

export function readHostUserProjectedFieldsResponse(value: unknown): HostUserProjectedFieldsResponse {
  const normalizedValue = normalizeHostUserProjectedFieldsResponseIntegerJson(value);
  if (!(isHostUserProjectedFieldsResponse(normalizedValue))) {
    throw new Error('client.invalid_host_user_projected_fields_response');
  }
  return normalizedValue;
}

function isHostUserProjectedFieldsResponse(value: unknown): value is HostUserProjectedFieldsResponse {
  return isRecord(value) && (Array.isArray(value["effectiveFieldKeys"]) && value["effectiveFieldKeys"].every(item27 => typeof item27 === 'string')) && ((value["failedLoginCount"] === null) || (typeof value["failedLoginCount"] === 'number' && Number.isSafeInteger(value["failedLoginCount"]))) && ((value["lockoutEndUtc"] === null) || (typeof value["lockoutEndUtc"] === 'string')) && ((value["preferredLocale"] === null) || (typeof value["preferredLocale"] === 'string'));
}

export function readHostUserResponse(value: unknown): HostUserResponse {
  const normalizedValue = normalizeHostUserResponseIntegerJson(value);
  if (!(isHostUserResponse(normalizedValue))) {
    throw new Error('client.invalid_host_user_response');
  }
  return normalizedValue;
}

function isHostUserResponse(value: unknown): value is HostUserResponse {
  return isRecord(value) && (typeof value["accountType"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileResponse(value["profile"])))) && (value["projectedFields"] === undefined || ((value["projectedFields"] === null) || (isHostUserProjectedFieldsResponse(value["projectedFields"])))) && (value["retiredAtUtc"] === undefined || ((value["retiredAtUtc"] === null) || (typeof value["retiredAtUtc"] === 'string'))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHostUserRolesResponse(value: unknown): HostUserRolesResponse {
  const normalizedValue = normalizeHostUserRolesResponseIntegerJson(value);
  if (!(isHostUserRolesResponse(normalizedValue))) {
    throw new Error('client.invalid_host_user_roles_response');
  }
  return normalizedValue;
}

function isHostUserRolesResponse(value: unknown): value is HostUserRolesResponse {
  return isRecord(value) && (Array.isArray(value["roleIds"]) && value["roleIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readHttpJobArgs(value: unknown): HttpJobArgs {
  const normalizedValue = normalizeHttpJobArgsIntegerJson(value);
  if (!(isHttpJobArgs(normalizedValue))) {
    throw new Error('client.invalid_http_job_args');
  }
  return normalizedValue;
}

function isHttpJobArgs(value: unknown): value is HttpJobArgs {
  return isRecord(value) && (value["headers"] === undefined || ((value["headers"] === null) || (isRecord(value["headers"])))) && (typeof value["method"] === 'string') && (value["secretHeaders"] === undefined || ((value["secretHeaders"] === null) || (isRecord(value["secretHeaders"])))) && (value["successStatusCodes"] === undefined || ((value["successStatusCodes"] === null) || (Array.isArray(value["successStatusCodes"]) && value["successStatusCodes"].every(item27 => typeof item27 === 'number' && Number.isSafeInteger(item27))))) && (value["timeoutSeconds"] === undefined || ((value["timeoutSeconds"] === null) || (typeof value["timeoutSeconds"] === 'number' && Number.isSafeInteger(value["timeoutSeconds"])))) && (typeof value["url"] === 'string');
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

export function readIdentitySessionLoginPolicy(value: unknown): IdentitySessionLoginPolicy {
  if (!(isIdentitySessionLoginPolicy(value))) {
    throw new Error('client.invalid_identity_session_login_policy');
  }
  return value;
}

function isIdentitySessionLoginPolicy(value: unknown): value is IdentitySessionLoginPolicy {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

export function readIdentitySessionPolicyResponse(value: unknown): IdentitySessionPolicyResponse {
  if (!(isIdentitySessionPolicyResponse(value))) {
    throw new Error('client.invalid_identity_session_policy_response');
  }
  return value;
}

function isIdentitySessionPolicyResponse(value: unknown): value is IdentitySessionPolicyResponse {
  return isRecord(value) && (isIdentitySessionLoginPolicy(value["loginPolicy"]));
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

export function readIFormFileCollection(value: unknown): IFormFileCollection {
  if (!(isIFormFileCollection(value))) {
    throw new Error('client.invalid_iform_file_collection');
  }
  return value;
}

function isIFormFileCollection(value: unknown): value is IFormFileCollection {
  return Array.isArray(value) && value.every(item5 => isIFormFile(item5));
}

export function readImportAdministrativeRegionAddedSummary(value: unknown): ImportAdministrativeRegionAddedSummary {
  const normalizedValue = normalizeImportAdministrativeRegionAddedSummaryIntegerJson(value);
  if (!(isImportAdministrativeRegionAddedSummary(normalizedValue))) {
    throw new Error('client.invalid_import_administrative_region_added_summary');
  }
  return normalizedValue;
}

function isImportAdministrativeRegionAddedSummary(value: unknown): value is ImportAdministrativeRegionAddedSummary {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && (typeof value["name"] === 'string');
}

export function readImportAdministrativeRegionItem(value: unknown): ImportAdministrativeRegionItem {
  const normalizedValue = normalizeImportAdministrativeRegionItemIntegerJson(value);
  if (!(isImportAdministrativeRegionItem(normalizedValue))) {
    throw new Error('client.invalid_import_administrative_region_item');
  }
  return normalizedValue;
}

function isImportAdministrativeRegionItem(value: unknown): value is ImportAdministrativeRegionItem {
  return isRecord(value) && ((value["cityCode"] === null) || (typeof value["cityCode"] === 'string')) && (typeof value["code"] === 'string') && ((value["displayOrder"] === null) || (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"]))) && ((value["latitude"] === null) || (typeof value["latitude"] === 'number' && Number.isFinite(value["latitude"])) || (typeof value["latitude"] === 'string')) && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && ((value["longitude"] === null) || (typeof value["longitude"] === 'number' && Number.isFinite(value["longitude"])) || (typeof value["longitude"] === 'string')) && ((value["mergerName"] === null) || (typeof value["mergerName"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentCode"] === null) || (typeof value["parentCode"] === 'string')) && ((value["pinYin"] === null) || (typeof value["pinYin"] === 'string')) && ((value["regionType"] === null) || (typeof value["regionType"] === 'string')) && ((value["shortName"] === null) || (typeof value["shortName"] === 'string')) && ((value["zipCode"] === null) || (typeof value["zipCode"] === 'string'));
}

export function readImportAdministrativeRegionRemovedSummary(value: unknown): ImportAdministrativeRegionRemovedSummary {
  if (!(isImportAdministrativeRegionRemovedSummary(value))) {
    throw new Error('client.invalid_import_administrative_region_removed_summary');
  }
  return value;
}

function isImportAdministrativeRegionRemovedSummary(value: unknown): value is ImportAdministrativeRegionRemovedSummary {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["name"] === 'string');
}

export function readImportAdministrativeRegionsApplyResponse(value: unknown): ImportAdministrativeRegionsApplyResponse {
  const normalizedValue = normalizeImportAdministrativeRegionsApplyResponseIntegerJson(value);
  if (!(isImportAdministrativeRegionsApplyResponse(normalizedValue))) {
    throw new Error('client.invalid_import_administrative_regions_apply_response');
  }
  return normalizedValue;
}

function isImportAdministrativeRegionsApplyResponse(value: unknown): value is ImportAdministrativeRegionsApplyResponse {
  return isRecord(value) && (typeof value["addedCount"] === 'number' && Number.isSafeInteger(value["addedCount"])) && (isAdministrativeRegionDatasetManifestResponse(value["manifest"])) && (typeof value["removedCount"] === 'number' && Number.isSafeInteger(value["removedCount"])) && (typeof value["skippedCount"] === 'number' && Number.isSafeInteger(value["skippedCount"])) && (typeof value["updatedCount"] === 'number' && Number.isSafeInteger(value["updatedCount"]));
}

export function readImportAdministrativeRegionsPreviewResponse(value: unknown): ImportAdministrativeRegionsPreviewResponse {
  const normalizedValue = normalizeImportAdministrativeRegionsPreviewResponseIntegerJson(value);
  if (!(isImportAdministrativeRegionsPreviewResponse(normalizedValue))) {
    throw new Error('client.invalid_import_administrative_regions_preview_response');
  }
  return normalizedValue;
}

function isImportAdministrativeRegionsPreviewResponse(value: unknown): value is ImportAdministrativeRegionsPreviewResponse {
  return isRecord(value) && (Array.isArray(value["added"]) && value["added"].every(item14 => isImportAdministrativeRegionAddedSummary(item14))) && (Array.isArray(value["removed"]) && value["removed"].every(item16 => isImportAdministrativeRegionRemovedSummary(item16))) && (typeof value["skippedCount"] === 'number' && Number.isSafeInteger(value["skippedCount"])) && (Array.isArray(value["updated"]) && value["updated"].every(item16 => isImportAdministrativeRegionUpdatedSummary(item16)));
}

export function readImportAdministrativeRegionsRequest(value: unknown): ImportAdministrativeRegionsRequest {
  const normalizedValue = normalizeImportAdministrativeRegionsRequestIntegerJson(value);
  if (!(isImportAdministrativeRegionsRequest(normalizedValue))) {
    throw new Error('client.invalid_import_administrative_regions_request');
  }
  return normalizedValue;
}

function isImportAdministrativeRegionsRequest(value: unknown): value is ImportAdministrativeRegionsRequest {
  return isRecord(value) && (typeof value["datasetKey"] === 'string') && (typeof value["datasetVersion"] === 'string') && (Array.isArray(value["items"]) && value["items"].every(item14 => isImportAdministrativeRegionItem(item14))) && (typeof value["mergeMode"] === 'string') && (typeof value["sourceDigest"] === 'string');
}

export function readImportAdministrativeRegionUpdatedSummary(value: unknown): ImportAdministrativeRegionUpdatedSummary {
  if (!(isImportAdministrativeRegionUpdatedSummary(value))) {
    throw new Error('client.invalid_import_administrative_region_updated_summary');
  }
  return value;
}

function isImportAdministrativeRegionUpdatedSummary(value: unknown): value is ImportAdministrativeRegionUpdatedSummary {
  return isRecord(value) && (Array.isArray(value["changedFields"]) && value["changedFields"].every(item22 => typeof item22 === 'string')) && (typeof value["code"] === 'string') && (typeof value["name"] === 'string');
}

export function readImportExportTaskDetailResponse(value: unknown): ImportExportTaskDetailResponse {
  const normalizedValue = normalizeImportExportTaskDetailResponseIntegerJson(value);
  if (!(isImportExportTaskDetailResponse(normalizedValue))) {
    throw new Error('client.invalid_import_export_task_detail_response');
  }
  return normalizedValue;
}

function isImportExportTaskDetailResponse(value: unknown): value is ImportExportTaskDetailResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["executionCompletedAtUtc"] === null) || (typeof value["executionCompletedAtUtc"] === 'string')) && (typeof value["executionFailedRowCount"] === 'number' && Number.isSafeInteger(value["executionFailedRowCount"])) && ((value["executionStartedAtUtc"] === null) || (typeof value["executionStartedAtUtc"] === 'string')) && (typeof value["hasErrorReceipt"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["invalidRowCount"] === 'number' && Number.isSafeInteger(value["invalidRowCount"])) && (typeof value["nextLineNumber"] === 'number' && Number.isSafeInteger(value["nextLineNumber"])) && ((value["previewCompletedAtUtc"] === null) || (typeof value["previewCompletedAtUtc"] === 'string')) && (Array.isArray(value["previewRows"]) && value["previewRows"].every(item20 => isStaticImportRowPreviewResult(item20))) && (typeof value["processedRowCount"] === 'number' && Number.isSafeInteger(value["processedRowCount"])) && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && (typeof value["schemaDisplayName"] === 'string') && (typeof value["schemaKey"] === 'string') && (typeof value["sourceFileId"] === 'string' && guidPattern.test(value["sourceFileId"])) && ((value["sourceFileName"] === null) || (typeof value["sourceFileName"] === 'string')) && (typeof value["statusKey"] === 'string') && (typeof value["succeededRowCount"] === 'number' && Number.isSafeInteger(value["succeededRowCount"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["totalRows"] === 'number' && Number.isSafeInteger(value["totalRows"])) && (typeof value["validRowCount"] === 'number' && Number.isSafeInteger(value["validRowCount"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && (typeof value["worksheetKey"] === 'string');
}

export function readImportExportTaskResponse(value: unknown): ImportExportTaskResponse {
  const normalizedValue = normalizeImportExportTaskResponseIntegerJson(value);
  if (!(isImportExportTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_import_export_task_response');
  }
  return normalizedValue;
}

function isImportExportTaskResponse(value: unknown): value is ImportExportTaskResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["executionCompletedAtUtc"] === null) || (typeof value["executionCompletedAtUtc"] === 'string')) && (typeof value["executionFailedRowCount"] === 'number' && Number.isSafeInteger(value["executionFailedRowCount"])) && ((value["executionStartedAtUtc"] === null) || (typeof value["executionStartedAtUtc"] === 'string')) && (typeof value["hasErrorReceipt"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["invalidRowCount"] === 'number' && Number.isSafeInteger(value["invalidRowCount"])) && (typeof value["nextLineNumber"] === 'number' && Number.isSafeInteger(value["nextLineNumber"])) && ((value["previewCompletedAtUtc"] === null) || (typeof value["previewCompletedAtUtc"] === 'string')) && (typeof value["processedRowCount"] === 'number' && Number.isSafeInteger(value["processedRowCount"])) && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && (typeof value["schemaDisplayName"] === 'string') && (typeof value["schemaKey"] === 'string') && (typeof value["sourceFileId"] === 'string' && guidPattern.test(value["sourceFileId"])) && ((value["sourceFileName"] === null) || (typeof value["sourceFileName"] === 'string')) && (typeof value["statusKey"] === 'string') && (typeof value["succeededRowCount"] === 'number' && Number.isSafeInteger(value["succeededRowCount"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["totalRows"] === 'number' && Number.isSafeInteger(value["totalRows"])) && (typeof value["validRowCount"] === 'number' && Number.isSafeInteger(value["validRowCount"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && (typeof value["worksheetKey"] === 'string');
}

export function readImportHostUserRowResult(value: unknown): ImportHostUserRowResult {
  const normalizedValue = normalizeImportHostUserRowResultIntegerJson(value);
  if (!(isImportHostUserRowResult(normalizedValue))) {
    throw new Error('client.invalid_import_host_user_row_result');
  }
  return normalizedValue;
}

function isImportHostUserRowResult(value: unknown): value is ImportHostUserRowResult {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["line"] === 'number' && Number.isSafeInteger(value["line"])) && ((value["message"] === null) || (typeof value["message"] === 'string')) && (typeof value["succeeded"] === 'boolean') && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readImportHostUsersRequest(value: unknown): ImportHostUsersRequest {
  const normalizedValue = normalizeImportHostUsersRequestIntegerJson(value);
  if (!(isImportHostUsersRequest(normalizedValue))) {
    throw new Error('client.invalid_import_host_users_request');
  }
  return normalizedValue;
}

function isImportHostUsersRequest(value: unknown): value is ImportHostUsersRequest {
  return isRecord(value) && (Array.isArray(value["rows"]) && value["rows"].every(item13 => isCreateHostUserRequest(item13)));
}

export function readImportHostUsersResponse(value: unknown): ImportHostUsersResponse {
  const normalizedValue = normalizeImportHostUsersResponseIntegerJson(value);
  if (!(isImportHostUsersResponse(normalizedValue))) {
    throw new Error('client.invalid_import_host_users_response');
  }
  return normalizedValue;
}

function isImportHostUsersResponse(value: unknown): value is ImportHostUsersResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isImportHostUserRowResult(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
}

export function readImportOrganizationPositionRow(value: unknown): ImportOrganizationPositionRow {
  const normalizedValue = normalizeImportOrganizationPositionRowIntegerJson(value);
  if (!(isImportOrganizationPositionRow(normalizedValue))) {
    throw new Error('client.invalid_import_organization_position_row');
  }
  return normalizedValue;
}

function isImportOrganizationPositionRow(value: unknown): value is ImportOrganizationPositionRow {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && ((value["positionLevelCode"] === null) || (typeof value["positionLevelCode"] === 'string')) && ((value["unitCode"] === null) || (typeof value["unitCode"] === 'string'));
}

export function readImportOrganizationPositionRowResult(value: unknown): ImportOrganizationPositionRowResult {
  const normalizedValue = normalizeImportOrganizationPositionRowResultIntegerJson(value);
  if (!(isImportOrganizationPositionRowResult(normalizedValue))) {
    throw new Error('client.invalid_import_organization_position_row_result');
  }
  return normalizedValue;
}

function isImportOrganizationPositionRowResult(value: unknown): value is ImportOrganizationPositionRowResult {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["line"] === 'number' && Number.isSafeInteger(value["line"])) && ((value["message"] === null) || (typeof value["message"] === 'string')) && ((value["positionId"] === null) || (typeof value["positionId"] === 'string' && guidPattern.test(value["positionId"]))) && (typeof value["succeeded"] === 'boolean');
}

export function readImportOrganizationPositionsRequest(value: unknown): ImportOrganizationPositionsRequest {
  const normalizedValue = normalizeImportOrganizationPositionsRequestIntegerJson(value);
  if (!(isImportOrganizationPositionsRequest(normalizedValue))) {
    throw new Error('client.invalid_import_organization_positions_request');
  }
  return normalizedValue;
}

function isImportOrganizationPositionsRequest(value: unknown): value is ImportOrganizationPositionsRequest {
  return isRecord(value) && (Array.isArray(value["rows"]) && value["rows"].every(item13 => isImportOrganizationPositionRow(item13)));
}

export function readImportOrganizationPositionsResponse(value: unknown): ImportOrganizationPositionsResponse {
  const normalizedValue = normalizeImportOrganizationPositionsResponseIntegerJson(value);
  if (!(isImportOrganizationPositionsResponse(normalizedValue))) {
    throw new Error('client.invalid_import_organization_positions_response');
  }
  return normalizedValue;
}

function isImportOrganizationPositionsResponse(value: unknown): value is ImportOrganizationPositionsResponse {
  return isRecord(value) && (Array.isArray(value["results"]) && value["results"].every(item16 => isImportOrganizationPositionRowResult(item16))) && (typeof value["succeededCount"] === 'number' && Number.isSafeInteger(value["succeededCount"]));
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
  const normalizedValue = normalizeInboxUnreadCountResponseIntegerJson(value);
  if (!(isInboxUnreadCountResponse(normalizedValue))) {
    throw new Error('client.invalid_inbox_unread_count_response');
  }
  return normalizedValue;
}

function isInboxUnreadCountResponse(value: unknown): value is InboxUnreadCountResponse {
  return isRecord(value) && (typeof value["unreadCount"] === 'number' && Number.isSafeInteger(value["unreadCount"]));
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

export function readK3CloudConnectionConfigResponse(value: unknown): K3CloudConnectionConfigResponse {
  const normalizedValue = normalizeK3CloudConnectionConfigResponseIntegerJson(value);
  if (!(isK3CloudConnectionConfigResponse(normalizedValue))) {
    throw new Error('client.invalid_k3_cloud_connection_config_response');
  }
  return normalizedValue;
}

function isK3CloudConnectionConfigResponse(value: unknown): value is K3CloudConnectionConfigResponse {
  return isRecord(value) && (typeof value["acctId"] === 'string') && (typeof value["baseUrl"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["hasPassword"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && ((value["lastTestedAtUtc"] === null) || (typeof value["lastTestedAtUtc"] === 'string')) && ((value["lastTestMessage"] === null) || (typeof value["lastTestMessage"] === 'string')) && ((value["lastTestStatusKey"] === null) || (typeof value["lastTestStatusKey"] === 'string')) && (typeof value["lcid"] === 'number' && Number.isSafeInteger(value["lcid"])) && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readK3CloudDocumentSyncResponse(value: unknown): K3CloudDocumentSyncResponse {
  const normalizedValue = normalizeK3CloudDocumentSyncResponseIntegerJson(value);
  if (!(isK3CloudDocumentSyncResponse(normalizedValue))) {
    throw new Error('client.invalid_k3_cloud_document_sync_response');
  }
  return normalizedValue;
}

function isK3CloudDocumentSyncResponse(value: unknown): value is K3CloudDocumentSyncResponse {
  return isRecord(value) && (typeof value["businessKey"] === 'string') && (typeof value["connectionConfigId"] === 'string' && guidPattern.test(value["connectionConfigId"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && (typeof value["documentTypeKey"] === 'string') && ((value["externalBillId"] === null) || (typeof value["externalBillId"] === 'string')) && ((value["externalBillNo"] === null) || (typeof value["externalBillNo"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["lastErrorCode"] === null) || (typeof value["lastErrorCode"] === 'string')) && ((value["lastErrorMessage"] === null) || (typeof value["lastErrorMessage"] === 'string')) && ((value["lastStepKey"] === null) || (typeof value["lastStepKey"] === 'string')) && (typeof value["statusKey"] === 'string') && ((value["submittedAtUtc"] === null) || (typeof value["submittedAtUtc"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readLocalePreferenceResponse(value: unknown): LocalePreferenceResponse {
  const normalizedValue = normalizeLocalePreferenceResponseIntegerJson(value);
  if (!(isLocalePreferenceResponse(normalizedValue))) {
    throw new Error('client.invalid_locale_preference_response');
  }
  return normalizedValue;
}

function isLocalePreferenceResponse(value: unknown): value is LocalePreferenceResponse {
  return isRecord(value) && (typeof value["preferredLocale"] === 'string') && (typeof value["profileVersion"] === 'number' && Number.isSafeInteger(value["profileVersion"]));
}

export function readLogFileSummary(value: unknown): LogFileSummary {
  const normalizedValue = normalizeLogFileSummaryIntegerJson(value);
  if (!(isLogFileSummary(normalizedValue))) {
    throw new Error('client.invalid_log_file_summary');
  }
  return normalizedValue;
}

function isLogFileSummary(value: unknown): value is LogFileSummary {
  return isRecord(value) && (typeof value["fileName"] === 'string') && (typeof value["id"] === 'string') && (typeof value["lastModifiedUtc"] === 'string') && (typeof value["sizeBytes"] === 'number' && Number.isSafeInteger(value["sizeBytes"]));
}

export function readLogFileTail(value: unknown): LogFileTail {
  const normalizedValue = normalizeLogFileTailIntegerJson(value);
  if (!(isLogFileTail(normalizedValue))) {
    throw new Error('client.invalid_log_file_tail');
  }
  return normalizedValue;
}

function isLogFileTail(value: unknown): value is LogFileTail {
  return isRecord(value) && (typeof value["bytesRead"] === 'number' && Number.isSafeInteger(value["bytesRead"])) && (typeof value["content"] === 'string') && (typeof value["fileName"] === 'string') && (typeof value["id"] === 'string') && (typeof value["isTruncated"] === 'boolean');
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

export function readModuleSelectionAnalysisResponse(value: unknown): ModuleSelectionAnalysisResponse {
  if (!(isModuleSelectionAnalysisResponse(value))) {
    throw new Error('client.invalid_module_selection_analysis_response');
  }
  return value;
}

function isModuleSelectionAnalysisResponse(value: unknown): value is ModuleSelectionAnalysisResponse {
  return isRecord(value) && (typeof value["deploymentNotice"] === 'string') && (Array.isArray(value["enabledModuleKeys"]) && value["enabledModuleKeys"].every(item26 => typeof item26 === 'string')) && (Array.isArray(value["issues"]) && value["issues"].every(item15 => isModuleSelectionIssueResponse(item15))) && (typeof value["isValid"] === 'boolean') && (Array.isArray(value["modules"]) && value["modules"].every(item16 => isModuleSelectionModuleStateResponse(item16))) && (Array.isArray(value["officialModuleKeys"]) && value["officialModuleKeys"].every(item27 => typeof item27 === 'string')) && ((value["preset"] === null) || (typeof value["preset"] === 'string')) && (typeof value["sourceKind"] === 'string');
}

export function readModuleSelectionIssueResponse(value: unknown): ModuleSelectionIssueResponse {
  if (!(isModuleSelectionIssueResponse(value))) {
    throw new Error('client.invalid_module_selection_issue_response');
  }
  return value;
}

function isModuleSelectionIssueResponse(value: unknown): value is ModuleSelectionIssueResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["message"] === 'string') && ((value["moduleKey"] === null) || (typeof value["moduleKey"] === 'string')) && ((value["relatedModuleKey"] === null) || (typeof value["relatedModuleKey"] === 'string'));
}

export function readModuleSelectionModuleStateResponse(value: unknown): ModuleSelectionModuleStateResponse {
  if (!(isModuleSelectionModuleStateResponse(value))) {
    throw new Error('client.invalid_module_selection_module_state_response');
  }
  return value;
}

function isModuleSelectionModuleStateResponse(value: unknown): value is ModuleSelectionModuleStateResponse {
  return isRecord(value) && (Array.isArray(value["dependencies"]) && value["dependencies"].every(item21 => typeof item21 === 'string')) && (typeof value["isEnabled"] === 'boolean') && (Array.isArray(value["missingDependencies"]) && value["missingDependencies"].every(item28 => typeof item28 === 'string')) && (typeof value["moduleKey"] === 'string');
}

export function readModuleSelectionValidateRequest(value: unknown): ModuleSelectionValidateRequest {
  if (!(isModuleSelectionValidateRequest(value))) {
    throw new Error('client.invalid_module_selection_validate_request');
  }
  return value;
}

function isModuleSelectionValidateRequest(value: unknown): value is ModuleSelectionValidateRequest {
  return isRecord(value) && ((value["enabled"] === null) || (Array.isArray(value["enabled"]) && value["enabled"].every(item16 => typeof item16 === 'string'))) && ((value["preset"] === null) || (typeof value["preset"] === 'string'));
}

export function readMyReleaseNoteResponse(value: unknown): MyReleaseNoteResponse {
  const normalizedValue = normalizeMyReleaseNoteResponseIntegerJson(value);
  if (!(isMyReleaseNoteResponse(normalizedValue))) {
    throw new Error('client.invalid_my_release_note_response');
  }
  return normalizedValue;
}

function isMyReleaseNoteResponse(value: unknown): value is MyReleaseNoteResponse {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isRead"] === 'boolean') && (typeof value["publishedAtUtc"] === 'string') && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && (typeof value["title"] === 'string') && (typeof value["versionLabel"] === 'string') && (typeof value["versionSortKey"] === 'number' && Number.isSafeInteger(value["versionSortKey"]));
}

export function readNotificationBindingResponse(value: unknown): NotificationBindingResponse {
  const normalizedValue = normalizeNotificationBindingResponseIntegerJson(value);
  if (!(isNotificationBindingResponse(normalizedValue))) {
    throw new Error('client.invalid_notification_binding_response');
  }
  return normalizedValue;
}

function isNotificationBindingResponse(value: unknown): value is NotificationBindingResponse {
  return isRecord(value) && (typeof value["bindingKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["draftDispatchModeKey"] === 'string') && (typeof value["draftJson"] === 'string') && (typeof value["draftRevision"] === 'number' && Number.isSafeInteger(value["draftRevision"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestBindingTargetsJson"] === null) || (typeof value["latestBindingTargetsJson"] === 'string')) && ((value["latestChannelKey"] === null) || (typeof value["latestChannelKey"] === 'string')) && ((value["latestDispatchModeKey"] === null) || (typeof value["latestDispatchModeKey"] === 'string')) && ((value["latestProducerKey"] === null) || (typeof value["latestProducerKey"] === 'string')) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["latestPublishedVersionNumber"] === null) || (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isSafeInteger(value["latestPublishedVersionNumber"]))) && ((value["latestSceneKey"] === null) || (typeof value["latestSceneKey"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readNotificationBindingTargetInput(value: unknown): NotificationBindingTargetInput {
  const normalizedValue = normalizeNotificationBindingTargetInputIntegerJson(value);
  if (!(isNotificationBindingTargetInput(normalizedValue))) {
    throw new Error('client.invalid_notification_binding_target_input');
  }
  return normalizedValue;
}

function isNotificationBindingTargetInput(value: unknown): value is NotificationBindingTargetInput {
  return isRecord(value) && (typeof value["order"] === 'number' && Number.isSafeInteger(value["order"])) && (typeof value["profileKey"] === 'string');
}

export function readNotificationDeliveryAttemptResponse(value: unknown): NotificationDeliveryAttemptResponse {
  const normalizedValue = normalizeNotificationDeliveryAttemptResponseIntegerJson(value);
  if (!(isNotificationDeliveryAttemptResponse(normalizedValue))) {
    throw new Error('client.invalid_notification_delivery_attempt_response');
  }
  return normalizedValue;
}

function isNotificationDeliveryAttemptResponse(value: unknown): value is NotificationDeliveryAttemptResponse {
  return isRecord(value) && (typeof value["attemptNumber"] === 'number' && Number.isSafeInteger(value["attemptNumber"])) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["finishedAtUtc"] === null) || (typeof value["finishedAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["providerMessageId"] === null) || (typeof value["providerMessageId"] === 'string')) && ((value["resultCategoryKey"] === null) || (typeof value["resultCategoryKey"] === 'string')) && (typeof value["startedAtUtc"] === 'string') && (typeof value["statusKey"] === 'string');
}

export function readNotificationDeliveryReceiptResponse(value: unknown): NotificationDeliveryReceiptResponse {
  if (!(isNotificationDeliveryReceiptResponse(value))) {
    throw new Error('client.invalid_notification_delivery_receipt_response');
  }
  return value;
}

function isNotificationDeliveryReceiptResponse(value: unknown): value is NotificationDeliveryReceiptResponse {
  return isRecord(value) && (typeof value["externalStatusKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["mappedStatusKey"] === 'string') && ((value["processedAtUtc"] === null) || (typeof value["processedAtUtc"] === 'string')) && (typeof value["processStatusKey"] === 'string') && ((value["providerMessageId"] === null) || (typeof value["providerMessageId"] === 'string')) && (typeof value["providerTypeKey"] === 'string') && (typeof value["receivedAtUtc"] === 'string');
}

export function readNotificationDeliveryResponse(value: unknown): NotificationDeliveryResponse {
  const normalizedValue = normalizeNotificationDeliveryResponseIntegerJson(value);
  if (!(isNotificationDeliveryResponse(normalizedValue))) {
    throw new Error('client.invalid_notification_delivery_response');
  }
  return normalizedValue;
}

function isNotificationDeliveryResponse(value: unknown): value is NotificationDeliveryResponse {
  return isRecord(value) && (Array.isArray(value["attempts"]) && value["attempts"].every(item17 => isNotificationDeliveryAttemptResponse(item17))) && ((value["bindingVersionId"] === null) || (typeof value["bindingVersionId"] === 'string' && guidPattern.test(value["bindingVersionId"]))) && (typeof value["channelKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["intentId"] === 'string' && guidPattern.test(value["intentId"])) && ((value["nextAttemptAtUtc"] === null) || (typeof value["nextAttemptAtUtc"] === 'string')) && ((value["providerProfileVersionId"] === null) || (typeof value["providerProfileVersionId"] === 'string' && guidPattern.test(value["providerProfileVersionId"]))) && (Array.isArray(value["receipts"]) && value["receipts"].every(item17 => isNotificationDeliveryReceiptResponse(item17))) && (typeof value["recipientId"] === 'string' && guidPattern.test(value["recipientId"])) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string'));
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
  const normalizedValue = normalizeNotificationProviderProfileResponseIntegerJson(value);
  if (!(isNotificationProviderProfileResponse(normalizedValue))) {
    throw new Error('client.invalid_notification_provider_profile_response');
  }
  return normalizedValue;
}

function isNotificationProviderProfileResponse(value: unknown): value is NotificationProviderProfileResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["draftRevision"] === 'number' && Number.isSafeInteger(value["draftRevision"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["latestAdapterVersion"] === null) || (typeof value["latestAdapterVersion"] === 'string')) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["latestPublishedVersionNumber"] === null) || (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isSafeInteger(value["latestPublishedVersionNumber"]))) && (typeof value["nonSecretConfigJson"] === 'string') && (typeof value["profileKey"] === 'string') && (typeof value["providerTypeKey"] === 'string') && (typeof value["secretStatus"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
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
  const normalizedValue = normalizeNotificationTemplateParameterDefinitionIntegerJson(value);
  if (!(isNotificationTemplateParameterDefinition(normalizedValue))) {
    throw new Error('client.invalid_notification_template_parameter_definition');
  }
  return normalizedValue;
}

function isNotificationTemplateParameterDefinition(value: unknown): value is NotificationTemplateParameterDefinition {
  return isRecord(value) && ((value["maxLength"] === null) || (typeof value["maxLength"] === 'number' && Number.isSafeInteger(value["maxLength"]))) && (typeof value["name"] === 'string') && (typeof value["required"] === 'boolean') && (typeof value["typeKey"] === 'string');
}

export function readNotificationTemplateParameterSchema(value: unknown): NotificationTemplateParameterSchema {
  const normalizedValue = normalizeNotificationTemplateParameterSchemaIntegerJson(value);
  if (!(isNotificationTemplateParameterSchema(normalizedValue))) {
    throw new Error('client.invalid_notification_template_parameter_schema');
  }
  return normalizedValue;
}

function isNotificationTemplateParameterSchema(value: unknown): value is NotificationTemplateParameterSchema {
  return isRecord(value) && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isNotificationTemplateParameterDefinition(item19))) && (typeof value["schemaVersion"] === 'number' && Number.isSafeInteger(value["schemaVersion"]));
}

export function readNotificationTemplateResponse(value: unknown): NotificationTemplateResponse {
  const normalizedValue = normalizeNotificationTemplateResponseIntegerJson(value);
  if (!(isNotificationTemplateResponse(normalizedValue))) {
    throw new Error('client.invalid_notification_template_response');
  }
  return normalizedValue;
}

function isNotificationTemplateResponse(value: unknown): value is NotificationTemplateResponse {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["contentCategoryKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["defaultLocaleTag"] === 'string') && (typeof value["draftBodyJson"] === 'string') && (typeof value["draftParameterSchemaJson"] === 'string') && (typeof value["draftRevision"] === 'number' && Number.isSafeInteger(value["draftRevision"])) && (typeof value["draftSubject"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestContentClassificationKey"] === null) || (typeof value["latestContentClassificationKey"] === 'string')) && ((value["latestContentHash"] === null) || (typeof value["latestContentHash"] === 'string')) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && ((value["latestPublishedVersionNumber"] === null) || (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isSafeInteger(value["latestPublishedVersionNumber"]))) && (typeof value["localeTag"] === 'string') && (Array.isArray(value["missingLocaleTags"]) && value["missingLocaleTags"].every(item26 => typeof item26 === 'string')) && (Array.isArray(value["publishedLocaleTags"]) && value["publishedLocaleTags"].every(item28 => typeof item28 === 'string')) && (typeof value["templateKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOcrIdCardTaskResponse(value: unknown): OcrIdCardTaskResponse {
  const normalizedValue = normalizeOcrIdCardTaskResponseIntegerJson(value);
  if (!(isOcrIdCardTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_ocr_id_card_task_response');
  }
  return normalizedValue;
}

function isOcrIdCardTaskResponse(value: unknown): value is OcrIdCardTaskResponse {
  return isRecord(value) && ((value["confirmedAddress"] === null) || (typeof value["confirmedAddress"] === 'string')) && ((value["confirmedAtUtc"] === null) || (typeof value["confirmedAtUtc"] === 'string')) && ((value["confirmedBirthDate"] === null) || (typeof value["confirmedBirthDate"] === 'string')) && ((value["confirmedGender"] === null) || (typeof value["confirmedGender"] === 'string')) && ((value["confirmedIdNumber"] === null) || (typeof value["confirmedIdNumber"] === 'string')) && ((value["confirmedName"] === null) || (typeof value["confirmedName"] === 'string')) && ((value["confirmedNation"] === null) || (typeof value["confirmedNation"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["failureMessage"] === null) || (typeof value["failureMessage"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["recognizedAddress"] === null) || (typeof value["recognizedAddress"] === 'string')) && ((value["recognizedAtUtc"] === null) || (typeof value["recognizedAtUtc"] === 'string')) && ((value["recognizedBirthDate"] === null) || (typeof value["recognizedBirthDate"] === 'string')) && ((value["recognizedGender"] === null) || (typeof value["recognizedGender"] === 'string')) && ((value["recognizedIdNumber"] === null) || (typeof value["recognizedIdNumber"] === 'string')) && ((value["recognizedName"] === null) || (typeof value["recognizedName"] === 'string')) && ((value["recognizedNation"] === null) || (typeof value["recognizedNation"] === 'string')) && ((value["rejectedAtUtc"] === null) || (typeof value["rejectedAtUtc"] === 'string')) && (typeof value["sourceFileId"] === 'string' && guidPattern.test(value["sourceFileId"])) && (typeof value["statusKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOcrProviderConfigResponse(value: unknown): OcrProviderConfigResponse {
  const normalizedValue = normalizeOcrProviderConfigResponseIntegerJson(value);
  if (!(isOcrProviderConfigResponse(normalizedValue))) {
    throw new Error('client.invalid_ocr_provider_config_response');
  }
  return normalizedValue;
}

function isOcrProviderConfigResponse(value: unknown): value is OcrProviderConfigResponse {
  return isRecord(value) && (typeof value["baseUrl"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["hasApiKey"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["lastTestedAtUtc"] === null) || (typeof value["lastTestedAtUtc"] === 'string')) && ((value["lastTestMessage"] === null) || (typeof value["lastTestMessage"] === 'string')) && ((value["lastTestStatusKey"] === null) || (typeof value["lastTestStatusKey"] === 'string')) && (typeof value["name"] === 'string') && (typeof value["providerKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOperationLogResponse(value: unknown): OperationLogResponse {
  const normalizedValue = normalizeOperationLogResponseIntegerJson(value);
  if (!(isOperationLogResponse(normalizedValue))) {
    throw new Error('client.invalid_operation_log_response');
  }
  return normalizedValue;
}

function isOperationLogResponse(value: unknown): value is OperationLogResponse {
  return isRecord(value) && (typeof value["actionKey"] === 'string') && ((value["clientIpFingerprint"] === null) || (typeof value["clientIpFingerprint"] === 'string')) && (typeof value["durationMs"] === 'number' && Number.isSafeInteger(value["durationMs"])) && (typeof value["httpMethod"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && ((value["permissionCode"] === null) || (typeof value["permissionCode"] === 'string')) && (typeof value["requestPath"] === 'string') && (typeof value["statusCode"] === 'number' && Number.isSafeInteger(value["statusCode"])) && (typeof value["succeeded"] === 'boolean') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
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
  const normalizedValue = normalizeOrganizationPositionLevelResponseIntegerJson(value);
  if (!(isOrganizationPositionLevelResponse(normalizedValue))) {
    throw new Error('client.invalid_organization_position_level_response');
  }
  return normalizedValue;
}

function isOrganizationPositionLevelResponse(value: unknown): value is OrganizationPositionLevelResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOrganizationPositionResponse(value: unknown): OrganizationPositionResponse {
  const normalizedValue = normalizeOrganizationPositionResponseIntegerJson(value);
  if (!(isOrganizationPositionResponse(normalizedValue))) {
    throw new Error('client.invalid_organization_position_response');
  }
  return normalizedValue;
}

function isOrganizationPositionResponse(value: unknown): value is OrganizationPositionResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["positionLevelCode"] === null) || (typeof value["positionLevelCode"] === 'string')) && ((value["positionLevelId"] === null) || (typeof value["positionLevelId"] === 'string' && guidPattern.test(value["positionLevelId"]))) && ((value["positionLevelName"] === null) || (typeof value["positionLevelName"] === 'string')) && ((value["unitCode"] === null) || (typeof value["unitCode"] === 'string')) && ((value["unitId"] === null) || (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"]))) && ((value["unitName"] === null) || (typeof value["unitName"] === 'string')) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOrganizationUnitResponse(value: unknown): OrganizationUnitResponse {
  const normalizedValue = normalizeOrganizationUnitResponseIntegerJson(value);
  if (!(isOrganizationUnitResponse(normalizedValue))) {
    throw new Error('client.invalid_organization_unit_response');
  }
  return normalizedValue;
}

function isOrganizationUnitResponse(value: unknown): value is OrganizationUnitResponse {
  return isRecord(value) && (typeof value["code"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOrganizationUserPositionResponse(value: unknown): OrganizationUserPositionResponse {
  const normalizedValue = normalizeOrganizationUserPositionResponseIntegerJson(value);
  if (!(isOrganizationUserPositionResponse(normalizedValue))) {
    throw new Error('client.invalid_organization_user_position_response');
  }
  return normalizedValue;
}

function isOrganizationUserPositionResponse(value: unknown): value is OrganizationUserPositionResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isPrimary"] === 'boolean') && (typeof value["positionCode"] === 'string') && (typeof value["positionId"] === 'string' && guidPattern.test(value["positionId"])) && (typeof value["positionName"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOrganizationUserUnitResponse(value: unknown): OrganizationUserUnitResponse {
  const normalizedValue = normalizeOrganizationUserUnitResponseIntegerJson(value);
  if (!(isOrganizationUserUnitResponse(normalizedValue))) {
    throw new Error('client.invalid_organization_user_unit_response');
  }
  return normalizedValue;
}

function isOrganizationUserUnitResponse(value: unknown): value is OrganizationUserUnitResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["isPrimary"] === 'boolean') && (typeof value["unitCode"] === 'string') && (typeof value["unitId"] === 'string' && guidPattern.test(value["unitId"])) && (typeof value["unitName"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readOutboundCallLogResponse(value: unknown): OutboundCallLogResponse {
  const normalizedValue = normalizeOutboundCallLogResponseIntegerJson(value);
  if (!(isOutboundCallLogResponse(normalizedValue))) {
    throw new Error('client.invalid_outbound_call_log_response');
  }
  return normalizedValue;
}

function isOutboundCallLogResponse(value: unknown): value is OutboundCallLogResponse {
  return isRecord(value) && (typeof value["destinationHostCategory"] === 'string') && (typeof value["durationMs"] === 'number' && Number.isSafeInteger(value["durationMs"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["occurredAtUtc"] === 'string') && (typeof value["operationKey"] === 'string') && (typeof value["providerKey"] === 'string') && (typeof value["retryCount"] === 'number' && Number.isSafeInteger(value["retryCount"])) && ((value["safeErrorCode"] === null) || (typeof value["safeErrorCode"] === 'string')) && (typeof value["statusCode"] === 'number' && Number.isSafeInteger(value["statusCode"])) && (typeof value["succeeded"] === 'boolean') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["traceId"] === null) || (typeof value["traceId"] === 'string')) && ((value["userId"] === null) || (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])));
}

export function readPagedResultOfAccessLogResponse(value: unknown): PagedResultOfAccessLogResponse {
  const normalizedValue = normalizePagedResultOfAccessLogResponseIntegerJson(value);
  if (!(isPagedResultOfAccessLogResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_access_log_response');
  }
  return normalizedValue;
}

function isPagedResultOfAccessLogResponse(value: unknown): value is PagedResultOfAccessLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAccessLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfAdministrativeRegionResponse(value: unknown): PagedResultOfAdministrativeRegionResponse {
  const normalizedValue = normalizePagedResultOfAdministrativeRegionResponseIntegerJson(value);
  if (!(isPagedResultOfAdministrativeRegionResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_administrative_region_response');
  }
  return normalizedValue;
}

function isPagedResultOfAdministrativeRegionResponse(value: unknown): value is PagedResultOfAdministrativeRegionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAdministrativeRegionResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfAiAgentToolCallListItem(value: unknown): PagedResultOfAiAgentToolCallListItem {
  const normalizedValue = normalizePagedResultOfAiAgentToolCallListItemIntegerJson(value);
  if (!(isPagedResultOfAiAgentToolCallListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_ai_agent_tool_call_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfAiAgentToolCallListItem(value: unknown): value is PagedResultOfAiAgentToolCallListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAiAgentToolCallListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfAiChatSessionListItem(value: unknown): PagedResultOfAiChatSessionListItem {
  const normalizedValue = normalizePagedResultOfAiChatSessionListItemIntegerJson(value);
  if (!(isPagedResultOfAiChatSessionListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_ai_chat_session_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfAiChatSessionListItem(value: unknown): value is PagedResultOfAiChatSessionListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAiChatSessionListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfAiModelConfigListItem(value: unknown): PagedResultOfAiModelConfigListItem {
  const normalizedValue = normalizePagedResultOfAiModelConfigListItemIntegerJson(value);
  if (!(isPagedResultOfAiModelConfigListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_ai_model_config_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfAiModelConfigListItem(value: unknown): value is PagedResultOfAiModelConfigListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAiModelConfigListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfAiTenantQuotaListItem(value: unknown): PagedResultOfAiTenantQuotaListItem {
  const normalizedValue = normalizePagedResultOfAiTenantQuotaListItemIntegerJson(value);
  if (!(isPagedResultOfAiTenantQuotaListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_ai_tenant_quota_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfAiTenantQuotaListItem(value: unknown): value is PagedResultOfAiTenantQuotaListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isAiTenantQuotaListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfCodeGenerationRunResponse(value: unknown): PagedResultOfCodeGenerationRunResponse {
  const normalizedValue = normalizePagedResultOfCodeGenerationRunResponseIntegerJson(value);
  if (!(isPagedResultOfCodeGenerationRunResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_code_generation_run_response');
  }
  return normalizedValue;
}

function isPagedResultOfCodeGenerationRunResponse(value: unknown): value is PagedResultOfCodeGenerationRunResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isCodeGenerationRunResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfCodeGenerationTemplateResponse(value: unknown): PagedResultOfCodeGenerationTemplateResponse {
  const normalizedValue = normalizePagedResultOfCodeGenerationTemplateResponseIntegerJson(value);
  if (!(isPagedResultOfCodeGenerationTemplateResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_code_generation_template_response');
  }
  return normalizedValue;
}

function isPagedResultOfCodeGenerationTemplateResponse(value: unknown): value is PagedResultOfCodeGenerationTemplateResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isCodeGenerationTemplateResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfConfigEntryResponse(value: unknown): PagedResultOfConfigEntryResponse {
  const normalizedValue = normalizePagedResultOfConfigEntryResponseIntegerJson(value);
  if (!(isPagedResultOfConfigEntryResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_config_entry_response');
  }
  return normalizedValue;
}

function isPagedResultOfConfigEntryResponse(value: unknown): value is PagedResultOfConfigEntryResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isConfigEntryResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfDataApprovalRequestResponse(value: unknown): PagedResultOfDataApprovalRequestResponse {
  const normalizedValue = normalizePagedResultOfDataApprovalRequestResponseIntegerJson(value);
  if (!(isPagedResultOfDataApprovalRequestResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_data_approval_request_response');
  }
  return normalizedValue;
}

function isPagedResultOfDataApprovalRequestResponse(value: unknown): value is PagedResultOfDataApprovalRequestResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isDataApprovalRequestResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfDictItemResponse(value: unknown): PagedResultOfDictItemResponse {
  const normalizedValue = normalizePagedResultOfDictItemResponseIntegerJson(value);
  if (!(isPagedResultOfDictItemResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_dict_item_response');
  }
  return normalizedValue;
}

function isPagedResultOfDictItemResponse(value: unknown): value is PagedResultOfDictItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isDictItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfDictTypeResponse(value: unknown): PagedResultOfDictTypeResponse {
  const normalizedValue = normalizePagedResultOfDictTypeResponseIntegerJson(value);
  if (!(isPagedResultOfDictTypeResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_dict_type_response');
  }
  return normalizedValue;
}

function isPagedResultOfDictTypeResponse(value: unknown): value is PagedResultOfDictTypeResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isDictTypeResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfEnterpriseRequestResponse(value: unknown): PagedResultOfEnterpriseRequestResponse {
  const normalizedValue = normalizePagedResultOfEnterpriseRequestResponseIntegerJson(value);
  if (!(isPagedResultOfEnterpriseRequestResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_enterprise_request_response');
  }
  return normalizedValue;
}

function isPagedResultOfEnterpriseRequestResponse(value: unknown): value is PagedResultOfEnterpriseRequestResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isEnterpriseRequestResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfExceptionLogResponse(value: unknown): PagedResultOfExceptionLogResponse {
  const normalizedValue = normalizePagedResultOfExceptionLogResponseIntegerJson(value);
  if (!(isPagedResultOfExceptionLogResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_exception_log_response');
  }
  return normalizedValue;
}

function isPagedResultOfExceptionLogResponse(value: unknown): value is PagedResultOfExceptionLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isExceptionLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostAnnouncementReadReceiptResponse(value: unknown): PagedResultOfHostAnnouncementReadReceiptResponse {
  const normalizedValue = normalizePagedResultOfHostAnnouncementReadReceiptResponseIntegerJson(value);
  if (!(isPagedResultOfHostAnnouncementReadReceiptResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_announcement_read_receipt_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostAnnouncementReadReceiptResponse(value: unknown): value is PagedResultOfHostAnnouncementReadReceiptResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostAnnouncementReadReceiptResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostAnnouncementResponse(value: unknown): PagedResultOfHostAnnouncementResponse {
  const normalizedValue = normalizePagedResultOfHostAnnouncementResponseIntegerJson(value);
  if (!(isPagedResultOfHostAnnouncementResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_announcement_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostAnnouncementResponse(value: unknown): value is PagedResultOfHostAnnouncementResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostAnnouncementResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostApiKeyResponse(value: unknown): PagedResultOfHostApiKeyResponse {
  const normalizedValue = normalizePagedResultOfHostApiKeyResponseIntegerJson(value);
  if (!(isPagedResultOfHostApiKeyResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_api_key_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostApiKeyResponse(value: unknown): value is PagedResultOfHostApiKeyResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostApiKeyResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostDocumentAccessLogResponse(value: unknown): PagedResultOfHostDocumentAccessLogResponse {
  const normalizedValue = normalizePagedResultOfHostDocumentAccessLogResponseIntegerJson(value);
  if (!(isPagedResultOfHostDocumentAccessLogResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_document_access_log_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostDocumentAccessLogResponse(value: unknown): value is PagedResultOfHostDocumentAccessLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostDocumentAccessLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostDocumentItemResponse(value: unknown): PagedResultOfHostDocumentItemResponse {
  const normalizedValue = normalizePagedResultOfHostDocumentItemResponseIntegerJson(value);
  if (!(isPagedResultOfHostDocumentItemResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_document_item_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostDocumentItemResponse(value: unknown): value is PagedResultOfHostDocumentItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostDocumentItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostDocumentPreviewTaskResponse(value: unknown): PagedResultOfHostDocumentPreviewTaskResponse {
  const normalizedValue = normalizePagedResultOfHostDocumentPreviewTaskResponseIntegerJson(value);
  if (!(isPagedResultOfHostDocumentPreviewTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_document_preview_task_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostDocumentPreviewTaskResponse(value: unknown): value is PagedResultOfHostDocumentPreviewTaskResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostDocumentPreviewTaskResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostDocumentShareResponse(value: unknown): PagedResultOfHostDocumentShareResponse {
  const normalizedValue = normalizePagedResultOfHostDocumentShareResponseIntegerJson(value);
  if (!(isPagedResultOfHostDocumentShareResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_document_share_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostDocumentShareResponse(value: unknown): value is PagedResultOfHostDocumentShareResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostDocumentShareResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostFileReferenceClaimResponse(value: unknown): PagedResultOfHostFileReferenceClaimResponse {
  const normalizedValue = normalizePagedResultOfHostFileReferenceClaimResponseIntegerJson(value);
  if (!(isPagedResultOfHostFileReferenceClaimResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_file_reference_claim_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostFileReferenceClaimResponse(value: unknown): value is PagedResultOfHostFileReferenceClaimResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostFileReferenceClaimResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostFileResponse(value: unknown): PagedResultOfHostFileResponse {
  const normalizedValue = normalizePagedResultOfHostFileResponseIntegerJson(value);
  if (!(isPagedResultOfHostFileResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_file_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostFileResponse(value: unknown): value is PagedResultOfHostFileResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostFileResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostJobDefinitionResponse(value: unknown): PagedResultOfHostJobDefinitionResponse {
  const normalizedValue = normalizePagedResultOfHostJobDefinitionResponseIntegerJson(value);
  if (!(isPagedResultOfHostJobDefinitionResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_job_definition_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostJobDefinitionResponse(value: unknown): value is PagedResultOfHostJobDefinitionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostJobDefinitionResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostJobExecutionResponse(value: unknown): PagedResultOfHostJobExecutionResponse {
  const normalizedValue = normalizePagedResultOfHostJobExecutionResponseIntegerJson(value);
  if (!(isPagedResultOfHostJobExecutionResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_job_execution_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostJobExecutionResponse(value: unknown): value is PagedResultOfHostJobExecutionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostJobExecutionResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostJobScheduleResponse(value: unknown): PagedResultOfHostJobScheduleResponse {
  const normalizedValue = normalizePagedResultOfHostJobScheduleResponseIntegerJson(value);
  if (!(isPagedResultOfHostJobScheduleResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_job_schedule_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostJobScheduleResponse(value: unknown): value is PagedResultOfHostJobScheduleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostJobScheduleResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostMenuResponse(value: unknown): PagedResultOfHostMenuResponse {
  const normalizedValue = normalizePagedResultOfHostMenuResponseIntegerJson(value);
  if (!(isPagedResultOfHostMenuResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_menu_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostMenuResponse(value: unknown): value is PagedResultOfHostMenuResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostMenuResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostOnlineSessionResponse(value: unknown): PagedResultOfHostOnlineSessionResponse {
  const normalizedValue = normalizePagedResultOfHostOnlineSessionResponseIntegerJson(value);
  if (!(isPagedResultOfHostOnlineSessionResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_online_session_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostOnlineSessionResponse(value: unknown): value is PagedResultOfHostOnlineSessionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostOnlineSessionResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostReleaseNoteResponse(value: unknown): PagedResultOfHostReleaseNoteResponse {
  const normalizedValue = normalizePagedResultOfHostReleaseNoteResponseIntegerJson(value);
  if (!(isPagedResultOfHostReleaseNoteResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_release_note_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostReleaseNoteResponse(value: unknown): value is PagedResultOfHostReleaseNoteResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostReleaseNoteResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostRoleResponse(value: unknown): PagedResultOfHostRoleResponse {
  const normalizedValue = normalizePagedResultOfHostRoleResponseIntegerJson(value);
  if (!(isPagedResultOfHostRoleResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_role_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostRoleResponse(value: unknown): value is PagedResultOfHostRoleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostRoleResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfHostUserResponse(value: unknown): PagedResultOfHostUserResponse {
  const normalizedValue = normalizePagedResultOfHostUserResponseIntegerJson(value);
  if (!(isPagedResultOfHostUserResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_host_user_response');
  }
  return normalizedValue;
}

function isPagedResultOfHostUserResponse(value: unknown): value is PagedResultOfHostUserResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostUserResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfImportExportTaskResponse(value: unknown): PagedResultOfImportExportTaskResponse {
  const normalizedValue = normalizePagedResultOfImportExportTaskResponseIntegerJson(value);
  if (!(isPagedResultOfImportExportTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_import_export_task_response');
  }
  return normalizedValue;
}

function isPagedResultOfImportExportTaskResponse(value: unknown): value is PagedResultOfImportExportTaskResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isImportExportTaskResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfInboxMessageResponse(value: unknown): PagedResultOfInboxMessageResponse {
  const normalizedValue = normalizePagedResultOfInboxMessageResponseIntegerJson(value);
  if (!(isPagedResultOfInboxMessageResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_inbox_message_response');
  }
  return normalizedValue;
}

function isPagedResultOfInboxMessageResponse(value: unknown): value is PagedResultOfInboxMessageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isInboxMessageResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfK3CloudDocumentSyncResponse(value: unknown): PagedResultOfK3CloudDocumentSyncResponse {
  const normalizedValue = normalizePagedResultOfK3CloudDocumentSyncResponseIntegerJson(value);
  if (!(isPagedResultOfK3CloudDocumentSyncResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_k3_cloud_document_sync_response');
  }
  return normalizedValue;
}

function isPagedResultOfK3CloudDocumentSyncResponse(value: unknown): value is PagedResultOfK3CloudDocumentSyncResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isK3CloudDocumentSyncResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfMyReleaseNoteResponse(value: unknown): PagedResultOfMyReleaseNoteResponse {
  const normalizedValue = normalizePagedResultOfMyReleaseNoteResponseIntegerJson(value);
  if (!(isPagedResultOfMyReleaseNoteResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_my_release_note_response');
  }
  return normalizedValue;
}

function isPagedResultOfMyReleaseNoteResponse(value: unknown): value is PagedResultOfMyReleaseNoteResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isMyReleaseNoteResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfNotificationBindingResponse(value: unknown): PagedResultOfNotificationBindingResponse {
  const normalizedValue = normalizePagedResultOfNotificationBindingResponseIntegerJson(value);
  if (!(isPagedResultOfNotificationBindingResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_notification_binding_response');
  }
  return normalizedValue;
}

function isPagedResultOfNotificationBindingResponse(value: unknown): value is PagedResultOfNotificationBindingResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationBindingResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfNotificationDeliveryResponse(value: unknown): PagedResultOfNotificationDeliveryResponse {
  const normalizedValue = normalizePagedResultOfNotificationDeliveryResponseIntegerJson(value);
  if (!(isPagedResultOfNotificationDeliveryResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_notification_delivery_response');
  }
  return normalizedValue;
}

function isPagedResultOfNotificationDeliveryResponse(value: unknown): value is PagedResultOfNotificationDeliveryResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationDeliveryResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfNotificationProviderProfileResponse(value: unknown): PagedResultOfNotificationProviderProfileResponse {
  const normalizedValue = normalizePagedResultOfNotificationProviderProfileResponseIntegerJson(value);
  if (!(isPagedResultOfNotificationProviderProfileResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_notification_provider_profile_response');
  }
  return normalizedValue;
}

function isPagedResultOfNotificationProviderProfileResponse(value: unknown): value is PagedResultOfNotificationProviderProfileResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationProviderProfileResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfNotificationTemplateResponse(value: unknown): PagedResultOfNotificationTemplateResponse {
  const normalizedValue = normalizePagedResultOfNotificationTemplateResponseIntegerJson(value);
  if (!(isPagedResultOfNotificationTemplateResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_notification_template_response');
  }
  return normalizedValue;
}

function isPagedResultOfNotificationTemplateResponse(value: unknown): value is PagedResultOfNotificationTemplateResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isNotificationTemplateResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOcrIdCardTaskResponse(value: unknown): PagedResultOfOcrIdCardTaskResponse {
  const normalizedValue = normalizePagedResultOfOcrIdCardTaskResponseIntegerJson(value);
  if (!(isPagedResultOfOcrIdCardTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_ocr_id_card_task_response');
  }
  return normalizedValue;
}

function isPagedResultOfOcrIdCardTaskResponse(value: unknown): value is PagedResultOfOcrIdCardTaskResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOcrIdCardTaskResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOperationLogResponse(value: unknown): PagedResultOfOperationLogResponse {
  const normalizedValue = normalizePagedResultOfOperationLogResponseIntegerJson(value);
  if (!(isPagedResultOfOperationLogResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_operation_log_response');
  }
  return normalizedValue;
}

function isPagedResultOfOperationLogResponse(value: unknown): value is PagedResultOfOperationLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOperationLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOrganizationAssignableUserResponse(value: unknown): PagedResultOfOrganizationAssignableUserResponse {
  const normalizedValue = normalizePagedResultOfOrganizationAssignableUserResponseIntegerJson(value);
  if (!(isPagedResultOfOrganizationAssignableUserResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_organization_assignable_user_response');
  }
  return normalizedValue;
}

function isPagedResultOfOrganizationAssignableUserResponse(value: unknown): value is PagedResultOfOrganizationAssignableUserResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationAssignableUserResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOrganizationPositionLevelResponse(value: unknown): PagedResultOfOrganizationPositionLevelResponse {
  const normalizedValue = normalizePagedResultOfOrganizationPositionLevelResponseIntegerJson(value);
  if (!(isPagedResultOfOrganizationPositionLevelResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_organization_position_level_response');
  }
  return normalizedValue;
}

function isPagedResultOfOrganizationPositionLevelResponse(value: unknown): value is PagedResultOfOrganizationPositionLevelResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationPositionLevelResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOrganizationPositionResponse(value: unknown): PagedResultOfOrganizationPositionResponse {
  const normalizedValue = normalizePagedResultOfOrganizationPositionResponseIntegerJson(value);
  if (!(isPagedResultOfOrganizationPositionResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_organization_position_response');
  }
  return normalizedValue;
}

function isPagedResultOfOrganizationPositionResponse(value: unknown): value is PagedResultOfOrganizationPositionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationPositionResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOrganizationUnitResponse(value: unknown): PagedResultOfOrganizationUnitResponse {
  const normalizedValue = normalizePagedResultOfOrganizationUnitResponseIntegerJson(value);
  if (!(isPagedResultOfOrganizationUnitResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_organization_unit_response');
  }
  return normalizedValue;
}

function isPagedResultOfOrganizationUnitResponse(value: unknown): value is PagedResultOfOrganizationUnitResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationUnitResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOrganizationUserPositionResponse(value: unknown): PagedResultOfOrganizationUserPositionResponse {
  const normalizedValue = normalizePagedResultOfOrganizationUserPositionResponseIntegerJson(value);
  if (!(isPagedResultOfOrganizationUserPositionResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_organization_user_position_response');
  }
  return normalizedValue;
}

function isPagedResultOfOrganizationUserPositionResponse(value: unknown): value is PagedResultOfOrganizationUserPositionResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationUserPositionResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOrganizationUserUnitResponse(value: unknown): PagedResultOfOrganizationUserUnitResponse {
  const normalizedValue = normalizePagedResultOfOrganizationUserUnitResponseIntegerJson(value);
  if (!(isPagedResultOfOrganizationUserUnitResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_organization_user_unit_response');
  }
  return normalizedValue;
}

function isPagedResultOfOrganizationUserUnitResponse(value: unknown): value is PagedResultOfOrganizationUserUnitResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOrganizationUserUnitResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfOutboundCallLogResponse(value: unknown): PagedResultOfOutboundCallLogResponse {
  const normalizedValue = normalizePagedResultOfOutboundCallLogResponseIntegerJson(value);
  if (!(isPagedResultOfOutboundCallLogResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_outbound_call_log_response');
  }
  return normalizedValue;
}

function isPagedResultOfOutboundCallLogResponse(value: unknown): value is PagedResultOfOutboundCallLogResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isOutboundCallLogResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfPaymentMerchantConfigListItem(value: unknown): PagedResultOfPaymentMerchantConfigListItem {
  const normalizedValue = normalizePagedResultOfPaymentMerchantConfigListItemIntegerJson(value);
  if (!(isPagedResultOfPaymentMerchantConfigListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_payment_merchant_config_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfPaymentMerchantConfigListItem(value: unknown): value is PagedResultOfPaymentMerchantConfigListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isPaymentMerchantConfigListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfPaymentOrderListItem(value: unknown): PagedResultOfPaymentOrderListItem {
  const normalizedValue = normalizePagedResultOfPaymentOrderListItemIntegerJson(value);
  if (!(isPagedResultOfPaymentOrderListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_payment_order_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfPaymentOrderListItem(value: unknown): value is PagedResultOfPaymentOrderListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isPaymentOrderListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfPaymentRefundListItem(value: unknown): PagedResultOfPaymentRefundListItem {
  const normalizedValue = normalizePagedResultOfPaymentRefundListItemIntegerJson(value);
  if (!(isPagedResultOfPaymentRefundListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_payment_refund_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfPaymentRefundListItem(value: unknown): value is PagedResultOfPaymentRefundListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isPaymentRefundListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfPersonalScheduleResponse(value: unknown): PagedResultOfPersonalScheduleResponse {
  const normalizedValue = normalizePagedResultOfPersonalScheduleResponseIntegerJson(value);
  if (!(isPagedResultOfPersonalScheduleResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_personal_schedule_response');
  }
  return normalizedValue;
}

function isPagedResultOfPersonalScheduleResponse(value: unknown): value is PagedResultOfPersonalScheduleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isPersonalScheduleResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfReceivedHostAnnouncementListItemResponse(value: unknown): PagedResultOfReceivedHostAnnouncementListItemResponse {
  const normalizedValue = normalizePagedResultOfReceivedHostAnnouncementListItemResponseIntegerJson(value);
  if (!(isPagedResultOfReceivedHostAnnouncementListItemResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_received_host_announcement_list_item_response');
  }
  return normalizedValue;
}

function isPagedResultOfReceivedHostAnnouncementListItemResponse(value: unknown): value is PagedResultOfReceivedHostAnnouncementListItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isReceivedHostAnnouncementListItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfReportingDataSourceListItem(value: unknown): PagedResultOfReportingDataSourceListItem {
  const normalizedValue = normalizePagedResultOfReportingDataSourceListItemIntegerJson(value);
  if (!(isPagedResultOfReportingDataSourceListItem(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_reporting_data_source_list_item');
  }
  return normalizedValue;
}

function isPagedResultOfReportingDataSourceListItem(value: unknown): value is PagedResultOfReportingDataSourceListItem {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isReportingDataSourceListItem(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfReportingExportTaskResponse(value: unknown): PagedResultOfReportingExportTaskResponse {
  const normalizedValue = normalizePagedResultOfReportingExportTaskResponseIntegerJson(value);
  if (!(isPagedResultOfReportingExportTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_reporting_export_task_response');
  }
  return normalizedValue;
}

function isPagedResultOfReportingExportTaskResponse(value: unknown): value is PagedResultOfReportingExportTaskResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isReportingExportTaskResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfSerialNumberRuleResponse(value: unknown): PagedResultOfSerialNumberRuleResponse {
  const normalizedValue = normalizePagedResultOfSerialNumberRuleResponseIntegerJson(value);
  if (!(isPagedResultOfSerialNumberRuleResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_serial_number_rule_response');
  }
  return normalizedValue;
}

function isPagedResultOfSerialNumberRuleResponse(value: unknown): value is PagedResultOfSerialNumberRuleResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isSerialNumberRuleResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfTenantPackageSummary(value: unknown): PagedResultOfTenantPackageSummary {
  const normalizedValue = normalizePagedResultOfTenantPackageSummaryIntegerJson(value);
  if (!(isPagedResultOfTenantPackageSummary(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_tenant_package_summary');
  }
  return normalizedValue;
}

function isPagedResultOfTenantPackageSummary(value: unknown): value is PagedResultOfTenantPackageSummary {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isTenantPackageSummary(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfTenantSummary(value: unknown): PagedResultOfTenantSummary {
  const normalizedValue = normalizePagedResultOfTenantSummaryIntegerJson(value);
  if (!(isPagedResultOfTenantSummary(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_tenant_summary');
  }
  return normalizedValue;
}

function isPagedResultOfTenantSummary(value: unknown): value is PagedResultOfTenantSummary {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isTenantSummary(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfWorkflowInstanceListItemResponse(value: unknown): PagedResultOfWorkflowInstanceListItemResponse {
  const normalizedValue = normalizePagedResultOfWorkflowInstanceListItemResponseIntegerJson(value);
  if (!(isPagedResultOfWorkflowInstanceListItemResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_workflow_instance_list_item_response');
  }
  return normalizedValue;
}

function isPagedResultOfWorkflowInstanceListItemResponse(value: unknown): value is PagedResultOfWorkflowInstanceListItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWorkflowInstanceListItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfWorkflowRecoveryTaskResponse(value: unknown): PagedResultOfWorkflowRecoveryTaskResponse {
  const normalizedValue = normalizePagedResultOfWorkflowRecoveryTaskResponseIntegerJson(value);
  if (!(isPagedResultOfWorkflowRecoveryTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_workflow_recovery_task_response');
  }
  return normalizedValue;
}

function isPagedResultOfWorkflowRecoveryTaskResponse(value: unknown): value is PagedResultOfWorkflowRecoveryTaskResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWorkflowRecoveryTaskResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfWorkflowTodoListItemResponse(value: unknown): PagedResultOfWorkflowTodoListItemResponse {
  const normalizedValue = normalizePagedResultOfWorkflowTodoListItemResponseIntegerJson(value);
  if (!(isPagedResultOfWorkflowTodoListItemResponse(normalizedValue))) {
    throw new Error('client.invalid_paged_result_of_workflow_todo_list_item_response');
  }
  return normalizedValue;
}

function isPagedResultOfWorkflowTodoListItemResponse(value: unknown): value is PagedResultOfWorkflowTodoListItemResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWorkflowTodoListItemResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPauseWorkflowInstanceRequest(value: unknown): PauseWorkflowInstanceRequest {
  const normalizedValue = normalizePauseWorkflowInstanceRequestIntegerJson(value);
  if (!(isPauseWorkflowInstanceRequest(normalizedValue))) {
    throw new Error('client.invalid_pause_workflow_instance_request');
  }
  return normalizedValue;
}

function isPauseWorkflowInstanceRequest(value: unknown): value is PauseWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readPaymentMerchantConfigListItem(value: unknown): PaymentMerchantConfigListItem {
  const normalizedValue = normalizePaymentMerchantConfigListItemIntegerJson(value);
  if (!(isPaymentMerchantConfigListItem(normalizedValue))) {
    throw new Error('client.invalid_payment_merchant_config_list_item');
  }
  return normalizedValue;
}

function isPaymentMerchantConfigListItem(value: unknown): value is PaymentMerchantConfigListItem {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["hasApiV3Key"] === 'boolean') && (typeof value["hasPrivateKey"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["maskedAppId"] === 'string') && (typeof value["maskedCertificateSerialNo"] === 'string') && (typeof value["maskedMerchantId"] === 'string') && (typeof value["maskedNotifyUrl"] === 'string') && (typeof value["maskedReturnUrl"] === 'string') && (typeof value["name"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPaymentMerchantConfigResponse(value: unknown): PaymentMerchantConfigResponse {
  const normalizedValue = normalizePaymentMerchantConfigResponseIntegerJson(value);
  if (!(isPaymentMerchantConfigResponse(normalizedValue))) {
    throw new Error('client.invalid_payment_merchant_config_response');
  }
  return normalizedValue;
}

function isPaymentMerchantConfigResponse(value: unknown): value is PaymentMerchantConfigResponse {
  return isRecord(value) && (typeof value["appId"] === 'string') && (typeof value["certificateSerialNo"] === 'string') && (typeof value["channelKey"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["hasApiV3Key"] === 'boolean') && (typeof value["hasPrivateKey"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["merchantId"] === 'string') && (typeof value["name"] === 'string') && (typeof value["notifyUrl"] === 'string') && (typeof value["returnUrl"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPaymentOrderListItem(value: unknown): PaymentOrderListItem {
  const normalizedValue = normalizePaymentOrderListItemIntegerJson(value);
  if (!(isPaymentOrderListItem(normalizedValue))) {
    throw new Error('client.invalid_payment_order_list_item');
  }
  return normalizedValue;
}

function isPaymentOrderListItem(value: unknown): value is PaymentOrderListItem {
  return isRecord(value) && (typeof value["amountMinor"] === 'number' && Number.isSafeInteger(value["amountMinor"])) && (typeof value["channelKey"] === 'string') && ((value["codeUrl"] === null) || (typeof value["codeUrl"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["currency"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["failMessage"] === null) || (typeof value["failMessage"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["merchantConfigId"] === 'string' && guidPattern.test(value["merchantConfigId"])) && (typeof value["outTradeNo"] === 'string') && ((value["paidAtUtc"] === null) || (typeof value["paidAtUtc"] === 'string')) && ((value["providerTransactionId"] === null) || (typeof value["providerTransactionId"] === 'string')) && (typeof value["subject"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["tradeStateKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPaymentOrderResponse(value: unknown): PaymentOrderResponse {
  const normalizedValue = normalizePaymentOrderResponseIntegerJson(value);
  if (!(isPaymentOrderResponse(normalizedValue))) {
    throw new Error('client.invalid_payment_order_response');
  }
  return normalizedValue;
}

function isPaymentOrderResponse(value: unknown): value is PaymentOrderResponse {
  return isRecord(value) && (typeof value["amountMinor"] === 'number' && Number.isSafeInteger(value["amountMinor"])) && (typeof value["channelKey"] === 'string') && ((value["codeUrl"] === null) || (typeof value["codeUrl"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["currency"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["failMessage"] === null) || (typeof value["failMessage"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["merchantConfigId"] === 'string' && guidPattern.test(value["merchantConfigId"])) && (typeof value["outTradeNo"] === 'string') && ((value["paidAtUtc"] === null) || (typeof value["paidAtUtc"] === 'string')) && ((value["providerTransactionId"] === null) || (typeof value["providerTransactionId"] === 'string')) && (typeof value["subject"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["tradeStateKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPaymentRefundListItem(value: unknown): PaymentRefundListItem {
  const normalizedValue = normalizePaymentRefundListItemIntegerJson(value);
  if (!(isPaymentRefundListItem(normalizedValue))) {
    throw new Error('client.invalid_payment_refund_list_item');
  }
  return normalizedValue;
}

function isPaymentRefundListItem(value: unknown): value is PaymentRefundListItem {
  return isRecord(value) && (typeof value["amountMinor"] === 'number' && Number.isSafeInteger(value["amountMinor"])) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["currency"] === 'string') && ((value["failMessage"] === null) || (typeof value["failMessage"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["orderId"] === 'string' && guidPattern.test(value["orderId"])) && (typeof value["outRefundNo"] === 'string') && (typeof value["outTradeNo"] === 'string') && ((value["providerRefundId"] === null) || (typeof value["providerRefundId"] === 'string')) && (typeof value["reason"] === 'string') && (typeof value["refundStateKey"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPaymentRefundResponse(value: unknown): PaymentRefundResponse {
  const normalizedValue = normalizePaymentRefundResponseIntegerJson(value);
  if (!(isPaymentRefundResponse(normalizedValue))) {
    throw new Error('client.invalid_payment_refund_response');
  }
  return normalizedValue;
}

function isPaymentRefundResponse(value: unknown): value is PaymentRefundResponse {
  return isRecord(value) && (typeof value["amountMinor"] === 'number' && Number.isSafeInteger(value["amountMinor"])) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["currency"] === 'string') && ((value["failMessage"] === null) || (typeof value["failMessage"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["merchantConfigId"] === 'string' && guidPattern.test(value["merchantConfigId"])) && (typeof value["orderId"] === 'string' && guidPattern.test(value["orderId"])) && (typeof value["outRefundNo"] === 'string') && (typeof value["outTradeNo"] === 'string') && ((value["providerRefundId"] === null) || (typeof value["providerRefundId"] === 'string')) && (typeof value["reason"] === 'string') && (typeof value["refundStateKey"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPersonalScheduleResponse(value: unknown): PersonalScheduleResponse {
  const normalizedValue = normalizePersonalScheduleResponseIntegerJson(value);
  if (!(isPersonalScheduleResponse(normalizedValue))) {
    throw new Error('client.invalid_personal_schedule_response');
  }
  return normalizedValue;
}

function isPersonalScheduleResponse(value: unknown): value is PersonalScheduleResponse {
  return isRecord(value) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["endAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["startAtUtc"] === 'string') && (typeof value["status"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPreviewGoViewProjectRequest(value: unknown): PreviewGoViewProjectRequest {
  const normalizedValue = normalizePreviewGoViewProjectRequestIntegerJson(value);
  if (!(isPreviewGoViewProjectRequest(normalizedValue))) {
    throw new Error('client.invalid_preview_go_view_project_request');
  }
  return normalizedValue;
}

function isPreviewGoViewProjectRequest(value: unknown): value is PreviewGoViewProjectRequest {
  return isRecord(value) && ((value["versionNumber"] === null) || (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"])));
}

export function readPreviewPrintingTemplateRequest(value: unknown): PreviewPrintingTemplateRequest {
  const normalizedValue = normalizePreviewPrintingTemplateRequestIntegerJson(value);
  if (!(isPreviewPrintingTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_preview_printing_template_request');
  }
  return normalizedValue;
}

function isPreviewPrintingTemplateRequest(value: unknown): value is PreviewPrintingTemplateRequest {
  return isRecord(value) && ((value["versionNumber"] === null) || (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"])));
}

export function readPreviewSerialNumberRequest(value: unknown): PreviewSerialNumberRequest {
  const normalizedValue = normalizePreviewSerialNumberRequestIntegerJson(value);
  if (!(isPreviewSerialNumberRequest(normalizedValue))) {
    throw new Error('client.invalid_preview_serial_number_request');
  }
  return normalizedValue;
}

function isPreviewSerialNumberRequest(value: unknown): value is PreviewSerialNumberRequest {
  return isRecord(value) && (typeof value["atUtc"] === 'string') && (typeof value["pattern"] === 'string') && (value["resetInterval"] === undefined || (isSerialNumberResetInterval(value["resetInterval"]))) && (isSerialNumberRuleScope(value["scope"])) && (typeof value["sequenceValue"] === 'number' && Number.isSafeInteger(value["sequenceValue"])) && ((value["tenantIdentifier"] === null) || (typeof value["tenantIdentifier"] === 'string'));
}

export function readPreviewWorkflowAssigneeRequest(value: unknown): PreviewWorkflowAssigneeRequest {
  if (!(isPreviewWorkflowAssigneeRequest(value))) {
    throw new Error('client.invalid_preview_workflow_assignee_request');
  }
  return value;
}

function isPreviewWorkflowAssigneeRequest(value: unknown): value is PreviewWorkflowAssigneeRequest {
  return isRecord(value) && (isJsonElement(value["assigneePolicy"])) && ((value["initiatorUserId"] === null) || (typeof value["initiatorUserId"] === 'string' && guidPattern.test(value["initiatorUserId"])));
}

export function readPrintingFormFieldDefinition(value: unknown): PrintingFormFieldDefinition {
  if (!(isPrintingFormFieldDefinition(value))) {
    throw new Error('client.invalid_printing_form_field_definition');
  }
  return value;
}

function isPrintingFormFieldDefinition(value: unknown): value is PrintingFormFieldDefinition {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["fieldKey"] === 'string');
}

export function readPrintingFormSchemaDefinition(value: unknown): PrintingFormSchemaDefinition {
  if (!(isPrintingFormSchemaDefinition(value))) {
    throw new Error('client.invalid_printing_form_schema_definition');
  }
  return value;
}

function isPrintingFormSchemaDefinition(value: unknown): value is PrintingFormSchemaDefinition {
  return isRecord(value) && (typeof value["description"] === 'string') && (typeof value["displayName"] === 'string') && (Array.isArray(value["fields"]) && value["fields"].every(item15 => isPrintingFormFieldDefinition(item15))) && (typeof value["formSchemaKey"] === 'string');
}

export function readPrintingTemplatePreviewResponse(value: unknown): PrintingTemplatePreviewResponse {
  const normalizedValue = normalizePrintingTemplatePreviewResponseIntegerJson(value);
  if (!(isPrintingTemplatePreviewResponse(normalizedValue))) {
    throw new Error('client.invalid_printing_template_preview_response');
  }
  return normalizedValue;
}

function isPrintingTemplatePreviewResponse(value: unknown): value is PrintingTemplatePreviewResponse {
  return isRecord(value) && (isRecord(value["boundFields"])) && (typeof value["formSchemaKey"] === 'string') && (typeof value["generatedAtUtc"] === 'string') && (typeof value["html"] === 'string') && (typeof value["templateId"] === 'string' && guidPattern.test(value["templateId"])) && (typeof value["templateKey"] === 'string') && (typeof value["templateName"] === 'string') && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readPrintingTemplateResponse(value: unknown): PrintingTemplateResponse {
  const normalizedValue = normalizePrintingTemplateResponseIntegerJson(value);
  if (!(isPrintingTemplateResponse(normalizedValue))) {
    throw new Error('client.invalid_printing_template_response');
  }
  return normalizedValue;
}

function isPrintingTemplateResponse(value: unknown): value is PrintingTemplateResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["formSchemaKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isSafeInteger(value["latestPublishedVersionNumber"])) && (typeof value["layoutHtml"] === 'string') && (typeof value["name"] === 'string') && (typeof value["templateKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPrintingTemplateVersionResponse(value: unknown): PrintingTemplateVersionResponse {
  const normalizedValue = normalizePrintingTemplateVersionResponseIntegerJson(value);
  if (!(isPrintingTemplateVersionResponse(normalizedValue))) {
    throw new Error('client.invalid_printing_template_version_response');
  }
  return normalizedValue;
}

function isPrintingTemplateVersionResponse(value: unknown): value is PrintingTemplateVersionResponse {
  return isRecord(value) && ((value["changeNote"] === null) || (typeof value["changeNote"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["layoutHtml"] === 'string') && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"])) && (typeof value["templateId"] === 'string' && guidPattern.test(value["templateId"])) && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readProblemDetails(value: unknown): ProblemDetails {
  const normalizedValue = normalizeProblemDetailsIntegerJson(value);
  if (!(isProblemDetails(normalizedValue))) {
    throw new Error('client.invalid_problem_details');
  }
  return normalizedValue;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return isRecord(value) && (value["detail"] === undefined || ((value["detail"] === null) || (typeof value["detail"] === 'string'))) && (value["instance"] === undefined || ((value["instance"] === null) || (typeof value["instance"] === 'string'))) && (value["status"] === undefined || ((value["status"] === null) || (typeof value["status"] === 'number' && Number.isSafeInteger(value["status"])))) && (value["title"] === undefined || ((value["title"] === null) || (typeof value["title"] === 'string'))) && (value["type"] === undefined || ((value["type"] === null) || (typeof value["type"] === 'string')));
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

export function readPublishGoViewProjectRequest(value: unknown): PublishGoViewProjectRequest {
  const normalizedValue = normalizePublishGoViewProjectRequestIntegerJson(value);
  if (!(isPublishGoViewProjectRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_go_view_project_request');
  }
  return normalizedValue;
}

function isPublishGoViewProjectRequest(value: unknown): value is PublishGoViewProjectRequest {
  return isRecord(value) && ((value["changeNote"] === null) || (typeof value["changeNote"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishHostAnnouncementRequest(value: unknown): PublishHostAnnouncementRequest {
  const normalizedValue = normalizePublishHostAnnouncementRequestIntegerJson(value);
  if (!(isPublishHostAnnouncementRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_host_announcement_request');
  }
  return normalizedValue;
}

function isPublishHostAnnouncementRequest(value: unknown): value is PublishHostAnnouncementRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishHostReleaseNoteRequest(value: unknown): PublishHostReleaseNoteRequest {
  const normalizedValue = normalizePublishHostReleaseNoteRequestIntegerJson(value);
  if (!(isPublishHostReleaseNoteRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_host_release_note_request');
  }
  return normalizedValue;
}

function isPublishHostReleaseNoteRequest(value: unknown): value is PublishHostReleaseNoteRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishNotificationBindingRequest(value: unknown): PublishNotificationBindingRequest {
  const normalizedValue = normalizePublishNotificationBindingRequestIntegerJson(value);
  if (!(isPublishNotificationBindingRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_notification_binding_request');
  }
  return normalizedValue;
}

function isPublishNotificationBindingRequest(value: unknown): value is PublishNotificationBindingRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishNotificationProviderProfileRequest(value: unknown): PublishNotificationProviderProfileRequest {
  const normalizedValue = normalizePublishNotificationProviderProfileRequestIntegerJson(value);
  if (!(isPublishNotificationProviderProfileRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_notification_provider_profile_request');
  }
  return normalizedValue;
}

function isPublishNotificationProviderProfileRequest(value: unknown): value is PublishNotificationProviderProfileRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishNotificationTemplateRequest(value: unknown): PublishNotificationTemplateRequest {
  const normalizedValue = normalizePublishNotificationTemplateRequestIntegerJson(value);
  if (!(isPublishNotificationTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_notification_template_request');
  }
  return normalizedValue;
}

function isPublishNotificationTemplateRequest(value: unknown): value is PublishNotificationTemplateRequest {
  return isRecord(value) && (typeof value["contentClassificationKey"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishPrintingTemplateRequest(value: unknown): PublishPrintingTemplateRequest {
  const normalizedValue = normalizePublishPrintingTemplateRequestIntegerJson(value);
  if (!(isPublishPrintingTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_printing_template_request');
  }
  return normalizedValue;
}

function isPublishPrintingTemplateRequest(value: unknown): value is PublishPrintingTemplateRequest {
  return isRecord(value) && ((value["changeNote"] === null) || (typeof value["changeNote"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishReportingDefinitionRequest(value: unknown): PublishReportingDefinitionRequest {
  const normalizedValue = normalizePublishReportingDefinitionRequestIntegerJson(value);
  if (!(isPublishReportingDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_reporting_definition_request');
  }
  return normalizedValue;
}

function isPublishReportingDefinitionRequest(value: unknown): value is PublishReportingDefinitionRequest {
  return isRecord(value) && ((value["changeNote"] === null) || (typeof value["changeNote"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readPublishWorkflowDefinitionRequest(value: unknown): PublishWorkflowDefinitionRequest {
  const normalizedValue = normalizePublishWorkflowDefinitionRequestIntegerJson(value);
  if (!(isPublishWorkflowDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_workflow_definition_request');
  }
  return normalizedValue;
}

function isPublishWorkflowDefinitionRequest(value: unknown): value is PublishWorkflowDefinitionRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"]));
}

export function readPublishWorkflowFormRequest(value: unknown): PublishWorkflowFormRequest {
  const normalizedValue = normalizePublishWorkflowFormRequestIntegerJson(value);
  if (!(isPublishWorkflowFormRequest(normalizedValue))) {
    throw new Error('client.invalid_publish_workflow_form_request');
  }
  return normalizedValue;
}

function isPublishWorkflowFormRequest(value: unknown): value is PublishWorkflowFormRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"]));
}

export function readReassignWorkflowInstanceRequest(value: unknown): ReassignWorkflowInstanceRequest {
  const normalizedValue = normalizeReassignWorkflowInstanceRequestIntegerJson(value);
  if (!(isReassignWorkflowInstanceRequest(normalizedValue))) {
    throw new Error('client.invalid_reassign_workflow_instance_request');
  }
  return normalizedValue;
}

function isReassignWorkflowInstanceRequest(value: unknown): value is ReassignWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readReceivedHostAnnouncementDetailResponse(value: unknown): ReceivedHostAnnouncementDetailResponse {
  if (!(isReceivedHostAnnouncementDetailResponse(value))) {
    throw new Error('client.invalid_received_host_announcement_detail_response');
  }
  return value;
}

function isReceivedHostAnnouncementDetailResponse(value: unknown): value is ReceivedHostAnnouncementDetailResponse {
  return isRecord(value) && (typeof value["audienceKind"] === 'string') && (typeof value["content"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isRead"] === 'boolean') && (typeof value["kind"] === 'string') && (typeof value["publishedAtUtc"] === 'string') && ((value["publishedByUserId"] === null) || (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"]))) && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && (typeof value["title"] === 'string');
}

export function readReceivedHostAnnouncementListItemResponse(value: unknown): ReceivedHostAnnouncementListItemResponse {
  if (!(isReceivedHostAnnouncementListItemResponse(value))) {
    throw new Error('client.invalid_received_host_announcement_list_item_response');
  }
  return value;
}

function isReceivedHostAnnouncementListItemResponse(value: unknown): value is ReceivedHostAnnouncementListItemResponse {
  return isRecord(value) && (typeof value["audienceKind"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isRead"] === 'boolean') && (typeof value["kind"] === 'string') && (typeof value["publishedAtUtc"] === 'string') && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && (typeof value["title"] === 'string');
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
  const normalizedValue = normalizeReconcileWorkflowRecoveryTaskRequestIntegerJson(value);
  if (!(isReconcileWorkflowRecoveryTaskRequest(normalizedValue))) {
    throw new Error('client.invalid_reconcile_workflow_recovery_task_request');
  }
  return normalizedValue;
}

function isReconcileWorkflowRecoveryTaskRequest(value: unknown): value is ReconcileWorkflowRecoveryTaskRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readRecoverWorkflowInstanceRequest(value: unknown): RecoverWorkflowInstanceRequest {
  const normalizedValue = normalizeRecoverWorkflowInstanceRequestIntegerJson(value);
  if (!(isRecoverWorkflowInstanceRequest(normalizedValue))) {
    throw new Error('client.invalid_recover_workflow_instance_request');
  }
  return normalizedValue;
}

function isRecoverWorkflowInstanceRequest(value: unknown): value is RecoverWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && (typeof value["reason"] === 'string');
}

export function readReplaceHostRoleFieldGrantsRequest(value: unknown): ReplaceHostRoleFieldGrantsRequest {
  const normalizedValue = normalizeReplaceHostRoleFieldGrantsRequestIntegerJson(value);
  if (!(isReplaceHostRoleFieldGrantsRequest(normalizedValue))) {
    throw new Error('client.invalid_replace_host_role_field_grants_request');
  }
  return normalizedValue;
}

function isReplaceHostRoleFieldGrantsRequest(value: unknown): value is ReplaceHostRoleFieldGrantsRequest {
  return isRecord(value) && (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string')) && (typeof value["resourceKey"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReplaceHostRoleMembersRequest(value: unknown): ReplaceHostRoleMembersRequest {
  const normalizedValue = normalizeReplaceHostRoleMembersRequestIntegerJson(value);
  if (!(isReplaceHostRoleMembersRequest(normalizedValue))) {
    throw new Error('client.invalid_replace_host_role_members_request');
  }
  return normalizedValue;
}

function isReplaceHostRoleMembersRequest(value: unknown): value is ReplaceHostRoleMembersRequest {
  return isRecord(value) && (Array.isArray(value["userIds"]) && value["userIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReplaceHostRolePermissionsRequest(value: unknown): ReplaceHostRolePermissionsRequest {
  const normalizedValue = normalizeReplaceHostRolePermissionsRequestIntegerJson(value);
  if (!(isReplaceHostRolePermissionsRequest(normalizedValue))) {
    throw new Error('client.invalid_replace_host_role_permissions_request');
  }
  return normalizedValue;
}

function isReplaceHostRolePermissionsRequest(value: unknown): value is ReplaceHostRolePermissionsRequest {
  return isRecord(value) && (Array.isArray(value["permissionCodes"]) && value["permissionCodes"].every(item24 => typeof item24 === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReplaceHostUserRolesRequest(value: unknown): ReplaceHostUserRolesRequest {
  const normalizedValue = normalizeReplaceHostUserRolesRequestIntegerJson(value);
  if (!(isReplaceHostUserRolesRequest(normalizedValue))) {
    throw new Error('client.invalid_replace_host_user_roles_request');
  }
  return normalizedValue;
}

function isReplaceHostUserRolesRequest(value: unknown): value is ReplaceHostUserRolesRequest {
  return isRecord(value) && (Array.isArray(value["roleIds"]) && value["roleIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReportingDataSourceListItem(value: unknown): ReportingDataSourceListItem {
  const normalizedValue = normalizeReportingDataSourceListItemIntegerJson(value);
  if (!(isReportingDataSourceListItem(normalizedValue))) {
    throw new Error('client.invalid_reporting_data_source_list_item');
  }
  return normalizedValue;
}

function isReportingDataSourceListItem(value: unknown): value is ReportingDataSourceListItem {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["hasPassword"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["lastTestedAtUtc"] === null) || (typeof value["lastTestedAtUtc"] === 'string')) && ((value["lastTestMessage"] === null) || (typeof value["lastTestMessage"] === 'string')) && ((value["lastTestStatusKey"] === null) || (typeof value["lastTestStatusKey"] === 'string')) && (typeof value["maskedDatabaseName"] === 'string') && (typeof value["maskedServerEndpoint"] === 'string') && (typeof value["maskedUsername"] === 'string') && (typeof value["name"] === 'string') && (typeof value["providerKey"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["trustServerCertificate"] === 'boolean') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReportingDataSourceResponse(value: unknown): ReportingDataSourceResponse {
  const normalizedValue = normalizeReportingDataSourceResponseIntegerJson(value);
  if (!(isReportingDataSourceResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_data_source_response');
  }
  return normalizedValue;
}

function isReportingDataSourceResponse(value: unknown): value is ReportingDataSourceResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["databaseName"] === 'string') && (typeof value["hasPassword"] === 'boolean') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && ((value["lastTestedAtUtc"] === null) || (typeof value["lastTestedAtUtc"] === 'string')) && ((value["lastTestMessage"] === null) || (typeof value["lastTestMessage"] === 'string')) && ((value["lastTestStatusKey"] === null) || (typeof value["lastTestStatusKey"] === 'string')) && (typeof value["name"] === 'string') && (typeof value["port"] === 'number' && Number.isSafeInteger(value["port"])) && (typeof value["providerKey"] === 'string') && (typeof value["serverHost"] === 'string') && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["trustServerCertificate"] === 'boolean') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReportingDefinitionResponse(value: unknown): ReportingDefinitionResponse {
  const normalizedValue = normalizeReportingDefinitionResponseIntegerJson(value);
  if (!(isReportingDefinitionResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_definition_response');
  }
  return normalizedValue;
}

function isReportingDefinitionResponse(value: unknown): value is ReportingDefinitionResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["dataSourceId"] === 'string' && guidPattern.test(value["dataSourceId"])) && (typeof value["definitionKey"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["groupId"] === 'string' && guidPattern.test(value["groupId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["latestPublishedVersionNumber"] === 'number' && Number.isSafeInteger(value["latestPublishedVersionNumber"])) && (typeof value["layoutConfigJson"] === 'string') && (typeof value["name"] === 'string') && (Array.isArray(value["parameterSchema"]) && value["parameterSchema"].every(item24 => isReportingParameterSchemaEntry(item24))) && (typeof value["queryPortKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReportingDefinitionVersionResponse(value: unknown): ReportingDefinitionVersionResponse {
  const normalizedValue = normalizeReportingDefinitionVersionResponseIntegerJson(value);
  if (!(isReportingDefinitionVersionResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_definition_version_response');
  }
  return normalizedValue;
}

function isReportingDefinitionVersionResponse(value: unknown): value is ReportingDefinitionVersionResponse {
  return isRecord(value) && ((value["changeNote"] === null) || (typeof value["changeNote"] === 'string')) && (typeof value["dataSourceId"] === 'string' && guidPattern.test(value["dataSourceId"])) && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["layoutConfigJson"] === 'string') && (Array.isArray(value["parameterSchema"]) && value["parameterSchema"].every(item24 => isReportingParameterSchemaEntry(item24))) && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedByUserId"] === 'string' && guidPattern.test(value["publishedByUserId"])) && (typeof value["queryPortKey"] === 'string') && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readReportingExecutionColumnDefinition(value: unknown): ReportingExecutionColumnDefinition {
  if (!(isReportingExecutionColumnDefinition(value))) {
    throw new Error('client.invalid_reporting_execution_column_definition');
  }
  return value;
}

function isReportingExecutionColumnDefinition(value: unknown): value is ReportingExecutionColumnDefinition {
  return isRecord(value) && (typeof value["columnKey"] === 'string') && (typeof value["displayName"] === 'string');
}

export function readReportingExecutionPageResponse(value: unknown): ReportingExecutionPageResponse {
  const normalizedValue = normalizeReportingExecutionPageResponseIntegerJson(value);
  if (!(isReportingExecutionPageResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_execution_page_response');
  }
  return normalizedValue;
}

function isReportingExecutionPageResponse(value: unknown): value is ReportingExecutionPageResponse {
  return isRecord(value) && (Array.isArray(value["columns"]) && value["columns"].every(item16 => isReportingExecutionColumnDefinition(item16))) && (typeof value["commandTimeoutSeconds"] === 'number' && Number.isSafeInteger(value["commandTimeoutSeconds"])) && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["definitionKey"] === 'string') && (typeof value["definitionName"] === 'string') && (typeof value["executedAtUtc"] === 'string') && (typeof value["hasMore"] === 'boolean') && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["queryPortKey"] === 'string') && (Array.isArray(value["rows"]) && value["rows"].every(item13 => isReportingExecutionRow(item13))) && ((value["totalRows"] === null) || (typeof value["totalRows"] === 'number' && Number.isSafeInteger(value["totalRows"]))) && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readReportingExecutionParameterValue(value: unknown): ReportingExecutionParameterValue {
  if (!(isReportingExecutionParameterValue(value))) {
    throw new Error('client.invalid_reporting_execution_parameter_value');
  }
  return value;
}

function isReportingExecutionParameterValue(value: unknown): value is ReportingExecutionParameterValue {
  return isRecord(value) && (typeof value["parameterKey"] === 'string') && ((value["value"] === null) || (typeof value["value"] === 'string'));
}

export function readReportingExecutionRow(value: unknown): ReportingExecutionRow {
  if (!(isReportingExecutionRow(value))) {
    throw new Error('client.invalid_reporting_execution_row');
  }
  return value;
}

function isReportingExecutionRow(value: unknown): value is ReportingExecutionRow {
  return isRecord(value) && (isRecord(value["values"]));
}

export function readReportingExportTaskDetailResponse(value: unknown): ReportingExportTaskDetailResponse {
  const normalizedValue = normalizeReportingExportTaskDetailResponseIntegerJson(value);
  if (!(isReportingExportTaskDetailResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_export_task_detail_response');
  }
  return normalizedValue;
}

function isReportingExportTaskDetailResponse(value: unknown): value is ReportingExportTaskDetailResponse {
  return isRecord(value) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["definitionKey"] === 'string') && (typeof value["definitionName"] === 'string') && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["errorMessage"] === null) || (typeof value["errorMessage"] === 'string')) && (typeof value["formatKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["outputFileId"] === null) || (typeof value["outputFileId"] === 'string' && guidPattern.test(value["outputFileId"]))) && ((value["outputFileName"] === null) || (typeof value["outputFileName"] === 'string')) && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isReportingExecutionParameterValue(item19))) && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && (typeof value["rowCount"] === 'number' && Number.isSafeInteger(value["rowCount"])) && (typeof value["statusKey"] === 'string') && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readReportingExportTaskResponse(value: unknown): ReportingExportTaskResponse {
  const normalizedValue = normalizeReportingExportTaskResponseIntegerJson(value);
  if (!(isReportingExportTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_export_task_response');
  }
  return normalizedValue;
}

function isReportingExportTaskResponse(value: unknown): value is ReportingExportTaskResponse {
  return isRecord(value) && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["definitionKey"] === 'string') && (typeof value["definitionName"] === 'string') && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && ((value["errorMessage"] === null) || (typeof value["errorMessage"] === 'string')) && (typeof value["formatKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["outputFileName"] === null) || (typeof value["outputFileName"] === 'string')) && (typeof value["requestedByUserId"] === 'string' && guidPattern.test(value["requestedByUserId"])) && (typeof value["rowCount"] === 'number' && Number.isSafeInteger(value["rowCount"])) && (typeof value["statusKey"] === 'string') && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
}

export function readReportingGroupResponse(value: unknown): ReportingGroupResponse {
  const normalizedValue = normalizeReportingGroupResponseIntegerJson(value);
  if (!(isReportingGroupResponse(normalizedValue))) {
    throw new Error('client.invalid_reporting_group_response');
  }
  return normalizedValue;
}

function isReportingGroupResponse(value: unknown): value is ReportingGroupResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readReportingParameterSchemaEntry(value: unknown): ReportingParameterSchemaEntry {
  if (!(isReportingParameterSchemaEntry(value))) {
    throw new Error('client.invalid_reporting_parameter_schema_entry');
  }
  return value;
}

function isReportingParameterSchemaEntry(value: unknown): value is ReportingParameterSchemaEntry {
  return isRecord(value) && (typeof value["dataTypeKey"] === 'string') && ((value["defaultValue"] === null) || (typeof value["defaultValue"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["isRequired"] === 'boolean') && (typeof value["parameterKey"] === 'string');
}

export function readReportingQueryPortDefinition(value: unknown): ReportingQueryPortDefinition {
  const normalizedValue = normalizeReportingQueryPortDefinitionIntegerJson(value);
  if (!(isReportingQueryPortDefinition(normalizedValue))) {
    throw new Error('client.invalid_reporting_query_port_definition');
  }
  return normalizedValue;
}

function isReportingQueryPortDefinition(value: unknown): value is ReportingQueryPortDefinition {
  return isRecord(value) && (typeof value["description"] === 'string') && (typeof value["displayName"] === 'string') && (Array.isArray(value["parameters"]) && value["parameters"].every(item19 => isReportingQueryPortParameterDefinition(item19))) && (typeof value["queryPortKey"] === 'string') && (Array.isArray(value["supportedProviderKeys"]) && value["supportedProviderKeys"].every(item30 => typeof item30 === 'string'));
}

export function readReportingQueryPortParameterDefinition(value: unknown): ReportingQueryPortParameterDefinition {
  const normalizedValue = normalizeReportingQueryPortParameterDefinitionIntegerJson(value);
  if (!(isReportingQueryPortParameterDefinition(normalizedValue))) {
    throw new Error('client.invalid_reporting_query_port_parameter_definition');
  }
  return normalizedValue;
}

function isReportingQueryPortParameterDefinition(value: unknown): value is ReportingQueryPortParameterDefinition {
  return isRecord(value) && (typeof value["dataTypeKey"] === 'string') && ((value["defaultValue"] === null) || (typeof value["defaultValue"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["isRequired"] === 'boolean') && ((value["maximum"] === null) || (typeof value["maximum"] === 'number' && Number.isSafeInteger(value["maximum"]))) && ((value["minimum"] === null) || (typeof value["minimum"] === 'number' && Number.isSafeInteger(value["minimum"]))) && (typeof value["parameterKey"] === 'string');
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
  const normalizedValue = normalizeRestoreDiagnosticPolicyRequestIntegerJson(value);
  if (!(isRestoreDiagnosticPolicyRequest(normalizedValue))) {
    throw new Error('client.invalid_restore_diagnostic_policy_request');
  }
  return normalizedValue;
}

function isRestoreDiagnosticPolicyRequest(value: unknown): value is RestoreDiagnosticPolicyRequest {
  return isRecord(value) && (typeof value["configEntryVersion"] === 'number' && Number.isSafeInteger(value["configEntryVersion"]));
}

export function readRestoreHostDocumentItemRequest(value: unknown): RestoreHostDocumentItemRequest {
  const normalizedValue = normalizeRestoreHostDocumentItemRequestIntegerJson(value);
  if (!(isRestoreHostDocumentItemRequest(normalizedValue))) {
    throw new Error('client.invalid_restore_host_document_item_request');
  }
  return normalizedValue;
}

function isRestoreHostDocumentItemRequest(value: unknown): value is RestoreHostDocumentItemRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readResumeWorkflowInstanceRequest(value: unknown): ResumeWorkflowInstanceRequest {
  const normalizedValue = normalizeResumeWorkflowInstanceRequestIntegerJson(value);
  if (!(isResumeWorkflowInstanceRequest(normalizedValue))) {
    throw new Error('client.invalid_resume_workflow_instance_request');
  }
  return normalizedValue;
}

function isResumeWorkflowInstanceRequest(value: unknown): value is ResumeWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && ((value["reason"] === null) || (typeof value["reason"] === 'string'));
}

export function readRetractHostReleaseNoteRequest(value: unknown): RetractHostReleaseNoteRequest {
  const normalizedValue = normalizeRetractHostReleaseNoteRequestIntegerJson(value);
  if (!(isRetractHostReleaseNoteRequest(normalizedValue))) {
    throw new Error('client.invalid_retract_host_release_note_request');
  }
  return normalizedValue;
}

function isRetractHostReleaseNoteRequest(value: unknown): value is RetractHostReleaseNoteRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readRetryDataApprovalRequestBody(value: unknown): RetryDataApprovalRequestBody {
  const normalizedValue = normalizeRetryDataApprovalRequestBodyIntegerJson(value);
  if (!(isRetryDataApprovalRequestBody(normalizedValue))) {
    throw new Error('client.invalid_retry_data_approval_request_body');
  }
  return normalizedValue;
}

function isRetryDataApprovalRequestBody(value: unknown): value is RetryDataApprovalRequestBody {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readRetryNotificationDeliveryRequest(value: unknown): RetryNotificationDeliveryRequest {
  const normalizedValue = normalizeRetryNotificationDeliveryRequestIntegerJson(value);
  if (!(isRetryNotificationDeliveryRequest(normalizedValue))) {
    throw new Error('client.invalid_retry_notification_delivery_request');
  }
  return normalizedValue;
}

function isRetryNotificationDeliveryRequest(value: unknown): value is RetryNotificationDeliveryRequest {
  return isRecord(value) && (typeof value["reason"] === 'string') && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"]));
}

export function readRetryWorkflowRecoveryTaskRequest(value: unknown): RetryWorkflowRecoveryTaskRequest {
  const normalizedValue = normalizeRetryWorkflowRecoveryTaskRequestIntegerJson(value);
  if (!(isRetryWorkflowRecoveryTaskRequest(normalizedValue))) {
    throw new Error('client.invalid_retry_workflow_recovery_task_request');
  }
  return normalizedValue;
}

function isRetryWorkflowRecoveryTaskRequest(value: unknown): value is RetryWorkflowRecoveryTaskRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["idempotencyKey"] === 'string') && (typeof value["reason"] === 'string');
}

export function readReturnWorkflowTodoRequest(value: unknown): ReturnWorkflowTodoRequest {
  const normalizedValue = normalizeReturnWorkflowTodoRequestIntegerJson(value);
  if (!(isReturnWorkflowTodoRequest(normalizedValue))) {
    throw new Error('client.invalid_return_workflow_todo_request');
  }
  return normalizedValue;
}

function isReturnWorkflowTodoRequest(value: unknown): value is ReturnWorkflowTodoRequest {
  return isRecord(value) && (typeof value["comment"] === 'string') && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (isJsonElement(value["fieldPatch"])) && (typeof value["idempotencyKey"] === 'string') && (typeof value["targetStepId"] === 'string' && guidPattern.test(value["targetStepId"]));
}

export function readRevealHostUserProfileFieldsRequest(value: unknown): RevealHostUserProfileFieldsRequest {
  if (!(isRevealHostUserProfileFieldsRequest(value))) {
    throw new Error('client.invalid_reveal_host_user_profile_fields_request');
  }
  return value;
}

function isRevealHostUserProfileFieldsRequest(value: unknown): value is RevealHostUserProfileFieldsRequest {
  return isRecord(value) && (Array.isArray(value["fieldKeys"]) && value["fieldKeys"].every(item18 => typeof item18 === 'string'));
}

export function readRevealHostUserProfileFieldsResponse(value: unknown): RevealHostUserProfileFieldsResponse {
  if (!(isRevealHostUserProfileFieldsResponse(value))) {
    throw new Error('client.invalid_reveal_host_user_profile_fields_response');
  }
  return value;
}

function isRevealHostUserProfileFieldsResponse(value: unknown): value is RevealHostUserProfileFieldsResponse {
  return isRecord(value) && (isRecord(value["values"]));
}

export function readRevokeAiAgentDelegationRequest(value: unknown): RevokeAiAgentDelegationRequest {
  const normalizedValue = normalizeRevokeAiAgentDelegationRequestIntegerJson(value);
  if (!(isRevokeAiAgentDelegationRequest(normalizedValue))) {
    throw new Error('client.invalid_revoke_ai_agent_delegation_request');
  }
  return normalizedValue;
}

function isRevokeAiAgentDelegationRequest(value: unknown): value is RevokeAiAgentDelegationRequest {
  return isRecord(value) && (typeof value["expectedVersion"] === 'number' && Number.isSafeInteger(value["expectedVersion"]));
}

export function readRevokeAllHostUserSessionsResponse(value: unknown): RevokeAllHostUserSessionsResponse {
  const normalizedValue = normalizeRevokeAllHostUserSessionsResponseIntegerJson(value);
  if (!(isRevokeAllHostUserSessionsResponse(normalizedValue))) {
    throw new Error('client.invalid_revoke_all_host_user_sessions_response');
  }
  return normalizedValue;
}

function isRevokeAllHostUserSessionsResponse(value: unknown): value is RevokeAllHostUserSessionsResponse {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["revokedSessionCount"] === 'number' && Number.isSafeInteger(value["revokedSessionCount"])) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
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

export function readRollbackHostDocumentVersionRequest(value: unknown): RollbackHostDocumentVersionRequest {
  const normalizedValue = normalizeRollbackHostDocumentVersionRequestIntegerJson(value);
  if (!(isRollbackHostDocumentVersionRequest(normalizedValue))) {
    throw new Error('client.invalid_rollback_host_document_version_request');
  }
  return normalizedValue;
}

function isRollbackHostDocumentVersionRequest(value: unknown): value is RollbackHostDocumentVersionRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readSelfServiceProfileResponse(value: unknown): SelfServiceProfileResponse {
  const normalizedValue = normalizeSelfServiceProfileResponseIntegerJson(value);
  if (!(isSelfServiceProfileResponse(normalizedValue))) {
    throw new Error('client.invalid_self_service_profile_response');
  }
  return normalizedValue;
}

function isSelfServiceProfileResponse(value: unknown): value is SelfServiceProfileResponse {
  return isRecord(value) && (typeof value["accountType"] === 'string') && ((value["avatarFileId"] === null) || (typeof value["avatarFileId"] === 'string' && guidPattern.test(value["avatarFileId"]))) && (typeof value["displayName"] === 'string') && ((value["profile"] === null) || (isHostUserProfileResponse(value["profile"]))) && (Array.isArray(value["readableFieldKeys"]) && value["readableFieldKeys"].every(item26 => typeof item26 === 'string')) && ((value["signatureFileId"] === null) || (typeof value["signatureFileId"] === 'string' && guidPattern.test(value["signatureFileId"]))) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string') && (typeof value["userVersion"] === 'number' && Number.isSafeInteger(value["userVersion"])) && (Array.isArray(value["writableFieldKeys"]) && value["writableFieldKeys"].every(item26 => typeof item26 === 'string'));
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
  const normalizedValue = normalizeSerialNumberPreviewResponseIntegerJson(value);
  if (!(isSerialNumberPreviewResponse(normalizedValue))) {
    throw new Error('client.invalid_serial_number_preview_response');
  }
  return normalizedValue;
}

function isSerialNumberPreviewResponse(value: unknown): value is SerialNumberPreviewResponse {
  return isRecord(value) && (typeof value["resetBucket"] === 'string') && (typeof value["sequenceValue"] === 'number' && Number.isSafeInteger(value["sequenceValue"])) && (typeof value["value"] === 'string');
}

export function readSerialNumberResetInterval(value: unknown): SerialNumberResetInterval {
  if (!(isSerialNumberResetInterval(value))) {
    throw new Error('client.invalid_serial_number_reset_interval');
  }
  return value;
}

function isSerialNumberResetInterval(value: unknown): value is SerialNumberResetInterval {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

export function readSerialNumberRuleResponse(value: unknown): SerialNumberRuleResponse {
  const normalizedValue = normalizeSerialNumberRuleResponseIntegerJson(value);
  if (!(isSerialNumberRuleResponse(normalizedValue))) {
    throw new Error('client.invalid_serial_number_rule_response');
  }
  return normalizedValue;
}

function isSerialNumberRuleResponse(value: unknown): value is SerialNumberRuleResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"])) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maximumValue"] === 'number' && Number.isSafeInteger(value["maximumValue"])) && (typeof value["minimumValue"] === 'number' && Number.isSafeInteger(value["minimumValue"])) && (typeof value["pattern"] === 'string') && (isSerialNumberResetInterval(value["resetInterval"])) && (typeof value["ruleKey"] === 'string') && (isSerialNumberRuleScope(value["scope"])) && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedByUserId"] === null) || (typeof value["updatedByUserId"] === 'string' && guidPattern.test(value["updatedByUserId"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readSerialNumberRuleScope(value: unknown): SerialNumberRuleScope {
  if (!(isSerialNumberRuleScope(value))) {
    throw new Error('client.invalid_serial_number_rule_scope');
  }
  return value;
}

function isSerialNumberRuleScope(value: unknown): value is SerialNumberRuleScope {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

export function readSerialRuleDisableApprovalPreviewResponse(value: unknown): SerialRuleDisableApprovalPreviewResponse {
  const normalizedValue = normalizeSerialRuleDisableApprovalPreviewResponseIntegerJson(value);
  if (!(isSerialRuleDisableApprovalPreviewResponse(normalizedValue))) {
    throw new Error('client.invalid_serial_rule_disable_approval_preview_response');
  }
  return normalizedValue;
}

function isSerialRuleDisableApprovalPreviewResponse(value: unknown): value is SerialRuleDisableApprovalPreviewResponse {
  return isRecord(value) && (typeof value["afterSnapshotJson"] === 'string') && (typeof value["beforeSnapshotJson"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["ruleId"] === 'string' && guidPattern.test(value["ruleId"])) && (typeof value["ruleKey"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readSerialRuleDisableApprovalSubmissionResponse(value: unknown): SerialRuleDisableApprovalSubmissionResponse {
  const normalizedValue = normalizeSerialRuleDisableApprovalSubmissionResponseIntegerJson(value);
  if (!(isSerialRuleDisableApprovalSubmissionResponse(normalizedValue))) {
    throw new Error('client.invalid_serial_rule_disable_approval_submission_response');
  }
  return normalizedValue;
}

function isSerialRuleDisableApprovalSubmissionResponse(value: unknown): value is SerialRuleDisableApprovalSubmissionResponse {
  return isRecord(value) && (typeof value["afterSnapshotJson"] === 'string') && ((value["beforeSnapshotJson"] === null) || (typeof value["beforeSnapshotJson"] === 'string')) && (typeof value["requestId"] === 'string' && guidPattern.test(value["requestId"])) && (typeof value["requestVersion"] === 'number' && Number.isSafeInteger(value["requestVersion"])) && (typeof value["statusKey"] === 'string') && (typeof value["workflowDefinitionVersionId"] === 'string' && guidPattern.test(value["workflowDefinitionVersionId"]));
}

export function readSerialRuleFieldChange(value: unknown): SerialRuleFieldChange {
  if (!(isSerialRuleFieldChange(value))) {
    throw new Error('client.invalid_serial_rule_field_change');
  }
  return value;
}

function isSerialRuleFieldChange(value: unknown): value is SerialRuleFieldChange {
  return isRecord(value) && ((value["afterValue"] === null) || (typeof value["afterValue"] === 'string')) && ((value["beforeValue"] === null) || (typeof value["beforeValue"] === 'string')) && (typeof value["changed"] === 'boolean') && (typeof value["fieldKey"] === 'string');
}

export function readSerialRuleUpdateApprovalPreviewResponse(value: unknown): SerialRuleUpdateApprovalPreviewResponse {
  if (!(isSerialRuleUpdateApprovalPreviewResponse(value))) {
    throw new Error('client.invalid_serial_rule_update_approval_preview_response');
  }
  return value;
}

function isSerialRuleUpdateApprovalPreviewResponse(value: unknown): value is SerialRuleUpdateApprovalPreviewResponse {
  return isRecord(value) && (typeof value["afterSnapshotJson"] === 'string') && (typeof value["beforeSnapshotJson"] === 'string') && (Array.isArray(value["changes"]) && value["changes"].every(item16 => isSerialRuleFieldChange(item16))) && (typeof value["displayName"] === 'string') && (typeof value["ruleId"] === 'string' && guidPattern.test(value["ruleId"])) && (typeof value["ruleKey"] === 'string');
}

export function readSerialRuleUpdateApprovalSubmissionResponse(value: unknown): SerialRuleUpdateApprovalSubmissionResponse {
  const normalizedValue = normalizeSerialRuleUpdateApprovalSubmissionResponseIntegerJson(value);
  if (!(isSerialRuleUpdateApprovalSubmissionResponse(normalizedValue))) {
    throw new Error('client.invalid_serial_rule_update_approval_submission_response');
  }
  return normalizedValue;
}

function isSerialRuleUpdateApprovalSubmissionResponse(value: unknown): value is SerialRuleUpdateApprovalSubmissionResponse {
  return isRecord(value) && (typeof value["afterSnapshotJson"] === 'string') && ((value["beforeSnapshotJson"] === null) || (typeof value["beforeSnapshotJson"] === 'string')) && (Array.isArray(value["changes"]) && value["changes"].every(item16 => isSerialRuleFieldChange(item16))) && (typeof value["requestId"] === 'string' && guidPattern.test(value["requestId"])) && (typeof value["requestVersion"] === 'number' && Number.isSafeInteger(value["requestVersion"])) && (typeof value["statusKey"] === 'string') && (typeof value["workflowDefinitionVersionId"] === 'string' && guidPattern.test(value["workflowDefinitionVersionId"]));
}

export function readServerInstanceCatalogEntry(value: unknown): ServerInstanceCatalogEntry {
  if (!(isServerInstanceCatalogEntry(value))) {
    throw new Error('client.invalid_server_instance_catalog_entry');
  }
  return value;
}

function isServerInstanceCatalogEntry(value: unknown): value is ServerInstanceCatalogEntry {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["hostRole"] === 'string') && (typeof value["instanceKey"] === 'string') && (typeof value["isCurrent"] === 'boolean') && (typeof value["runtimeQueryability"] === 'string');
}

export function readServerRuntimeMetric(value: unknown): ServerRuntimeMetric {
  const normalizedValue = normalizeServerRuntimeMetricIntegerJson(value);
  if (!(isServerRuntimeMetric(normalizedValue))) {
    throw new Error('client.invalid_server_runtime_metric');
  }
  return normalizedValue;
}

function isServerRuntimeMetric(value: unknown): value is ServerRuntimeMetric {
  return isRecord(value) && (typeof value["availability"] === 'string') && ((value["doubleValue"] === null) || (typeof value["doubleValue"] === 'number' && Number.isFinite(value["doubleValue"])) || (typeof value["doubleValue"] === 'string')) && (typeof value["key"] === 'string') && (typeof value["label"] === 'string') && ((value["longValue"] === null) || (typeof value["longValue"] === 'number' && Number.isSafeInteger(value["longValue"]))) && ((value["unavailableReason"] === null) || (typeof value["unavailableReason"] === 'string')) && ((value["unit"] === null) || (typeof value["unit"] === 'string'));
}

export function readServerRuntimeSnapshot(value: unknown): ServerRuntimeSnapshot {
  const normalizedValue = normalizeServerRuntimeSnapshotIntegerJson(value);
  if (!(isServerRuntimeSnapshot(normalizedValue))) {
    throw new Error('client.invalid_server_runtime_snapshot');
  }
  return normalizedValue;
}

function isServerRuntimeSnapshot(value: unknown): value is ServerRuntimeSnapshot {
  return isRecord(value) && (typeof value["applicationVersion"] === 'string') && (typeof value["capturedAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["frameworkDescription"] === 'string') && (typeof value["hostRole"] === 'string') && (typeof value["instanceKey"] === 'string') && (typeof value["machineName"] === 'string') && (Array.isArray(value["metrics"]) && value["metrics"].every(item16 => isServerRuntimeMetric(item16))) && (typeof value["operatingSystemDescription"] === 'string') && (typeof value["processArchitecture"] === 'string') && (typeof value["processId"] === 'number' && Number.isSafeInteger(value["processId"])) && (typeof value["processStartedAtUtc"] === 'string') && (typeof value["uptimeSeconds"] === 'number' && Number.isSafeInteger(value["uptimeSeconds"]));
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
  const normalizedValue = normalizeSetNotificationProviderProfileEnabledRequestIntegerJson(value);
  if (!(isSetNotificationProviderProfileEnabledRequest(normalizedValue))) {
    throw new Error('client.invalid_set_notification_provider_profile_enabled_request');
  }
  return normalizedValue;
}

function isSetNotificationProviderProfileEnabledRequest(value: unknown): value is SetNotificationProviderProfileEnabledRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readSetPersonalScheduleStatusRequest(value: unknown): SetPersonalScheduleStatusRequest {
  const normalizedValue = normalizeSetPersonalScheduleStatusRequestIntegerJson(value);
  if (!(isSetPersonalScheduleStatusRequest(normalizedValue))) {
    throw new Error('client.invalid_set_personal_schedule_status_request');
  }
  return normalizedValue;
}

function isSetPersonalScheduleStatusRequest(value: unknown): value is SetPersonalScheduleStatusRequest {
  return isRecord(value) && (typeof value["status"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readSetWorkflowDefinitionStatusRequest(value: unknown): SetWorkflowDefinitionStatusRequest {
  const normalizedValue = normalizeSetWorkflowDefinitionStatusRequestIntegerJson(value);
  if (!(isSetWorkflowDefinitionStatusRequest(normalizedValue))) {
    throw new Error('client.invalid_set_workflow_definition_status_request');
  }
  return normalizedValue;
}

function isSetWorkflowDefinitionStatusRequest(value: unknown): value is SetWorkflowDefinitionStatusRequest {
  return isRecord(value) && (typeof value["expectedVersion"] === 'number' && Number.isSafeInteger(value["expectedVersion"])) && (typeof value["statusKey"] === 'string');
}

export function readSetWorkflowFormStatusRequest(value: unknown): SetWorkflowFormStatusRequest {
  const normalizedValue = normalizeSetWorkflowFormStatusRequestIntegerJson(value);
  if (!(isSetWorkflowFormStatusRequest(normalizedValue))) {
    throw new Error('client.invalid_set_workflow_form_status_request');
  }
  return normalizedValue;
}

function isSetWorkflowFormStatusRequest(value: unknown): value is SetWorkflowFormStatusRequest {
  return isRecord(value) && (typeof value["expectedVersion"] === 'number' && Number.isSafeInteger(value["expectedVersion"])) && (typeof value["statusKey"] === 'string');
}

export function readStartWorkflowInstanceRequest(value: unknown): StartWorkflowInstanceRequest {
  if (!(isStartWorkflowInstanceRequest(value))) {
    throw new Error('client.invalid_start_workflow_instance_request');
  }
  return value;
}

function isStartWorkflowInstanceRequest(value: unknown): value is StartWorkflowInstanceRequest {
  return isRecord(value) && (typeof value["businessId"] === 'string') && (value["businessTitle"] === undefined || ((value["businessTitle"] === null) || (typeof value["businessTitle"] === 'string'))) && (typeof value["businessType"] === 'string') && (typeof value["definitionVersionId"] === 'string' && guidPattern.test(value["definitionVersionId"])) && (typeof value["idempotencyKey"] === 'string') && (isJsonElement(value["initialValues"]));
}

export function readStaticImportRowPreviewResult(value: unknown): StaticImportRowPreviewResult {
  const normalizedValue = normalizeStaticImportRowPreviewResultIntegerJson(value);
  if (!(isStaticImportRowPreviewResult(normalizedValue))) {
    throw new Error('client.invalid_static_import_row_preview_result');
  }
  return normalizedValue;
}

function isStaticImportRowPreviewResult(value: unknown): value is StaticImportRowPreviewResult {
  return isRecord(value) && ((value["errorCode"] === null) || (typeof value["errorCode"] === 'string')) && (typeof value["isValid"] === 'boolean') && (typeof value["lineNumber"] === 'number' && Number.isSafeInteger(value["lineNumber"])) && ((value["message"] === null) || (typeof value["message"] === 'string'));
}

export function readStaticImportSchemaDefinition(value: unknown): StaticImportSchemaDefinition {
  if (!(isStaticImportSchemaDefinition(value))) {
    throw new Error('client.invalid_static_import_schema_definition');
  }
  return value;
}

function isStaticImportSchemaDefinition(value: unknown): value is StaticImportSchemaDefinition {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (typeof value["requiredPermission"] === 'string') && (typeof value["schemaKey"] === 'string') && (typeof value["scopeKey"] === 'string') && (Array.isArray(value["worksheets"]) && value["worksheets"].every(item19 => isStaticImportWorksheetDefinition(item19)));
}

export function readStaticImportWorksheetDefinition(value: unknown): StaticImportWorksheetDefinition {
  if (!(isStaticImportWorksheetDefinition(value))) {
    throw new Error('client.invalid_static_import_worksheet_definition');
  }
  return value;
}

function isStaticImportWorksheetDefinition(value: unknown): value is StaticImportWorksheetDefinition {
  return isRecord(value) && (typeof value["displayName"] === 'string') && (Array.isArray(value["headerColumns"]) && value["headerColumns"].every(item22 => typeof item22 === 'string')) && (typeof value["worksheetKey"] === 'string');
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

export function readStreamAiChatMessageRequest(value: unknown): StreamAiChatMessageRequest {
  if (!(isStreamAiChatMessageRequest(value))) {
    throw new Error('client.invalid_stream_ai_chat_message_request');
  }
  return value;
}

function isStreamAiChatMessageRequest(value: unknown): value is StreamAiChatMessageRequest {
  return isRecord(value) && (typeof value["content"] === 'string');
}

export function readSubmitSerialRuleDisableApprovalRequest(value: unknown): SubmitSerialRuleDisableApprovalRequest {
  const normalizedValue = normalizeSubmitSerialRuleDisableApprovalRequestIntegerJson(value);
  if (!(isSubmitSerialRuleDisableApprovalRequest(normalizedValue))) {
    throw new Error('client.invalid_submit_serial_rule_disable_approval_request');
  }
  return normalizedValue;
}

function isSubmitSerialRuleDisableApprovalRequest(value: unknown): value is SubmitSerialRuleDisableApprovalRequest {
  return isRecord(value) && (typeof value["idempotencyKey"] === 'string') && (isChangeSerialNumberRuleStatusRequest(value["statusChange"]));
}

export function readSubmitSerialRuleUpdateApprovalRequest(value: unknown): SubmitSerialRuleUpdateApprovalRequest {
  const normalizedValue = normalizeSubmitSerialRuleUpdateApprovalRequestIntegerJson(value);
  if (!(isSubmitSerialRuleUpdateApprovalRequest(normalizedValue))) {
    throw new Error('client.invalid_submit_serial_rule_update_approval_request');
  }
  return normalizedValue;
}

function isSubmitSerialRuleUpdateApprovalRequest(value: unknown): value is SubmitSerialRuleUpdateApprovalRequest {
  return isRecord(value) && (typeof value["idempotencyKey"] === 'string') && (isUpdateSerialNumberRuleRequest(value["update"]));
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

export function readTenantBrandingResponse(value: unknown): TenantBrandingResponse {
  const normalizedValue = normalizeTenantBrandingResponseIntegerJson(value);
  if (!(isTenantBrandingResponse(normalizedValue))) {
    throw new Error('client.invalid_tenant_branding_response');
  }
  return normalizedValue;
}

function isTenantBrandingResponse(value: unknown): value is TenantBrandingResponse {
  return isRecord(value) && ((value["contactAddress"] === null) || (typeof value["contactAddress"] === 'string')) && ((value["contactEmail"] === null) || (typeof value["contactEmail"] === 'string')) && ((value["contactPhone"] === null) || (typeof value["contactPhone"] === 'string')) && ((value["copyright"] === null) || (typeof value["copyright"] === 'string')) && ((value["logoFileId"] === null) || (typeof value["logoFileId"] === 'string' && guidPattern.test(value["logoFileId"]))) && ((value["systemTitle"] === null) || (typeof value["systemTitle"] === 'string')) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readTenantPackageSummary(value: unknown): TenantPackageSummary {
  const normalizedValue = normalizeTenantPackageSummaryIntegerJson(value);
  if (!(isTenantPackageSummary(normalizedValue))) {
    throw new Error('client.invalid_tenant_package_summary');
  }
  return normalizedValue;
}

function isTenantPackageSummary(value: unknown): value is TenantPackageSummary {
  return isRecord(value) && (value["assignedTenantCount"] === undefined || (typeof value["assignedTenantCount"] === 'number' && Number.isSafeInteger(value["assignedTenantCount"]))) && (typeof value["code"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readTenantRuntimeBrandingResponse(value: unknown): TenantRuntimeBrandingResponse {
  if (!(isTenantRuntimeBrandingResponse(value))) {
    throw new Error('client.invalid_tenant_runtime_branding_response');
  }
  return value;
}

function isTenantRuntimeBrandingResponse(value: unknown): value is TenantRuntimeBrandingResponse {
  return isRecord(value) && ((value["contactAddress"] === null) || (typeof value["contactAddress"] === 'string')) && ((value["contactEmail"] === null) || (typeof value["contactEmail"] === 'string')) && ((value["contactPhone"] === null) || (typeof value["contactPhone"] === 'string')) && ((value["copyright"] === null) || (typeof value["copyright"] === 'string')) && (typeof value["hasLogo"] === 'boolean') && ((value["systemTitle"] === null) || (typeof value["systemTitle"] === 'string'));
}

export function readTenantSummary(value: unknown): TenantSummary {
  const normalizedValue = normalizeTenantSummaryIntegerJson(value);
  if (!(isTenantSummary(normalizedValue))) {
    throw new Error('client.invalid_tenant_summary');
  }
  return normalizedValue;
}

function isTenantSummary(value: unknown): value is TenantSummary {
  return isRecord(value) && (value["defaultLocale"] === undefined || (typeof value["defaultLocale"] === 'string')) && (typeof value["domain"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["identifier"] === 'string') && (typeof value["isActive"] === 'boolean') && (value["lifecycleStatus"] === undefined || (typeof value["lifecycleStatus"] === 'string')) && (typeof value["name"] === 'string') && (value["ownerUserId"] === undefined || ((value["ownerUserId"] === null) || (typeof value["ownerUserId"] === 'string' && guidPattern.test(value["ownerUserId"])))) && (value["provisioningStatus"] === undefined || (typeof value["provisioningStatus"] === 'string')) && (value["provisioningStep"] === undefined || ((value["provisioningStep"] === null) || (typeof value["provisioningStep"] === 'string'))) && (value["tenantPackageCode"] === undefined || ((value["tenantPackageCode"] === null) || (typeof value["tenantPackageCode"] === 'string'))) && (value["tenantPackageId"] === undefined || ((value["tenantPackageId"] === null) || (typeof value["tenantPackageId"] === 'string' && guidPattern.test(value["tenantPackageId"])))) && (value["tenantPackageName"] === undefined || ((value["tenantPackageName"] === null) || (typeof value["tenantPackageName"] === 'string'))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readTestAiModelConfigResult(value: unknown): TestAiModelConfigResult {
  if (!(isTestAiModelConfigResult(value))) {
    throw new Error('client.invalid_test_ai_model_config_result');
  }
  return value;
}

function isTestAiModelConfigResult(value: unknown): value is TestAiModelConfigResult {
  return isRecord(value) && (typeof value["message"] === 'string') && (typeof value["succeeded"] === 'boolean');
}

export function readTestAiModelEmbeddingRequest(value: unknown): TestAiModelEmbeddingRequest {
  if (!(isTestAiModelEmbeddingRequest(value))) {
    throw new Error('client.invalid_test_ai_model_embedding_request');
  }
  return value;
}

function isTestAiModelEmbeddingRequest(value: unknown): value is TestAiModelEmbeddingRequest {
  return isRecord(value) && ((value["batchInputs"] === null) || (Array.isArray(value["batchInputs"]) && value["batchInputs"].every(item20 => typeof item20 === 'string'))) && ((value["input"] === null) || (typeof value["input"] === 'string'));
}

export function readTestAiModelEmbeddingResult(value: unknown): TestAiModelEmbeddingResult {
  const normalizedValue = normalizeTestAiModelEmbeddingResultIntegerJson(value);
  if (!(isTestAiModelEmbeddingResult(normalizedValue))) {
    throw new Error('client.invalid_test_ai_model_embedding_result');
  }
  return normalizedValue;
}

function isTestAiModelEmbeddingResult(value: unknown): value is TestAiModelEmbeddingResult {
  return isRecord(value) && (typeof value["dimensions"] === 'number' && Number.isSafeInteger(value["dimensions"])) && (typeof value["inputCount"] === 'number' && Number.isSafeInteger(value["inputCount"])) && ((value["inputTokens"] === null) || (typeof value["inputTokens"] === 'number' && Number.isSafeInteger(value["inputTokens"]))) && (typeof value["message"] === 'string') && (typeof value["succeeded"] === 'boolean');
}

export function readTestK3CloudConnectionConfigResult(value: unknown): TestK3CloudConnectionConfigResult {
  if (!(isTestK3CloudConnectionConfigResult(value))) {
    throw new Error('client.invalid_test_k3_cloud_connection_config_result');
  }
  return value;
}

function isTestK3CloudConnectionConfigResult(value: unknown): value is TestK3CloudConnectionConfigResult {
  return isRecord(value) && (typeof value["message"] === 'string') && (typeof value["succeeded"] === 'boolean');
}

export function readTestOcrProviderConfigResult(value: unknown): TestOcrProviderConfigResult {
  if (!(isTestOcrProviderConfigResult(value))) {
    throw new Error('client.invalid_test_ocr_provider_config_result');
  }
  return value;
}

function isTestOcrProviderConfigResult(value: unknown): value is TestOcrProviderConfigResult {
  return isRecord(value) && (typeof value["message"] === 'string') && (typeof value["succeeded"] === 'boolean');
}

export function readTestReportingDataSourceResult(value: unknown): TestReportingDataSourceResult {
  if (!(isTestReportingDataSourceResult(value))) {
    throw new Error('client.invalid_test_reporting_data_source_result');
  }
  return value;
}

function isTestReportingDataSourceResult(value: unknown): value is TestReportingDataSourceResult {
  return isRecord(value) && (typeof value["message"] === 'string') && (typeof value["succeeded"] === 'boolean');
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

export function readUpdateAdministrativeRegionRequest(value: unknown): UpdateAdministrativeRegionRequest {
  const normalizedValue = normalizeUpdateAdministrativeRegionRequestIntegerJson(value);
  if (!(isUpdateAdministrativeRegionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_administrative_region_request');
  }
  return normalizedValue;
}

function isUpdateAdministrativeRegionRequest(value: unknown): value is UpdateAdministrativeRegionRequest {
  return isRecord(value) && ((value["cityCode"] === null) || (typeof value["cityCode"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && ((value["latitude"] === null) || (typeof value["latitude"] === 'number' && Number.isFinite(value["latitude"])) || (typeof value["latitude"] === 'string')) && (typeof value["level"] === 'number' && Number.isSafeInteger(value["level"])) && ((value["longitude"] === null) || (typeof value["longitude"] === 'number' && Number.isFinite(value["longitude"])) || (typeof value["longitude"] === 'string')) && ((value["mergerName"] === null) || (typeof value["mergerName"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && ((value["pinYin"] === null) || (typeof value["pinYin"] === 'string')) && ((value["regionType"] === null) || (typeof value["regionType"] === 'string')) && ((value["remark"] === null) || (typeof value["remark"] === 'string')) && ((value["shortName"] === null) || (typeof value["shortName"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && ((value["zipCode"] === null) || (typeof value["zipCode"] === 'string'));
}

export function readUpdateAiChatSessionRequest(value: unknown): UpdateAiChatSessionRequest {
  const normalizedValue = normalizeUpdateAiChatSessionRequestIntegerJson(value);
  if (!(isUpdateAiChatSessionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_ai_chat_session_request');
  }
  return normalizedValue;
}

function isUpdateAiChatSessionRequest(value: unknown): value is UpdateAiChatSessionRequest {
  return isRecord(value) && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateAiMcpRemoteConnectionRequest(value: unknown): UpdateAiMcpRemoteConnectionRequest {
  const normalizedValue = normalizeUpdateAiMcpRemoteConnectionRequestIntegerJson(value);
  if (!(isUpdateAiMcpRemoteConnectionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_ai_mcp_remote_connection_request');
  }
  return normalizedValue;
}

function isUpdateAiMcpRemoteConnectionRequest(value: unknown): value is UpdateAiMcpRemoteConnectionRequest {
  return isRecord(value) && (typeof value["clearServiceToken"] === 'boolean') && (typeof value["displayName"] === 'string') && (typeof value["endpointUrl"] === 'string') && (typeof value["isEnabled"] === 'boolean') && ((value["oAuthScopesJson"] === null) || (typeof value["oAuthScopesJson"] === 'string')) && ((value["serviceToken"] === null) || (typeof value["serviceToken"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateAiModelConfigRequest(value: unknown): UpdateAiModelConfigRequest {
  const normalizedValue = normalizeUpdateAiModelConfigRequestIntegerJson(value);
  if (!(isUpdateAiModelConfigRequest(normalizedValue))) {
    throw new Error('client.invalid_update_ai_model_config_request');
  }
  return normalizedValue;
}

function isUpdateAiModelConfigRequest(value: unknown): value is UpdateAiModelConfigRequest {
  return isRecord(value) && ((value["apiKey"] === null) || (typeof value["apiKey"] === 'string')) && (typeof value["clearApiKey"] === 'boolean') && (typeof value["endpointBaseUrl"] === 'string') && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["modelId"] === 'string') && (typeof value["name"] === 'string') && ((value["organizationId"] === null) || (typeof value["organizationId"] === 'string')) && (typeof value["providerKey"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateAiTenantQuotaRequest(value: unknown): UpdateAiTenantQuotaRequest {
  const normalizedValue = normalizeUpdateAiTenantQuotaRequestIntegerJson(value);
  if (!(isUpdateAiTenantQuotaRequest(normalizedValue))) {
    throw new Error('client.invalid_update_ai_tenant_quota_request');
  }
  return normalizedValue;
}

function isUpdateAiTenantQuotaRequest(value: unknown): value is UpdateAiTenantQuotaRequest {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && ((value["monthlyRequestLimit"] === null) || (typeof value["monthlyRequestLimit"] === 'number' && Number.isSafeInteger(value["monthlyRequestLimit"]))) && ((value["monthlyTokenLimit"] === null) || (typeof value["monthlyTokenLimit"] === 'number' && Number.isSafeInteger(value["monthlyTokenLimit"]))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateCodeGenerationTemplateRequest(value: unknown): UpdateCodeGenerationTemplateRequest {
  const normalizedValue = normalizeUpdateCodeGenerationTemplateRequestIntegerJson(value);
  if (!(isUpdateCodeGenerationTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_update_code_generation_template_request');
  }
  return normalizedValue;
}

function isUpdateCodeGenerationTemplateRequest(value: unknown): value is UpdateCodeGenerationTemplateRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string') && (isCodeGenerationPreviewRequest(value["schema"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateConfigEntryRequest(value: unknown): UpdateConfigEntryRequest {
  const normalizedValue = normalizeUpdateConfigEntryRequestIntegerJson(value);
  if (!(isUpdateConfigEntryRequest(normalizedValue))) {
    throw new Error('client.invalid_update_config_entry_request');
  }
  return normalizedValue;
}

function isUpdateConfigEntryRequest(value: unknown): value is UpdateConfigEntryRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateDataApprovalScenarioBindingBody(value: unknown): UpdateDataApprovalScenarioBindingBody {
  const normalizedValue = normalizeUpdateDataApprovalScenarioBindingBodyIntegerJson(value);
  if (!(isUpdateDataApprovalScenarioBindingBody(normalizedValue))) {
    throw new Error('client.invalid_update_data_approval_scenario_binding_body');
  }
  return normalizedValue;
}

function isUpdateDataApprovalScenarioBindingBody(value: unknown): value is UpdateDataApprovalScenarioBindingBody {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && ((value["version"] === null) || (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]))) && ((value["workflowDefinitionVersionId"] === null) || (typeof value["workflowDefinitionVersionId"] === 'string' && guidPattern.test(value["workflowDefinitionVersionId"])));
}

export function readUpdateDiagnosticPolicyRequest(value: unknown): UpdateDiagnosticPolicyRequest {
  const normalizedValue = normalizeUpdateDiagnosticPolicyRequestIntegerJson(value);
  if (!(isUpdateDiagnosticPolicyRequest(normalizedValue))) {
    throw new Error('client.invalid_update_diagnostic_policy_request');
  }
  return normalizedValue;
}

function isUpdateDiagnosticPolicyRequest(value: unknown): value is UpdateDiagnosticPolicyRequest {
  return isRecord(value) && (typeof value["configEntryVersion"] === 'number' && Number.isSafeInteger(value["configEntryVersion"])) && (typeof value["pressureState"] === 'string') && (Array.isArray(value["rules"]) && value["rules"].every(item14 => isDiagnosticPolicyRuleRequest(item14)));
}

export function readUpdateDictItemRequest(value: unknown): UpdateDictItemRequest {
  const normalizedValue = normalizeUpdateDictItemRequestIntegerJson(value);
  if (!(isUpdateDictItemRequest(normalizedValue))) {
    throw new Error('client.invalid_update_dict_item_request');
  }
  return normalizedValue;
}

function isUpdateDictItemRequest(value: unknown): value is UpdateDictItemRequest {
  return isRecord(value) && ((value["color"] === null) || (typeof value["color"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["label"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateDictTypeRequest(value: unknown): UpdateDictTypeRequest {
  const normalizedValue = normalizeUpdateDictTypeRequestIntegerJson(value);
  if (!(isUpdateDictTypeRequest(normalizedValue))) {
    throw new Error('client.invalid_update_dict_type_request');
  }
  return normalizedValue;
}

function isUpdateDictTypeRequest(value: unknown): value is UpdateDictTypeRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateEnterpriseRequestRequest(value: unknown): UpdateEnterpriseRequestRequest {
  const normalizedValue = normalizeUpdateEnterpriseRequestRequestIntegerJson(value);
  if (!(isUpdateEnterpriseRequestRequest(normalizedValue))) {
    throw new Error('client.invalid_update_enterprise_request_request');
  }
  return normalizedValue;
}

function isUpdateEnterpriseRequestRequest(value: unknown): value is UpdateEnterpriseRequestRequest {
  return isRecord(value) && (typeof value["applicantUserId"] === 'string' && guidPattern.test(value["applicantUserId"])) && (typeof value["requestNumber"] === 'string') && (typeof value["status"] === 'string') && (typeof value["title"] === 'string') && ((typeof value["totalAmount"] === 'number' && Number.isFinite(value["totalAmount"])) || (typeof value["totalAmount"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateGoViewProjectRequest(value: unknown): UpdateGoViewProjectRequest {
  const normalizedValue = normalizeUpdateGoViewProjectRequestIntegerJson(value);
  if (!(isUpdateGoViewProjectRequest(normalizedValue))) {
    throw new Error('client.invalid_update_go_view_project_request');
  }
  return normalizedValue;
}

function isUpdateGoViewProjectRequest(value: unknown): value is UpdateGoViewProjectRequest {
  return isRecord(value) && (typeof value["canvasJson"] === 'string') && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostAnnouncementRequest(value: unknown): UpdateHostAnnouncementRequest {
  const normalizedValue = normalizeUpdateHostAnnouncementRequestIntegerJson(value);
  if (!(isUpdateHostAnnouncementRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_announcement_request');
  }
  return normalizedValue;
}

function isUpdateHostAnnouncementRequest(value: unknown): value is UpdateHostAnnouncementRequest {
  return isRecord(value) && (value["audienceKind"] === undefined || ((value["audienceKind"] === null) || (typeof value["audienceKind"] === 'string'))) && (typeof value["content"] === 'string') && (value["kind"] === undefined || ((value["kind"] === null) || (typeof value["kind"] === 'string'))) && (value["targetOrganizations"] === undefined || ((value["targetOrganizations"] === null) || (Array.isArray(value["targetOrganizations"]) && value["targetOrganizations"].every(item28 => isHostAnnouncementTargetOrganization(item28))))) && (value["targetUserIds"] === undefined || ((value["targetUserIds"] === null) || (Array.isArray(value["targetUserIds"]) && value["targetUserIds"].every(item22 => typeof item22 === 'string' && guidPattern.test(item22))))) && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostDocumentCategoryRequest(value: unknown): UpdateHostDocumentCategoryRequest {
  const normalizedValue = normalizeUpdateHostDocumentCategoryRequestIntegerJson(value);
  if (!(isUpdateHostDocumentCategoryRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_document_category_request');
  }
  return normalizedValue;
}

function isUpdateHostDocumentCategoryRequest(value: unknown): value is UpdateHostDocumentCategoryRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostDocumentItemRequest(value: unknown): UpdateHostDocumentItemRequest {
  const normalizedValue = normalizeUpdateHostDocumentItemRequestIntegerJson(value);
  if (!(isUpdateHostDocumentItemRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_document_item_request');
  }
  return normalizedValue;
}

function isUpdateHostDocumentItemRequest(value: unknown): value is UpdateHostDocumentItemRequest {
  return isRecord(value) && ((value["categoryId"] === null) || (typeof value["categoryId"] === 'string' && guidPattern.test(value["categoryId"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["sort"] === null) || (typeof value["sort"] === 'number' && Number.isSafeInteger(value["sort"]))) && ((value["status"] === null) || (isHostDocumentStatus(value["status"]))) && ((value["tagIds"] === null) || (Array.isArray(value["tagIds"]) && value["tagIds"].every(item15 => typeof item15 === 'string' && guidPattern.test(item15)))) && ((value["thumbnail"] === null) || (typeof value["thumbnail"] === 'string')) && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostDocumentShareStatusRequest(value: unknown): UpdateHostDocumentShareStatusRequest {
  const normalizedValue = normalizeUpdateHostDocumentShareStatusRequestIntegerJson(value);
  if (!(isUpdateHostDocumentShareStatusRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_document_share_status_request');
  }
  return normalizedValue;
}

function isUpdateHostDocumentShareStatusRequest(value: unknown): value is UpdateHostDocumentShareStatusRequest {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostDocumentTagRequest(value: unknown): UpdateHostDocumentTagRequest {
  const normalizedValue = normalizeUpdateHostDocumentTagRequestIntegerJson(value);
  if (!(isUpdateHostDocumentTagRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_document_tag_request');
  }
  return normalizedValue;
}

function isUpdateHostDocumentTagRequest(value: unknown): value is UpdateHostDocumentTagRequest {
  return isRecord(value) && ((value["code"] === null) || (typeof value["code"] === 'string')) && ((value["color"] === null) || (typeof value["color"] === 'string')) && ((value["description"] === null) || (typeof value["description"] === 'string')) && ((value["icon"] === null) || (typeof value["icon"] === 'string')) && (value["isHot"] === undefined || (typeof value["isHot"] === 'boolean')) && (value["isRecommended"] === undefined || (typeof value["isRecommended"] === 'boolean')) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostDocumentVersionRetentionRequest(value: unknown): UpdateHostDocumentVersionRetentionRequest {
  const normalizedValue = normalizeUpdateHostDocumentVersionRetentionRequestIntegerJson(value);
  if (!(isUpdateHostDocumentVersionRetentionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_document_version_retention_request');
  }
  return normalizedValue;
}

function isUpdateHostDocumentVersionRetentionRequest(value: unknown): value is UpdateHostDocumentVersionRetentionRequest {
  return isRecord(value) && (typeof value["batchSize"] === 'number' && Number.isSafeInteger(value["batchSize"])) && (typeof value["maximumRetainedHistoryVersions"] === 'number' && Number.isSafeInteger(value["maximumRetainedHistoryVersions"])) && (typeof value["minimumRetainedVersionsPerItem"] === 'number' && Number.isSafeInteger(value["minimumRetainedVersionsPerItem"])) && (typeof value["pollSeconds"] === 'number' && Number.isSafeInteger(value["pollSeconds"]));
}

export function readUpdateHostFileMetadataRequest(value: unknown): UpdateHostFileMetadataRequest {
  const normalizedValue = normalizeUpdateHostFileMetadataRequestIntegerJson(value);
  if (!(isUpdateHostFileMetadataRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_file_metadata_request');
  }
  return normalizedValue;
}

function isUpdateHostFileMetadataRequest(value: unknown): value is UpdateHostFileMetadataRequest {
  return isRecord(value) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && ((value["folderId"] === null) || (typeof value["folderId"] === 'string' && guidPattern.test(value["folderId"]))) && (typeof value["originalFileName"] === 'string');
}

export function readUpdateHostFolderRequest(value: unknown): UpdateHostFolderRequest {
  const normalizedValue = normalizeUpdateHostFolderRequestIntegerJson(value);
  if (!(isUpdateHostFolderRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_folder_request');
  }
  return normalizedValue;
}

function isUpdateHostFolderRequest(value: unknown): value is UpdateHostFolderRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"])) && (typeof value["name"] === 'string');
}

export function readUpdateHostJobDefinitionRequest(value: unknown): UpdateHostJobDefinitionRequest {
  const normalizedValue = normalizeUpdateHostJobDefinitionRequestIntegerJson(value);
  if (!(isUpdateHostJobDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_job_definition_request');
  }
  return normalizedValue;
}

function isUpdateHostJobDefinitionRequest(value: unknown): value is UpdateHostJobDefinitionRequest {
  return isRecord(value) && (typeof value["allowConcurrentExecutions"] === 'boolean') && ((value["args"] === null) || (isHttpJobArgs(value["args"]))) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["handlerKind"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostJobScheduleRequest(value: unknown): UpdateHostJobScheduleRequest {
  const normalizedValue = normalizeUpdateHostJobScheduleRequestIntegerJson(value);
  if (!(isUpdateHostJobScheduleRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_job_schedule_request');
  }
  return normalizedValue;
}

function isUpdateHostJobScheduleRequest(value: unknown): value is UpdateHostJobScheduleRequest {
  return isRecord(value) && ((value["args"] === null) || (typeof value["args"] === 'string')) && ((value["cronExpression"] === null) || (typeof value["cronExpression"] === 'string')) && ((value["endTime"] === null) || (typeof value["endTime"] === 'string')) && (typeof value["misfirePolicy"] === 'string') && ((value["oneTimeAtUtc"] === null) || (typeof value["oneTimeAtUtc"] === 'string')) && ((value["startTime"] === null) || (typeof value["startTime"] === 'string')) && (typeof value["timeZoneId"] === 'string') && (typeof value["triggerKind"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostMenuRequest(value: unknown): UpdateHostMenuRequest {
  const normalizedValue = normalizeUpdateHostMenuRequestIntegerJson(value);
  if (!(isUpdateHostMenuRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_menu_request');
  }
  return normalizedValue;
}

function isUpdateHostMenuRequest(value: unknown): value is UpdateHostMenuRequest {
  return isRecord(value) && (typeof value["caption"] === 'string') && (typeof value["componentKey"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["icon"] === 'string') && (value["isAffix"] === undefined || (typeof value["isAffix"] === 'boolean')) && (value["isEmbedded"] === undefined || (typeof value["isEmbedded"] === 'boolean')) && (value["isHidden"] === undefined || (typeof value["isHidden"] === 'boolean')) && (value["isKeepAlive"] === undefined || (typeof value["isKeepAlive"] === 'boolean')) && (value["linkUrl"] === undefined || ((value["linkUrl"] === null) || (typeof value["linkUrl"] === 'string'))) && (value["menuType"] === undefined || (typeof value["menuType"] === 'string')) && ((value["parentId"] === null) || (typeof value["parentId"] === 'string')) && (typeof value["path"] === 'string') && (value["redirect"] === undefined || ((value["redirect"] === null) || (typeof value["redirect"] === 'string'))) && (value["remark"] === undefined || ((value["remark"] === null) || (typeof value["remark"] === 'string'))) && (typeof value["requiredPermission"] === 'string') && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostReleaseNoteRequest(value: unknown): UpdateHostReleaseNoteRequest {
  const normalizedValue = normalizeUpdateHostReleaseNoteRequestIntegerJson(value);
  if (!(isUpdateHostReleaseNoteRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_release_note_request');
  }
  return normalizedValue;
}

function isUpdateHostReleaseNoteRequest(value: unknown): value is UpdateHostReleaseNoteRequest {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["title"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"])) && (typeof value["versionLabel"] === 'string');
}

export function readUpdateHostRoleDataScopeRequest(value: unknown): UpdateHostRoleDataScopeRequest {
  const normalizedValue = normalizeUpdateHostRoleDataScopeRequestIntegerJson(value);
  if (!(isUpdateHostRoleDataScopeRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_role_data_scope_request');
  }
  return normalizedValue;
}

function isUpdateHostRoleDataScopeRequest(value: unknown): value is UpdateHostRoleDataScopeRequest {
  return isRecord(value) && (typeof value["dataScopeKind"] === 'string') && (value["tenantId"] === undefined || ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])))) && ((value["unitIds"] === null) || (Array.isArray(value["unitIds"]) && value["unitIds"].every(item16 => typeof item16 === 'string' && guidPattern.test(item16)))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostRoleRequest(value: unknown): UpdateHostRoleRequest {
  const normalizedValue = normalizeUpdateHostRoleRequestIntegerJson(value);
  if (!(isUpdateHostRoleRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_role_request');
  }
  return normalizedValue;
}

function isUpdateHostRoleRequest(value: unknown): value is UpdateHostRoleRequest {
  return isRecord(value) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostTenantPackageRequest(value: unknown): UpdateHostTenantPackageRequest {
  const normalizedValue = normalizeUpdateHostTenantPackageRequestIntegerJson(value);
  if (!(isUpdateHostTenantPackageRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_tenant_package_request');
  }
  return normalizedValue;
}

function isUpdateHostTenantPackageRequest(value: unknown): value is UpdateHostTenantPackageRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostTenantRequest(value: unknown): UpdateHostTenantRequest {
  const normalizedValue = normalizeUpdateHostTenantRequestIntegerJson(value);
  if (!(isUpdateHostTenantRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_tenant_request');
  }
  return normalizedValue;
}

function isUpdateHostTenantRequest(value: unknown): value is UpdateHostTenantRequest {
  return isRecord(value) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateHostUserRequest(value: unknown): UpdateHostUserRequest {
  const normalizedValue = normalizeUpdateHostUserRequestIntegerJson(value);
  if (!(isUpdateHostUserRequest(normalizedValue))) {
    throw new Error('client.invalid_update_host_user_request');
  }
  return normalizedValue;
}

function isUpdateHostUserRequest(value: unknown): value is UpdateHostUserRequest {
  return isRecord(value) && (value["accountType"] === undefined || ((value["accountType"] === null) || (typeof value["accountType"] === 'string'))) && (typeof value["displayName"] === 'string') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"])))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateK3CloudConnectionConfigRequest(value: unknown): UpdateK3CloudConnectionConfigRequest {
  const normalizedValue = normalizeUpdateK3CloudConnectionConfigRequestIntegerJson(value);
  if (!(isUpdateK3CloudConnectionConfigRequest(normalizedValue))) {
    throw new Error('client.invalid_update_k3_cloud_connection_config_request');
  }
  return normalizedValue;
}

function isUpdateK3CloudConnectionConfigRequest(value: unknown): value is UpdateK3CloudConnectionConfigRequest {
  return isRecord(value) && (typeof value["acctId"] === 'string') && (typeof value["baseUrl"] === 'string') && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["lcid"] === 'number' && Number.isSafeInteger(value["lcid"])) && (typeof value["name"] === 'string') && ((value["password"] === null) || (typeof value["password"] === 'string')) && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateLocaleRequest(value: unknown): UpdateLocaleRequest {
  const normalizedValue = normalizeUpdateLocaleRequestIntegerJson(value);
  if (!(isUpdateLocaleRequest(normalizedValue))) {
    throw new Error('client.invalid_update_locale_request');
  }
  return normalizedValue;
}

function isUpdateLocaleRequest(value: unknown): value is UpdateLocaleRequest {
  return isRecord(value) && (typeof value["locale"] === 'string') && (typeof value["profileVersion"] === 'number' && Number.isSafeInteger(value["profileVersion"]));
}

export function readUpdateNotificationBindingRequest(value: unknown): UpdateNotificationBindingRequest {
  const normalizedValue = normalizeUpdateNotificationBindingRequestIntegerJson(value);
  if (!(isUpdateNotificationBindingRequest(normalizedValue))) {
    throw new Error('client.invalid_update_notification_binding_request');
  }
  return normalizedValue;
}

function isUpdateNotificationBindingRequest(value: unknown): value is UpdateNotificationBindingRequest {
  return isRecord(value) && (typeof value["channelKey"] === 'string') && (typeof value["dispatchModeKey"] === 'string') && (typeof value["producerKey"] === 'string') && (typeof value["sceneKey"] === 'string') && (Array.isArray(value["targets"]) && value["targets"].every(item16 => isNotificationBindingTargetInput(item16))) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateNotificationProviderProfileRequest(value: unknown): UpdateNotificationProviderProfileRequest {
  const normalizedValue = normalizeUpdateNotificationProviderProfileRequestIntegerJson(value);
  if (!(isUpdateNotificationProviderProfileRequest(normalizedValue))) {
    throw new Error('client.invalid_update_notification_provider_profile_request');
  }
  return normalizedValue;
}

function isUpdateNotificationProviderProfileRequest(value: unknown): value is UpdateNotificationProviderProfileRequest {
  return isRecord(value) && (isJsonElement(value["nonSecretConfig"])) && ((value["secretReference"] === null) || (typeof value["secretReference"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateNotificationTemplateRequest(value: unknown): UpdateNotificationTemplateRequest {
  const normalizedValue = normalizeUpdateNotificationTemplateRequestIntegerJson(value);
  if (!(isUpdateNotificationTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_update_notification_template_request');
  }
  return normalizedValue;
}

function isUpdateNotificationTemplateRequest(value: unknown): value is UpdateNotificationTemplateRequest {
  return isRecord(value) && (isNotificationTemplateBody(value["draftBody"])) && (typeof value["draftSubject"] === 'string') && (isNotificationTemplateParameterSchema(value["parameterSchema"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateOcrProviderConfigRequest(value: unknown): UpdateOcrProviderConfigRequest {
  const normalizedValue = normalizeUpdateOcrProviderConfigRequestIntegerJson(value);
  if (!(isUpdateOcrProviderConfigRequest(normalizedValue))) {
    throw new Error('client.invalid_update_ocr_provider_config_request');
  }
  return normalizedValue;
}

function isUpdateOcrProviderConfigRequest(value: unknown): value is UpdateOcrProviderConfigRequest {
  return isRecord(value) && ((value["apiKey"] === null) || (typeof value["apiKey"] === 'string')) && (typeof value["baseUrl"] === 'string') && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateOrganizationPositionLevelRequest(value: unknown): UpdateOrganizationPositionLevelRequest {
  const normalizedValue = normalizeUpdateOrganizationPositionLevelRequestIntegerJson(value);
  if (!(isUpdateOrganizationPositionLevelRequest(normalizedValue))) {
    throw new Error('client.invalid_update_organization_position_level_request');
  }
  return normalizedValue;
}

function isUpdateOrganizationPositionLevelRequest(value: unknown): value is UpdateOrganizationPositionLevelRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateOrganizationPositionRequest(value: unknown): UpdateOrganizationPositionRequest {
  const normalizedValue = normalizeUpdateOrganizationPositionRequestIntegerJson(value);
  if (!(isUpdateOrganizationPositionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_organization_position_request');
  }
  return normalizedValue;
}

function isUpdateOrganizationPositionRequest(value: unknown): value is UpdateOrganizationPositionRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateOrganizationUnitRequest(value: unknown): UpdateOrganizationUnitRequest {
  const normalizedValue = normalizeUpdateOrganizationUnitRequestIntegerJson(value);
  if (!(isUpdateOrganizationUnitRequest(normalizedValue))) {
    throw new Error('client.invalid_update_organization_unit_request');
  }
  return normalizedValue;
}

function isUpdateOrganizationUnitRequest(value: unknown): value is UpdateOrganizationUnitRequest {
  return isRecord(value) && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateOrganizationUserPositionRequest(value: unknown): UpdateOrganizationUserPositionRequest {
  const normalizedValue = normalizeUpdateOrganizationUserPositionRequestIntegerJson(value);
  if (!(isUpdateOrganizationUserPositionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_organization_user_position_request');
  }
  return normalizedValue;
}

function isUpdateOrganizationUserPositionRequest(value: unknown): value is UpdateOrganizationUserPositionRequest {
  return isRecord(value) && (typeof value["isPrimary"] === 'boolean') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateOrganizationUserUnitRequest(value: unknown): UpdateOrganizationUserUnitRequest {
  const normalizedValue = normalizeUpdateOrganizationUserUnitRequestIntegerJson(value);
  if (!(isUpdateOrganizationUserUnitRequest(normalizedValue))) {
    throw new Error('client.invalid_update_organization_user_unit_request');
  }
  return normalizedValue;
}

function isUpdateOrganizationUserUnitRequest(value: unknown): value is UpdateOrganizationUserUnitRequest {
  return isRecord(value) && (typeof value["isPrimary"] === 'boolean') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdatePaymentMerchantConfigRequest(value: unknown): UpdatePaymentMerchantConfigRequest {
  const normalizedValue = normalizeUpdatePaymentMerchantConfigRequestIntegerJson(value);
  if (!(isUpdatePaymentMerchantConfigRequest(normalizedValue))) {
    throw new Error('client.invalid_update_payment_merchant_config_request');
  }
  return normalizedValue;
}

function isUpdatePaymentMerchantConfigRequest(value: unknown): value is UpdatePaymentMerchantConfigRequest {
  return isRecord(value) && ((value["apiV3Key"] === null) || (typeof value["apiV3Key"] === 'string')) && (typeof value["appId"] === 'string') && (typeof value["certificateSerialNo"] === 'string') && (typeof value["channelKey"] === 'string') && (typeof value["clearApiV3Key"] === 'boolean') && (typeof value["clearPrivateKey"] === 'boolean') && (typeof value["isDefault"] === 'boolean') && (typeof value["isEnabled"] === 'boolean') && (typeof value["merchantId"] === 'string') && (typeof value["name"] === 'string') && (typeof value["notifyUrl"] === 'string') && ((value["privateKeyPem"] === null) || (typeof value["privateKeyPem"] === 'string')) && (typeof value["returnUrl"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdatePersonalScheduleRequest(value: unknown): UpdatePersonalScheduleRequest {
  const normalizedValue = normalizeUpdatePersonalScheduleRequestIntegerJson(value);
  if (!(isUpdatePersonalScheduleRequest(normalizedValue))) {
    throw new Error('client.invalid_update_personal_schedule_request');
  }
  return normalizedValue;
}

function isUpdatePersonalScheduleRequest(value: unknown): value is UpdatePersonalScheduleRequest {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["endAtUtc"] === 'string') && (typeof value["startAtUtc"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdatePrintingTemplateRequest(value: unknown): UpdatePrintingTemplateRequest {
  const normalizedValue = normalizeUpdatePrintingTemplateRequestIntegerJson(value);
  if (!(isUpdatePrintingTemplateRequest(normalizedValue))) {
    throw new Error('client.invalid_update_printing_template_request');
  }
  return normalizedValue;
}

function isUpdatePrintingTemplateRequest(value: unknown): value is UpdatePrintingTemplateRequest {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["layoutHtml"] === 'string') && (typeof value["name"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateReportingDataSourceRequest(value: unknown): UpdateReportingDataSourceRequest {
  const normalizedValue = normalizeUpdateReportingDataSourceRequestIntegerJson(value);
  if (!(isUpdateReportingDataSourceRequest(normalizedValue))) {
    throw new Error('client.invalid_update_reporting_data_source_request');
  }
  return normalizedValue;
}

function isUpdateReportingDataSourceRequest(value: unknown): value is UpdateReportingDataSourceRequest {
  return isRecord(value) && (typeof value["databaseName"] === 'string') && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && ((value["password"] === null) || (typeof value["password"] === 'string')) && (typeof value["port"] === 'number' && Number.isSafeInteger(value["port"])) && (typeof value["providerKey"] === 'string') && (typeof value["serverHost"] === 'string') && (typeof value["trustServerCertificate"] === 'boolean') && (typeof value["username"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateReportingDefinitionRequest(value: unknown): UpdateReportingDefinitionRequest {
  const normalizedValue = normalizeUpdateReportingDefinitionRequestIntegerJson(value);
  if (!(isUpdateReportingDefinitionRequest(normalizedValue))) {
    throw new Error('client.invalid_update_reporting_definition_request');
  }
  return normalizedValue;
}

function isUpdateReportingDefinitionRequest(value: unknown): value is UpdateReportingDefinitionRequest {
  return isRecord(value) && (typeof value["dataSourceId"] === 'string' && guidPattern.test(value["dataSourceId"])) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["groupId"] === 'string' && guidPattern.test(value["groupId"])) && (typeof value["isEnabled"] === 'boolean') && ((value["layoutConfigJson"] === null) || (typeof value["layoutConfigJson"] === 'string')) && (typeof value["name"] === 'string') && (Array.isArray(value["parameterSchema"]) && value["parameterSchema"].every(item24 => isReportingParameterSchemaEntry(item24))) && (typeof value["queryPortKey"] === 'string') && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateReportingGroupRequest(value: unknown): UpdateReportingGroupRequest {
  const normalizedValue = normalizeUpdateReportingGroupRequestIntegerJson(value);
  if (!(isUpdateReportingGroupRequest(normalizedValue))) {
    throw new Error('client.invalid_update_reporting_group_request');
  }
  return normalizedValue;
}

function isUpdateReportingGroupRequest(value: unknown): value is UpdateReportingGroupRequest {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["name"] === 'string') && ((value["parentId"] === null) || (typeof value["parentId"] === 'string' && guidPattern.test(value["parentId"]))) && (typeof value["sortOrder"] === 'number' && Number.isSafeInteger(value["sortOrder"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateSelfServiceProfileRequest(value: unknown): UpdateSelfServiceProfileRequest {
  const normalizedValue = normalizeUpdateSelfServiceProfileRequestIntegerJson(value);
  if (!(isUpdateSelfServiceProfileRequest(normalizedValue))) {
    throw new Error('client.invalid_update_self_service_profile_request');
  }
  return normalizedValue;
}

function isUpdateSelfServiceProfileRequest(value: unknown): value is UpdateSelfServiceProfileRequest {
  return isRecord(value) && ((value["displayName"] === null) || (typeof value["displayName"] === 'string')) && ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"]))) && ((value["userVersion"] === null) || (typeof value["userVersion"] === 'number' && Number.isSafeInteger(value["userVersion"])));
}

export function readUpdateSerialNumberRuleRequest(value: unknown): UpdateSerialNumberRuleRequest {
  const normalizedValue = normalizeUpdateSerialNumberRuleRequestIntegerJson(value);
  if (!(isUpdateSerialNumberRuleRequest(normalizedValue))) {
    throw new Error('client.invalid_update_serial_number_rule_request');
  }
  return normalizedValue;
}

function isUpdateSerialNumberRuleRequest(value: unknown): value is UpdateSerialNumberRuleRequest {
  return isRecord(value) && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isSafeInteger(value["displayOrder"])) && (typeof value["isEnabled"] === 'boolean') && (typeof value["maximumValue"] === 'number' && Number.isSafeInteger(value["maximumValue"])) && (typeof value["minimumValue"] === 'number' && Number.isSafeInteger(value["minimumValue"])) && (typeof value["pattern"] === 'string') && (isSerialNumberResetInterval(value["resetInterval"])) && (isSerialNumberRuleScope(value["scope"])) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateTenantBrandingRequest(value: unknown): UpdateTenantBrandingRequest {
  const normalizedValue = normalizeUpdateTenantBrandingRequestIntegerJson(value);
  if (!(isUpdateTenantBrandingRequest(normalizedValue))) {
    throw new Error('client.invalid_update_tenant_branding_request');
  }
  return normalizedValue;
}

function isUpdateTenantBrandingRequest(value: unknown): value is UpdateTenantBrandingRequest {
  return isRecord(value) && ((value["contactAddress"] === null) || (typeof value["contactAddress"] === 'string')) && ((value["contactEmail"] === null) || (typeof value["contactEmail"] === 'string')) && ((value["contactPhone"] === null) || (typeof value["contactPhone"] === 'string')) && ((value["copyright"] === null) || (typeof value["copyright"] === 'string')) && ((value["systemTitle"] === null) || (typeof value["systemTitle"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readUpdateWorkflowDefinitionDraftRequest(value: unknown): UpdateWorkflowDefinitionDraftRequest {
  const normalizedValue = normalizeUpdateWorkflowDefinitionDraftRequestIntegerJson(value);
  if (!(isUpdateWorkflowDefinitionDraftRequest(normalizedValue))) {
    throw new Error('client.invalid_update_workflow_definition_draft_request');
  }
  return normalizedValue;
}

function isUpdateWorkflowDefinitionDraftRequest(value: unknown): value is UpdateWorkflowDefinitionDraftRequest {
  return isRecord(value) && (value["businessTitleTemplate"] === undefined || ((value["businessTitleTemplate"] === null) || (typeof value["businessTitleTemplate"] === 'string'))) && (isWorkflowDefinitionDraft(value["draft"])) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"]));
}

export function readUpdateWorkflowFormDraftRequest(value: unknown): UpdateWorkflowFormDraftRequest {
  const normalizedValue = normalizeUpdateWorkflowFormDraftRequestIntegerJson(value);
  if (!(isUpdateWorkflowFormDraftRequest(normalizedValue))) {
    throw new Error('client.invalid_update_workflow_form_draft_request');
  }
  return normalizedValue;
}

function isUpdateWorkflowFormDraftRequest(value: unknown): value is UpdateWorkflowFormDraftRequest {
  return isRecord(value) && (isWorkflowFormSchema(value["draft"])) && (typeof value["expectedRevision"] === 'number' && Number.isSafeInteger(value["expectedRevision"]));
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

export function readWorkflowAssigneePreviewResponse(value: unknown): WorkflowAssigneePreviewResponse {
  if (!(isWorkflowAssigneePreviewResponse(value))) {
    throw new Error('client.invalid_workflow_assignee_preview_response');
  }
  return value;
}

function isWorkflowAssigneePreviewResponse(value: unknown): value is WorkflowAssigneePreviewResponse {
  return isRecord(value) && (Array.isArray(value["users"]) && value["users"].every(item14 => isWorkflowRecipientCandidateResponse(item14)));
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
  return isRecord(value) && (typeof value["businessId"] === 'string') && ((value["businessTitle"] === null) || (typeof value["businessTitle"] === 'string')) && (typeof value["businessType"] === 'string') && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["nodeKey"] === 'string') && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && ((value["stepId"] === null) || (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"])));
}

export function readWorkflowDefinitionDraft(value: unknown): WorkflowDefinitionDraft {
  const normalizedValue = normalizeWorkflowDefinitionDraftIntegerJson(value);
  if (!(isWorkflowDefinitionDraft(normalizedValue))) {
    throw new Error('client.invalid_workflow_definition_draft');
  }
  return normalizedValue;
}

function isWorkflowDefinitionDraft(value: unknown): value is WorkflowDefinitionDraft {
  return isRecord(value) && (Array.isArray(value["nodes"]) && value["nodes"].every(item14 => isWorkflowNodeDraft(item14))) && (typeof value["schemaVersion"] === 'number' && Number.isSafeInteger(value["schemaVersion"]));
}

export function readWorkflowDefinitionResponse(value: unknown): WorkflowDefinitionResponse {
  const normalizedValue = normalizeWorkflowDefinitionResponseIntegerJson(value);
  if (!(isWorkflowDefinitionResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_definition_response');
  }
  return normalizedValue;
}

function isWorkflowDefinitionResponse(value: unknown): value is WorkflowDefinitionResponse {
  return isRecord(value) && ((value["businessTitleTemplate"] === null) || (typeof value["businessTitleTemplate"] === 'string')) && (typeof value["createdAtUtc"] === 'string') && (typeof value["definitionKey"] === 'string') && (isWorkflowDefinitionDraft(value["draft"])) && (typeof value["draftRevision"] === 'number' && Number.isSafeInteger(value["draftRevision"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && (typeof value["statusKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readWorkflowDefinitionVersionResponse(value: unknown): WorkflowDefinitionVersionResponse {
  const normalizedValue = normalizeWorkflowDefinitionVersionResponseIntegerJson(value);
  if (!(isWorkflowDefinitionVersionResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_definition_version_response');
  }
  return normalizedValue;
}

function isWorkflowDefinitionVersionResponse(value: unknown): value is WorkflowDefinitionVersionResponse {
  return isRecord(value) && ((value["businessTitleTemplate"] === null) || (typeof value["businessTitleTemplate"] === 'string')) && (typeof value["canonicalJson"] === 'string') && (typeof value["contentHash"] === 'string') && (typeof value["definitionId"] === 'string' && guidPattern.test(value["definitionId"])) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedById"] === 'string' && guidPattern.test(value["publishedById"])) && (typeof value["schemaVersion"] === 'number' && Number.isSafeInteger(value["schemaVersion"])) && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"]));
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
  const normalizedValue = normalizeWorkflowFormComponentCatalogResponseIntegerJson(value);
  if (!(isWorkflowFormComponentCatalogResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_form_component_catalog_response');
  }
  return normalizedValue;
}

function isWorkflowFormComponentCatalogResponse(value: unknown): value is WorkflowFormComponentCatalogResponse {
  return isRecord(value) && (typeof value["adapterVersion"] === 'number' && Number.isSafeInteger(value["adapterVersion"])) && (typeof value["catalogVersion"] === 'number' && Number.isSafeInteger(value["catalogVersion"])) && (Array.isArray(value["components"]) && value["components"].every(item19 => isWorkflowFormComponentResponse(item19))) && (typeof value["schemaVersion"] === 'number' && Number.isSafeInteger(value["schemaVersion"]));
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
  const normalizedValue = normalizeWorkflowFormResponseIntegerJson(value);
  if (!(isWorkflowFormResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_form_response');
  }
  return normalizedValue;
}

function isWorkflowFormResponse(value: unknown): value is WorkflowFormResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (isWorkflowFormSchema(value["draft"])) && (typeof value["draftRevision"] === 'number' && Number.isSafeInteger(value["draftRevision"])) && (typeof value["formKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((value["latestPublishedVersionId"] === null) || (typeof value["latestPublishedVersionId"] === 'string' && guidPattern.test(value["latestPublishedVersionId"]))) && (typeof value["statusKey"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && (typeof value["version"] === 'number' && Number.isSafeInteger(value["version"]));
}

export function readWorkflowFormSchema(value: unknown): WorkflowFormSchema {
  const normalizedValue = normalizeWorkflowFormSchemaIntegerJson(value);
  if (!(isWorkflowFormSchema(normalizedValue))) {
    throw new Error('client.invalid_workflow_form_schema');
  }
  return normalizedValue;
}

function isWorkflowFormSchema(value: unknown): value is WorkflowFormSchema {
  return isRecord(value) && (typeof value["adapterVersion"] === 'number' && Number.isSafeInteger(value["adapterVersion"])) && (typeof value["schemaVersion"] === 'number' && Number.isSafeInteger(value["schemaVersion"])) && (Array.isArray(value["sections"]) && value["sections"].every(item17 => isWorkflowFormSection(item17)));
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
  const normalizedValue = normalizeWorkflowFormVersionResponseIntegerJson(value);
  if (!(isWorkflowFormVersionResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_form_version_response');
  }
  return normalizedValue;
}

function isWorkflowFormVersionResponse(value: unknown): value is WorkflowFormVersionResponse {
  return isRecord(value) && (typeof value["adapterVersion"] === 'number' && Number.isSafeInteger(value["adapterVersion"])) && (typeof value["componentCatalogVersion"] === 'number' && Number.isSafeInteger(value["componentCatalogVersion"])) && (typeof value["contentHash"] === 'string') && (typeof value["formDefinitionId"] === 'string' && guidPattern.test(value["formDefinitionId"])) && (typeof value["formSchemaJson"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["publishedAtUtc"] === 'string') && (typeof value["publishedById"] === 'string' && guidPattern.test(value["publishedById"])) && (typeof value["schemaVersion"] === 'number' && Number.isSafeInteger(value["schemaVersion"])) && (typeof value["versionNumber"] === 'number' && Number.isSafeInteger(value["versionNumber"])) && (typeof value["webRenderSchemaJson"] === 'string');
}

export function readWorkflowGatewayJoinBranchResponse(value: unknown): WorkflowGatewayJoinBranchResponse {
  if (!(isWorkflowGatewayJoinBranchResponse(value))) {
    throw new Error('client.invalid_workflow_gateway_join_branch_response');
  }
  return value;
}

function isWorkflowGatewayJoinBranchResponse(value: unknown): value is WorkflowGatewayJoinBranchResponse {
  return isRecord(value) && ((value["arrivedAtUtc"] === null) || (typeof value["arrivedAtUtc"] === 'string')) && (typeof value["branchKey"] === 'string');
}

export function readWorkflowGatewayJoinResponse(value: unknown): WorkflowGatewayJoinResponse {
  const normalizedValue = normalizeWorkflowGatewayJoinResponseIntegerJson(value);
  if (!(isWorkflowGatewayJoinResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_gateway_join_response');
  }
  return normalizedValue;
}

function isWorkflowGatewayJoinResponse(value: unknown): value is WorkflowGatewayJoinResponse {
  return isRecord(value) && (typeof value["arrivedBranchCount"] === 'number' && Number.isSafeInteger(value["arrivedBranchCount"])) && (Array.isArray(value["branches"]) && value["branches"].every(item17 => isWorkflowGatewayJoinBranchResponse(item17))) && (typeof value["forkNodeKey"] === 'string') && (typeof value["gatewayTypeKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["joinNodeKey"] === 'string') && (typeof value["requiredBranchCount"] === 'number' && Number.isSafeInteger(value["requiredBranchCount"])) && (typeof value["statusKey"] === 'string');
}

export function readWorkflowInstanceListItemResponse(value: unknown): WorkflowInstanceListItemResponse {
  if (!(isWorkflowInstanceListItemResponse(value))) {
    throw new Error('client.invalid_workflow_instance_list_item_response');
  }
  return value;
}

function isWorkflowInstanceListItemResponse(value: unknown): value is WorkflowInstanceListItemResponse {
  return isRecord(value) && (typeof value["businessId"] === 'string') && ((value["businessTitle"] === null) || (typeof value["businessTitle"] === 'string')) && (typeof value["businessType"] === 'string') && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["definitionKey"] === 'string') && (typeof value["definitionVersionId"] === 'string' && guidPattern.test(value["definitionVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["startedAtUtc"] === 'string') && (typeof value["startedById"] === 'string' && guidPattern.test(value["startedById"])) && (typeof value["statusKey"] === 'string');
}

export function readWorkflowInstanceResponse(value: unknown): WorkflowInstanceResponse {
  const normalizedValue = normalizeWorkflowInstanceResponseIntegerJson(value);
  if (!(isWorkflowInstanceResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_instance_response');
  }
  return normalizedValue;
}

function isWorkflowInstanceResponse(value: unknown): value is WorkflowInstanceResponse {
  return isRecord(value) && (value["activeNodeKey"] === undefined || ((value["activeNodeKey"] === null) || (typeof value["activeNodeKey"] === 'string'))) && ((value["activeTodoId"] === null) || (typeof value["activeTodoId"] === 'string' && guidPattern.test(value["activeTodoId"]))) && (value["approvalModeKey"] === undefined || ((value["approvalModeKey"] === null) || (typeof value["approvalModeKey"] === 'string'))) && (value["approvedCount"] === undefined || ((value["approvedCount"] === null) || (typeof value["approvedCount"] === 'number' && Number.isSafeInteger(value["approvedCount"])))) && (typeof value["businessId"] === 'string') && ((value["businessTitle"] === null) || (typeof value["businessTitle"] === 'string')) && (typeof value["businessType"] === 'string') && (typeof value["definitionVersionId"] === 'string' && guidPattern.test(value["definitionVersionId"])) && (value["dueAtUtc"] === undefined || ((value["dueAtUtc"] === null) || (typeof value["dueAtUtc"] === 'string'))) && (value["escalatedAtUtc"] === undefined || ((value["escalatedAtUtc"] === null) || (typeof value["escalatedAtUtc"] === 'string'))) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (value["gatewayJoins"] === undefined || ((value["gatewayJoins"] === null) || (Array.isArray(value["gatewayJoins"]) && value["gatewayJoins"].every(item21 => isWorkflowGatewayJoinResponse(item21))))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (value["pendingCount"] === undefined || ((value["pendingCount"] === null) || (typeof value["pendingCount"] === 'number' && Number.isSafeInteger(value["pendingCount"])))) && (value["rejectedCount"] === undefined || ((value["rejectedCount"] === null) || (typeof value["rejectedCount"] === 'number' && Number.isSafeInteger(value["rejectedCount"])))) && (value["reminderCount"] === undefined || (typeof value["reminderCount"] === 'number' && Number.isSafeInteger(value["reminderCount"]))) && (value["requiredApprovalCount"] === undefined || ((value["requiredApprovalCount"] === null) || (typeof value["requiredApprovalCount"] === 'number' && Number.isSafeInteger(value["requiredApprovalCount"])))) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["startedAtUtc"] === 'string') && (typeof value["statusKey"] === 'string') && (value["timeoutStatusKey"] === undefined || (typeof value["timeoutStatusKey"] === 'string'));
}

export function readWorkflowNodeDraft(value: unknown): WorkflowNodeDraft {
  const normalizedValue = normalizeWorkflowNodeDraftIntegerJson(value);
  if (!(isWorkflowNodeDraft(normalizedValue))) {
    throw new Error('client.invalid_workflow_node_draft');
  }
  return normalizedValue;
}

function isWorkflowNodeDraft(value: unknown): value is WorkflowNodeDraft {
  return isRecord(value) && (isJsonElement(value["config"])) && (typeof value["nodeKey"] === 'string') && (typeof value["nodeSchemaVersion"] === 'number' && Number.isSafeInteger(value["nodeSchemaVersion"])) && (typeof value["nodeTypeKey"] === 'string');
}

export function readWorkflowNodeTypeCatalogResponse(value: unknown): WorkflowNodeTypeCatalogResponse {
  const normalizedValue = normalizeWorkflowNodeTypeCatalogResponseIntegerJson(value);
  if (!(isWorkflowNodeTypeCatalogResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_node_type_catalog_response');
  }
  return normalizedValue;
}

function isWorkflowNodeTypeCatalogResponse(value: unknown): value is WorkflowNodeTypeCatalogResponse {
  return isRecord(value) && (typeof value["catalogVersion"] === 'number' && Number.isSafeInteger(value["catalogVersion"])) && (typeof value["definitionSchemaVersion"] === 'number' && Number.isSafeInteger(value["definitionSchemaVersion"])) && (Array.isArray(value["nodeTypes"]) && value["nodeTypes"].every(item18 => isWorkflowNodeTypeResponse(item18)));
}

export function readWorkflowNodeTypeResponse(value: unknown): WorkflowNodeTypeResponse {
  const normalizedValue = normalizeWorkflowNodeTypeResponseIntegerJson(value);
  if (!(isWorkflowNodeTypeResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_node_type_response');
  }
  return normalizedValue;
}

function isWorkflowNodeTypeResponse(value: unknown): value is WorkflowNodeTypeResponse {
  return isRecord(value) && (typeof value["designable"] === 'boolean') && (typeof value["executable"] === 'boolean') && (typeof value["nodeSchemaVersion"] === 'number' && Number.isSafeInteger(value["nodeSchemaVersion"])) && (typeof value["nodeTypeKey"] === 'string') && (typeof value["publishable"] === 'boolean') && (typeof value["supportsFieldPolicies"] === 'boolean');
}

export function readWorkflowRecipientCandidatePageResponse(value: unknown): WorkflowRecipientCandidatePageResponse {
  const normalizedValue = normalizeWorkflowRecipientCandidatePageResponseIntegerJson(value);
  if (!(isWorkflowRecipientCandidatePageResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_recipient_candidate_page_response');
  }
  return normalizedValue;
}

function isWorkflowRecipientCandidatePageResponse(value: unknown): value is WorkflowRecipientCandidatePageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWorkflowRecipientCandidateResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
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
  const normalizedValue = normalizeWorkflowRecoveryTaskResponseIntegerJson(value);
  if (!(isWorkflowRecoveryTaskResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_recovery_task_response');
  }
  return normalizedValue;
}

function isWorkflowRecoveryTaskResponse(value: unknown): value is WorkflowRecoveryTaskResponse {
  return isRecord(value) && (typeof value["attemptCount"] === 'number' && Number.isSafeInteger(value["attemptCount"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["kindKey"] === 'string') && ((value["lastError"] === null) || (typeof value["lastError"] === 'string')) && ((value["leaseExpiresAtUtc"] === null) || (typeof value["leaseExpiresAtUtc"] === 'string')) && (typeof value["leaseGeneration"] === 'number' && Number.isSafeInteger(value["leaseGeneration"])) && ((value["leaseOwnerKey"] === null) || (typeof value["leaseOwnerKey"] === 'string')) && ((value["nextAttemptAtUtc"] === null) || (typeof value["nextAttemptAtUtc"] === 'string')) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && ((value["stepId"] === null) || (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"]))) && (typeof value["updatedAtUtc"] === 'string');
}

export function readWorkflowTodoDetailResponse(value: unknown): WorkflowTodoDetailResponse {
  const normalizedValue = normalizeWorkflowTodoDetailResponseIntegerJson(value);
  if (!(isWorkflowTodoDetailResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_todo_detail_response');
  }
  return normalizedValue;
}

function isWorkflowTodoDetailResponse(value: unknown): value is WorkflowTodoDetailResponse {
  return isRecord(value) && (typeof value["approvalModeKey"] === 'string') && (typeof value["approvedCount"] === 'number' && Number.isSafeInteger(value["approvedCount"])) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (isRecord(value["fieldPolicies"])) && (isJsonElement(value["formSchema"])) && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["pendingCount"] === 'number' && Number.isSafeInteger(value["pendingCount"])) && (typeof value["rejectedCount"] === 'number' && Number.isSafeInteger(value["rejectedCount"])) && (typeof value["requiredApprovalCount"] === 'number' && Number.isSafeInteger(value["requiredApprovalCount"])) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"])) && (isJsonElement(value["submission"])) && (typeof value["submissionRevision"] === 'number' && Number.isSafeInteger(value["submissionRevision"]));
}

export function readWorkflowTodoListItemResponse(value: unknown): WorkflowTodoListItemResponse {
  const normalizedValue = normalizeWorkflowTodoListItemResponseIntegerJson(value);
  if (!(isWorkflowTodoListItemResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_todo_list_item_response');
  }
  return normalizedValue;
}

function isWorkflowTodoListItemResponse(value: unknown): value is WorkflowTodoListItemResponse {
  return isRecord(value) && (typeof value["arrivedAtUtc"] === 'string') && (typeof value["businessId"] === 'string') && ((value["businessTitle"] === null) || (typeof value["businessTitle"] === 'string')) && (typeof value["businessType"] === 'string') && ((value["completedAtUtc"] === null) || (typeof value["completedAtUtc"] === 'string')) && (typeof value["definitionKey"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["instanceStatusKey"] === 'string') && (typeof value["nodeKey"] === 'string') && ((value["resultActionKey"] === null) || (typeof value["resultActionKey"] === 'string')) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"]));
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
  const normalizedValue = normalizeWorkflowTodoRuntimeResponseIntegerJson(value);
  if (!(isWorkflowTodoRuntimeResponse(normalizedValue))) {
    throw new Error('client.invalid_workflow_todo_runtime_response');
  }
  return normalizedValue;
}

function isWorkflowTodoRuntimeResponse(value: unknown): value is WorkflowTodoRuntimeResponse {
  return isRecord(value) && (typeof value["approvalModeKey"] === 'string') && (typeof value["approvedCount"] === 'number' && Number.isSafeInteger(value["approvedCount"])) && (typeof value["assigneeUserId"] === 'string' && guidPattern.test(value["assigneeUserId"])) && (isRecord(value["fieldPolicies"])) && (isJsonElement(value["formSchema"])) && (typeof value["formSchemaHash"] === 'string') && (typeof value["formVersionId"] === 'string' && guidPattern.test(value["formVersionId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["instanceId"] === 'string' && guidPattern.test(value["instanceId"])) && (typeof value["pendingCount"] === 'number' && Number.isSafeInteger(value["pendingCount"])) && (typeof value["rejectedCount"] === 'number' && Number.isSafeInteger(value["rejectedCount"])) && (typeof value["requiredApprovalCount"] === 'number' && Number.isSafeInteger(value["requiredApprovalCount"])) && (typeof value["revision"] === 'number' && Number.isSafeInteger(value["revision"])) && (typeof value["statusKey"] === 'string') && (typeof value["stepId"] === 'string' && guidPattern.test(value["stepId"])) && (isJsonElement(value["submission"])) && (typeof value["submissionRevision"] === 'number' && Number.isSafeInteger(value["submissionRevision"]));
}

export function readAiCancelAgentRunResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_ai_cancel_agent_run_response');
  }
  return value as boolean;
}

export function readAiCancelChatGenerationResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_ai_cancel_chat_generation_response');
  }
  return value as boolean;
}

export function readAiDeleteChatSessionResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_ai_delete_chat_session_response');
  }
  return value as boolean;
}

export function readAiDiscoverMcpRemoteToolsResponse(value: unknown): Array<AiMcpRemoteDiscoveredToolItem> {
  if (!(Array.isArray(value) && value.every(item5 => isAiMcpRemoteDiscoveredToolItem(item5)))) {
    throw new Error('client.invalid_ai_discover_mcp_remote_tools_response');
  }
  return value as Array<AiMcpRemoteDiscoveredToolItem>;
}

export function readAiListAgentDelegationsResponse(value: unknown): Array<AiAgentDelegationResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeAiAgentDelegationResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isAiAgentDelegationResponse(item15)))) {
    throw new Error('client.invalid_ai_list_agent_delegations_response');
  }
  return normalizedValue as Array<AiAgentDelegationResponse>;
}

export function readAiListAgentToolsResponse(value: unknown): Array<AiAgentToolCatalogItem> {
  if (!(Array.isArray(value) && value.every(item5 => isAiAgentToolCatalogItem(item5)))) {
    throw new Error('client.invalid_ai_list_agent_tools_response');
  }
  return value as Array<AiAgentToolCatalogItem>;
}

export function readAiListMcpRemoteConnectionsResponse(value: unknown): Array<AiMcpRemoteConnectionListItem> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeAiMcpRemoteConnectionListItemIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isAiMcpRemoteConnectionListItem(item15)))) {
    throw new Error('client.invalid_ai_list_mcp_remote_connections_response');
  }
  return normalizedValue as Array<AiMcpRemoteConnectionListItem>;
}

export function readAiListMcpRemoteToolApprovalsResponse(value: unknown): Array<AiMcpRemoteToolApprovalItem> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeAiMcpRemoteToolApprovalItemIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isAiMcpRemoteToolApprovalItem(item15)))) {
    throw new Error('client.invalid_ai_list_mcp_remote_tool_approvals_response');
  }
  return normalizedValue as Array<AiMcpRemoteToolApprovalItem>;
}

export function readAiResumeAgentRunResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_ai_resume_agent_run_response');
  }
  return value as boolean;
}

export function readCodeGenerationListCatalogObjectsResponse(value: unknown): Array<CodeGenerationCatalogObjectResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isCodeGenerationCatalogObjectResponse(item5)))) {
    throw new Error('client.invalid_code_generation_list_catalog_objects_response');
  }
  return value as Array<CodeGenerationCatalogObjectResponse>;
}

export function readCodeGenerationListCatalogTablesResponse(value: unknown): Array<CodeGenerationCatalogTableResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isCodeGenerationCatalogTableResponse(item5)))) {
    throw new Error('client.invalid_code_generation_list_catalog_tables_response');
  }
  return value as Array<CodeGenerationCatalogTableResponse>;
}

export function readCodeGenerationListCatalogViewsResponse(value: unknown): Array<CodeGenerationCatalogObjectResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isCodeGenerationCatalogObjectResponse(item5)))) {
    throw new Error('client.invalid_code_generation_list_catalog_views_response');
  }
  return value as Array<CodeGenerationCatalogObjectResponse>;
}

export function readDataApprovalsListScenariosResponse(value: unknown): Array<DataApprovalScenarioResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeDataApprovalScenarioResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isDataApprovalScenarioResponse(item15)))) {
    throw new Error('client.invalid_data_approvals_list_scenarios_response');
  }
  return normalizedValue as Array<DataApprovalScenarioResponse>;
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
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeHostDocumentCategoryResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isHostDocumentCategoryResponse(item15)))) {
    throw new Error('client.invalid_document_host_list_categories_response');
  }
  return normalizedValue as Array<HostDocumentCategoryResponse>;
}

export function readDocumentHostListDocumentPermissionsResponse(value: unknown): Array<HostDocumentPermissionResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isHostDocumentPermissionResponse(item5)))) {
    throw new Error('client.invalid_document_host_list_document_permissions_response');
  }
  return value as Array<HostDocumentPermissionResponse>;
}

export function readDocumentHostListItemVersionsResponse(value: unknown): Array<HostDocumentVersionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeHostDocumentVersionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isHostDocumentVersionResponse(item15)))) {
    throw new Error('client.invalid_document_host_list_item_versions_response');
  }
  return normalizedValue as Array<HostDocumentVersionResponse>;
}

export function readDocumentHostListTagsResponse(value: unknown): Array<HostDocumentTagResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeHostDocumentTagResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isHostDocumentTagResponse(item15)))) {
    throw new Error('client.invalid_document_host_list_tags_response');
  }
  return normalizedValue as Array<HostDocumentTagResponse>;
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

export function readFilesGetHostFolderTreeResponse(value: unknown): Array<HostFolderTreeNode> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeHostFolderTreeNodeIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isHostFolderTreeNode(item15)))) {
    throw new Error('client.invalid_files_get_host_folder_tree_response');
  }
  return normalizedValue as Array<HostFolderTreeNode>;
}

export function readGoviewListProjectsResponse(value: unknown): Array<GoViewProjectResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeGoViewProjectResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isGoViewProjectResponse(item15)))) {
    throw new Error('client.invalid_goview_list_projects_response');
  }
  return normalizedValue as Array<GoViewProjectResponse>;
}

export function readGoviewListProjectVersionsResponse(value: unknown): Array<GoViewProjectVersionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeGoViewProjectVersionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isGoViewProjectVersionResponse(item15)))) {
    throw new Error('client.invalid_goview_list_project_versions_response');
  }
  return normalizedValue as Array<GoViewProjectVersionResponse>;
}

export function readIdentityExportHostUsersResponse(value: unknown): Array<HostUserResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeHostUserResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isHostUserResponse(item15)))) {
    throw new Error('client.invalid_identity_export_host_users_response');
  }
  return normalizedValue as Array<HostUserResponse>;
}

export function readIdentityGetAuthorizationTreeResponse(value: unknown): Array<AuthorizationTreeModuleResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeAuthorizationTreeModuleResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isAuthorizationTreeModuleResponse(item15)))) {
    throw new Error('client.invalid_identity_get_authorization_tree_response');
  }
  return normalizedValue as Array<AuthorizationTreeModuleResponse>;
}

export function readIdentityListAllHostMenusResponse(value: unknown): Array<HostMenuResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeHostMenuResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isHostMenuResponse(item15)))) {
    throw new Error('client.invalid_identity_list_all_host_menus_response');
  }
  return normalizedValue as Array<HostMenuResponse>;
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

export function readImportExportListStaticSchemasResponse(value: unknown): Array<StaticImportSchemaDefinition> {
  if (!(Array.isArray(value) && value.every(item5 => isStaticImportSchemaDefinition(item5)))) {
    throw new Error('client.invalid_import_export_list_static_schemas_response');
  }
  return value as Array<StaticImportSchemaDefinition>;
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

export function readK3cloudListConnectionConfigsResponse(value: unknown): Array<K3CloudConnectionConfigResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeK3CloudConnectionConfigResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isK3CloudConnectionConfigResponse(item15)))) {
    throw new Error('client.invalid_k3cloud_list_connection_configs_response');
  }
  return normalizedValue as Array<K3CloudConnectionConfigResponse>;
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

export function readObservabilityListCachePoliciesResponse(value: unknown): Array<CachePolicySummary> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeCachePolicySummaryIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isCachePolicySummary(item15)))) {
    throw new Error('client.invalid_observability_list_cache_policies_response');
  }
  return normalizedValue as Array<CachePolicySummary>;
}

export function readObservabilityListLogFilesResponse(value: unknown): Array<LogFileSummary> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeLogFileSummaryIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isLogFileSummary(item15)))) {
    throw new Error('client.invalid_observability_list_log_files_response');
  }
  return normalizedValue as Array<LogFileSummary>;
}

export function readObservabilityListServerInstancesResponse(value: unknown): Array<ServerInstanceCatalogEntry> {
  if (!(Array.isArray(value) && value.every(item5 => isServerInstanceCatalogEntry(item5)))) {
    throw new Error('client.invalid_observability_list_server_instances_response');
  }
  return value as Array<ServerInstanceCatalogEntry>;
}

export function readPrintingListFormSchemasResponse(value: unknown): Array<PrintingFormSchemaDefinition> {
  if (!(Array.isArray(value) && value.every(item5 => isPrintingFormSchemaDefinition(item5)))) {
    throw new Error('client.invalid_printing_list_form_schemas_response');
  }
  return value as Array<PrintingFormSchemaDefinition>;
}

export function readPrintingListTemplatesResponse(value: unknown): Array<PrintingTemplateResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizePrintingTemplateResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isPrintingTemplateResponse(item15)))) {
    throw new Error('client.invalid_printing_list_templates_response');
  }
  return normalizedValue as Array<PrintingTemplateResponse>;
}

export function readPrintingListTemplateVersionsResponse(value: unknown): Array<PrintingTemplateVersionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizePrintingTemplateVersionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isPrintingTemplateVersionResponse(item15)))) {
    throw new Error('client.invalid_printing_list_template_versions_response');
  }
  return normalizedValue as Array<PrintingTemplateVersionResponse>;
}

export function readRegionsGetAdministrativeRegionTreeResponse(value: unknown): Array<AdministrativeRegionTreeNodeResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeAdministrativeRegionTreeNodeResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isAdministrativeRegionTreeNodeResponse(item15)))) {
    throw new Error('client.invalid_regions_get_administrative_region_tree_response');
  }
  return normalizedValue as Array<AdministrativeRegionTreeNodeResponse>;
}

export function readRegionsListAdministrativeRegionChildrenResponse(value: unknown): Array<AdministrativeRegionChildResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeAdministrativeRegionChildResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isAdministrativeRegionChildResponse(item15)))) {
    throw new Error('client.invalid_regions_list_administrative_region_children_response');
  }
  return normalizedValue as Array<AdministrativeRegionChildResponse>;
}

export function readReportingDeleteDataSourceResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_reporting_delete_data_source_response');
  }
  return value as boolean;
}

export function readReportingDeleteDefinitionResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_reporting_delete_definition_response');
  }
  return value as boolean;
}

export function readReportingDeleteGroupResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_reporting_delete_group_response');
  }
  return value as boolean;
}

export function readReportingListDefinitionsResponse(value: unknown): Array<ReportingDefinitionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeReportingDefinitionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isReportingDefinitionResponse(item15)))) {
    throw new Error('client.invalid_reporting_list_definitions_response');
  }
  return normalizedValue as Array<ReportingDefinitionResponse>;
}

export function readReportingListDefinitionVersionsResponse(value: unknown): Array<ReportingDefinitionVersionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeReportingDefinitionVersionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isReportingDefinitionVersionResponse(item15)))) {
    throw new Error('client.invalid_reporting_list_definition_versions_response');
  }
  return normalizedValue as Array<ReportingDefinitionVersionResponse>;
}

export function readReportingListGroupsResponse(value: unknown): Array<ReportingGroupResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeReportingGroupResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isReportingGroupResponse(item15)))) {
    throw new Error('client.invalid_reporting_list_groups_response');
  }
  return normalizedValue as Array<ReportingGroupResponse>;
}

export function readReportingListQueryPortsResponse(value: unknown): Array<ReportingQueryPortDefinition> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeReportingQueryPortDefinitionIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isReportingQueryPortDefinition(item15)))) {
    throw new Error('client.invalid_reporting_list_query_ports_response');
  }
  return normalizedValue as Array<ReportingQueryPortDefinition>;
}

export function readSettingsBatchUpdateHostConfigEntryValuesResponse(value: unknown): boolean {
  if (!(typeof value === 'boolean')) {
    throw new Error('client.invalid_settings_batch_update_host_config_entry_values_response');
  }
  return value as boolean;
}

export function readSettingsListAllHostConfigEntriesResponse(value: unknown): Array<ConfigEntryResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeConfigEntryResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isConfigEntryResponse(item15)))) {
    throw new Error('client.invalid_settings_list_all_host_config_entries_response');
  }
  return normalizedValue as Array<ConfigEntryResponse>;
}

export function readSettingsListAllHostDictTypesResponse(value: unknown): Array<DictTypeResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeDictTypeResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isDictTypeResponse(item15)))) {
    throw new Error('client.invalid_settings_list_all_host_dict_types_response');
  }
  return normalizedValue as Array<DictTypeResponse>;
}

export function readSettingsListAllTenantDictTypesResponse(value: unknown): Array<DictTypeResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeDictTypeResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isDictTypeResponse(item15)))) {
    throw new Error('client.invalid_settings_list_all_tenant_dict_types_response');
  }
  return normalizedValue as Array<DictTypeResponse>;
}

export function readSettingsListHostConfigEntryGroupsResponse(value: unknown): Array<string> {
  if (!(Array.isArray(value) && value.every(item5 => typeof item5 === 'string'))) {
    throw new Error('client.invalid_settings_list_host_config_entry_groups_response');
  }
  return value as Array<string>;
}

export function readSettingsListHostDictItemsByTypeCodeResponse(value: unknown): Array<DictItemResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeDictItemResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isDictItemResponse(item15)))) {
    throw new Error('client.invalid_settings_list_host_dict_items_by_type_code_response');
  }
  return normalizedValue as Array<DictItemResponse>;
}

export function readSettingsListHostEnumCatalogsResponse(value: unknown): Array<EnumCatalogSummary> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeEnumCatalogSummaryIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isEnumCatalogSummary(item15)))) {
    throw new Error('client.invalid_settings_list_host_enum_catalogs_response');
  }
  return normalizedValue as Array<EnumCatalogSummary>;
}

export function readSettingsListTenantDictItemsByTypeCodeResponse(value: unknown): Array<DictItemResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeDictItemResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isDictItemResponse(item15)))) {
    throw new Error('client.invalid_settings_list_tenant_dict_items_by_type_code_response');
  }
  return normalizedValue as Array<DictItemResponse>;
}

export function readWorkflowListDefinitionsResponse(value: unknown): Array<WorkflowDefinitionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeWorkflowDefinitionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isWorkflowDefinitionResponse(item15)))) {
    throw new Error('client.invalid_workflow_list_definitions_response');
  }
  return normalizedValue as Array<WorkflowDefinitionResponse>;
}

export function readWorkflowListDefinitionVersionsResponse(value: unknown): Array<WorkflowDefinitionVersionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeWorkflowDefinitionVersionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isWorkflowDefinitionVersionResponse(item15)))) {
    throw new Error('client.invalid_workflow_list_definition_versions_response');
  }
  return normalizedValue as Array<WorkflowDefinitionVersionResponse>;
}

export function readWorkflowListFormsResponse(value: unknown): Array<WorkflowFormResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeWorkflowFormResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isWorkflowFormResponse(item15)))) {
    throw new Error('client.invalid_workflow_list_forms_response');
  }
  return normalizedValue as Array<WorkflowFormResponse>;
}

export function readWorkflowListFormVersionsResponse(value: unknown): Array<WorkflowFormVersionResponse> {
  const normalizedValue = (Array.isArray(value) ? value.map((item5: unknown) => normalizeWorkflowFormVersionResponseIntegerJson(item5)) : value);
  if (!(Array.isArray(normalizedValue) && normalizedValue.every(item15 => isWorkflowFormVersionResponse(item15)))) {
    throw new Error('client.invalid_workflow_list_form_versions_response');
  }
  return normalizedValue as Array<WorkflowFormVersionResponse>;
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

export function readWorkflowListTodoReturnTargetsResponse(value: unknown): Array<WorkflowTodoReturnTargetResponse> {
  if (!(Array.isArray(value) && value.every(item5 => isWorkflowTodoReturnTargetResponse(item5)))) {
    throw new Error('client.invalid_workflow_list_todo_return_targets_response');
  }
  return value as Array<WorkflowTodoReturnTargetResponse>;
}

function normalizeAccessLogCursorPageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAccessLogResponseIntegerJson(item14)) : value["items"]) } : {}) } : value);
}

function normalizeAccessLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "durationMs") ? { ["durationMs"]: normalizeWireInteger(value["durationMs"]) } : {}), ...(Object.hasOwn(value, "statusCode") ? { ["statusCode"]: normalizeWireInteger(value["statusCode"]) } : {}) } : value);
}

function normalizeActWorkflowTodoRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeAdministrativeRegionChildResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}) } : value);
}

function normalizeAdministrativeRegionDatasetManifestResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "recordCount") ? { ["recordCount"]: normalizeWireInteger(value["recordCount"]) } : {}) } : value);
}

function normalizeAdministrativeRegionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAdministrativeRegionTreeNodeResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "children") ? { ["children"]: (Array.isArray(value["children"]) ? value["children"].map((item17: unknown) => normalizeAdministrativeRegionTreeNodeResponseIntegerJson(item17)) : value["children"]) } : {}), ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}) } : value);
}

function normalizeAiAgentApprovalResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "toolVersion") ? { ["toolVersion"]: normalizeWireInteger(value["toolVersion"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiAgentDelegationResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiAgentRunResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "definitionVersion") ? { ["definitionVersion"]: normalizeWireInteger(value["definitionVersion"]) } : {}) } : value);
}

function normalizeAiAgentToolCallListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "durationMs") ? { ["durationMs"]: normalizeWireInteger(value["durationMs"]) } : {}) } : value);
}

function normalizeAiChatMessageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "completionTokens") ? { ["completionTokens"]: normalizeWireInteger(value["completionTokens"]) } : {}), ...(Object.hasOwn(value, "promptTokens") ? { ["promptTokens"]: normalizeWireInteger(value["promptTokens"]) } : {}) } : value);
}

function normalizeAiChatSessionListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "messageCount") ? { ["messageCount"]: normalizeWireInteger(value["messageCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiChatSessionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "messages") ? { ["messages"]: (Array.isArray(value["messages"]) ? value["messages"].map((item17: unknown) => normalizeAiChatMessageResponseIntegerJson(item17)) : value["messages"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiMcpRemoteConnectionListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiMcpRemoteConnectionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiMcpRemoteToolApprovalItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "toolVersion") ? { ["toolVersion"]: normalizeWireInteger(value["toolVersion"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiModelConfigListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiModelConfigResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiTenantQuotaListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "monthlyRequestLimit") ? { ["monthlyRequestLimit"]: normalizeWireInteger(value["monthlyRequestLimit"]) } : {}), ...(Object.hasOwn(value, "monthlyTokenLimit") ? { ["monthlyTokenLimit"]: normalizeWireInteger(value["monthlyTokenLimit"]) } : {}), ...(Object.hasOwn(value, "usedRequestsThisMonth") ? { ["usedRequestsThisMonth"]: normalizeWireInteger(value["usedRequestsThisMonth"]) } : {}), ...(Object.hasOwn(value, "usedTokensThisMonth") ? { ["usedTokensThisMonth"]: normalizeWireInteger(value["usedTokensThisMonth"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAiTenantQuotaResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "monthlyRequestLimit") ? { ["monthlyRequestLimit"]: normalizeWireInteger(value["monthlyRequestLimit"]) } : {}), ...(Object.hasOwn(value, "monthlyTokenLimit") ? { ["monthlyTokenLimit"]: normalizeWireInteger(value["monthlyTokenLimit"]) } : {}), ...(Object.hasOwn(value, "usedRequestsThisMonth") ? { ["usedRequestsThisMonth"]: normalizeWireInteger(value["usedRequestsThisMonth"]) } : {}), ...(Object.hasOwn(value, "usedTokensThisMonth") ? { ["usedTokensThisMonth"]: normalizeWireInteger(value["usedTokensThisMonth"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAssignHostTenantPackageRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAssignOrganizationPositionLevelRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAssignOrganizationPositionUnitRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeAuthorizationTreeActionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "order") ? { ["order"]: normalizeWireInteger(value["order"]) } : {}) } : value);
}

function normalizeAuthorizationTreeModuleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "order") ? { ["order"]: normalizeWireInteger(value["order"]) } : {}), ...(Object.hasOwn(value, "pages") ? { ["pages"]: (Array.isArray(value["pages"]) ? value["pages"].map((item14: unknown) => normalizeAuthorizationTreePageResponseIntegerJson(item14)) : value["pages"]) } : {}) } : value);
}

function normalizeAuthorizationTreePageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "actions") ? { ["actions"]: (Array.isArray(value["actions"]) ? value["actions"].map((item16: unknown) => normalizeAuthorizationTreeActionResponseIntegerJson(item16)) : value["actions"]) } : {}), ...(Object.hasOwn(value, "children") ? { ["children"]: (Array.isArray(value["children"]) ? value["children"].map((item17: unknown) => normalizeAuthorizationTreePageResponseIntegerJson(item17)) : value["children"]) } : {}), ...(Object.hasOwn(value, "order") ? { ["order"]: normalizeWireInteger(value["order"]) } : {}) } : value);
}

function normalizeBatchChangeHostJobScheduleStateItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeBatchChangeHostJobScheduleStateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeBatchChangeHostJobScheduleStateItemIntegerJson(item14)) : value["items"]) } : {}) } : value);
}

function normalizeBatchChangeHostJobScheduleStateResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "results") ? { ["results"]: (Array.isArray(value["results"]) ? value["results"].map((item16: unknown) => normalizeBatchChangeHostJobScheduleStateResultItemIntegerJson(item16)) : value["results"]) } : {}), ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeBatchChangeHostJobScheduleStateResultItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "schedule") ? { ["schedule"]: normalizeIntegerUnion(value["schedule"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostJobScheduleResponse(value), normalize: (value: unknown) => normalizeHostJobScheduleResponseIntegerJson(value) }]) } : {}) } : value);
}

function normalizeBatchCreateHostDocumentShareItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "share") ? { ["share"]: normalizeIntegerUnion(value["share"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostDocumentShareResponse(value), normalize: (value: unknown) => normalizeHostDocumentShareResponseIntegerJson(value) }]) } : {}) } : value);
}

function normalizeBatchCreateHostDocumentSharesRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "maxAccessCount") ? { ["maxAccessCount"]: normalizeWireInteger(value["maxAccessCount"]) } : {}), ...(Object.hasOwn(value, "validDays") ? { ["validDays"]: normalizeWireInteger(value["validDays"]) } : {}) } : value);
}

function normalizeBatchCreateHostDocumentSharesResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "results") ? { ["results"]: (Array.isArray(value["results"]) ? value["results"].map((item16: unknown) => normalizeBatchCreateHostDocumentShareItemIntegerJson(item16)) : value["results"]) } : {}), ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeBatchDeleteHostFilesResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeBatchHostUserStatusResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeBatchUploadHostFileItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "file") ? { ["file"]: normalizeIntegerUnion(value["file"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostFileResponse(value), normalize: (value: unknown) => normalizeHostFileResponseIntegerJson(value) }]) } : {}) } : value);
}

function normalizeBatchUploadHostFilesResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "results") ? { ["results"]: (Array.isArray(value["results"]) ? value["results"].map((item16: unknown) => normalizeBatchUploadHostFileItemIntegerJson(item16)) : value["results"]) } : {}), ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeCachePolicySummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "l1DurationSeconds") ? { ["l1DurationSeconds"]: normalizeWireInteger(value["l1DurationSeconds"]) } : {}), ...(Object.hasOwn(value, "l2DurationSeconds") ? { ["l2DurationSeconds"]: normalizeWireInteger(value["l2DurationSeconds"]) } : {}) } : value);
}

function normalizeCancelWorkflowInstanceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeChangeHostJobScheduleStateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeChangePersonalScheduleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeChangeSerialNumberRuleStatusRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeCodeGenerationCatalogColumnListResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "columns") ? { ["columns"]: (Array.isArray(value["columns"]) ? value["columns"].map((item16: unknown) => normalizeCodeGenerationPreviewColumnRequestIntegerJson(item16)) : value["columns"]) } : {}) } : value);
}

function normalizeCodeGenerationCatalogColumnSyncRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "columns") ? { ["columns"]: (Array.isArray(value["columns"]) ? value["columns"].map((item16: unknown) => normalizeCodeGenerationPreviewColumnRequestIntegerJson(item16)) : value["columns"]) } : {}) } : value);
}

function normalizeCodeGenerationCatalogColumnSyncResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "columns") ? { ["columns"]: (Array.isArray(value["columns"]) ? value["columns"].map((item16: unknown) => normalizeCodeGenerationPreviewColumnRequestIntegerJson(item16)) : value["columns"]) } : {}) } : value);
}

function normalizeCodeGenerationCatalogMetadataColumnResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "maxLength") ? { ["maxLength"]: normalizeWireInteger(value["maxLength"]) } : {}), ...(Object.hasOwn(value, "numericPrecision") ? { ["numericPrecision"]: normalizeWireInteger(value["numericPrecision"]) } : {}), ...(Object.hasOwn(value, "numericScale") ? { ["numericScale"]: normalizeWireInteger(value["numericScale"]) } : {}), ...(Object.hasOwn(value, "ordinalPosition") ? { ["ordinalPosition"]: normalizeWireInteger(value["ordinalPosition"]) } : {}) } : value);
}

function normalizeCodeGenerationCatalogMetadataResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "columns") ? { ["columns"]: (Array.isArray(value["columns"]) ? value["columns"].map((item16: unknown) => normalizeCodeGenerationCatalogMetadataColumnResponseIntegerJson(item16)) : value["columns"]) } : {}) } : value);
}

function normalizeCodeGenerationPreviewColumnRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "maxLength") ? { ["maxLength"]: normalizeWireInteger(value["maxLength"]) } : {}), ...(Object.hasOwn(value, "numericPrecision") ? { ["numericPrecision"]: normalizeWireInteger(value["numericPrecision"]) } : {}), ...(Object.hasOwn(value, "numericScale") ? { ["numericScale"]: normalizeWireInteger(value["numericScale"]) } : {}), ...(Object.hasOwn(value, "ui") ? { ["ui"]: normalizeIntegerUnion(value["ui"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isCodeGenerationPreviewColumnUiRequest(value), normalize: (value: unknown) => value }]) } : {}) } : value);
}

function normalizeCodeGenerationPreviewRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "columns") ? { ["columns"]: (Array.isArray(value["columns"]) ? value["columns"].map((item16: unknown) => normalizeCodeGenerationPreviewColumnRequestIntegerJson(item16)) : value["columns"]) } : {}), ...(Object.hasOwn(value, "entityCapabilities") ? { ["entityCapabilities"]: normalizeIntegerUnion(value["entityCapabilities"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isCodeGenerationEntityCapabilitiesRequest(value), normalize: (value: unknown) => value }]) } : {}) } : value);
}

function normalizeCodeGenerationRunApplyResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "artifactCount") ? { ["artifactCount"]: normalizeWireInteger(value["artifactCount"]) } : {}), ...(Object.hasOwn(value, "changedArtifactCount") ? { ["changedArtifactCount"]: normalizeWireInteger(value["changedArtifactCount"]) } : {}) } : value);
}

function normalizeCodeGenerationRunPreviewRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "schema") ? { ["schema"]: normalizeIntegerUnion(value["schema"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isCodeGenerationPreviewRequest(value), normalize: (value: unknown) => normalizeCodeGenerationPreviewRequestIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "templateVersion") ? { ["templateVersion"]: normalizeWireInteger(value["templateVersion"]) } : {}) } : value);
}

function normalizeCodeGenerationRunResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "artifactCount") ? { ["artifactCount"]: normalizeWireInteger(value["artifactCount"]) } : {}), ...(Object.hasOwn(value, "templateVersion") ? { ["templateVersion"]: normalizeWireInteger(value["templateVersion"]) } : {}) } : value);
}

function normalizeCodeGenerationRunRollbackChainResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "rollbacks") ? { ["rollbacks"]: (Array.isArray(value["rollbacks"]) ? value["rollbacks"].map((item18: unknown) => normalizeCodeGenerationRunRollbackResponseIntegerJson(item18)) : value["rollbacks"]) } : {}) } : value);
}

function normalizeCodeGenerationRunRollbackResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "artifactCount") ? { ["artifactCount"]: normalizeWireInteger(value["artifactCount"]) } : {}), ...(Object.hasOwn(value, "changedArtifactCount") ? { ["changedArtifactCount"]: normalizeWireInteger(value["changedArtifactCount"]) } : {}) } : value);
}

function normalizeCodeGenerationTemplateResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "schema") ? { ["schema"]: normalizeCodeGenerationPreviewRequestIntegerJson(value["schema"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeConfigEntryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeConfirmOcrIdCardTaskRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeCreateAdministrativeRegionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}) } : value);
}

function normalizeCreateAiAgentApprovalRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "toolVersion") ? { ["toolVersion"]: normalizeWireInteger(value["toolVersion"]) } : {}) } : value);
}

function normalizeCreateAiAgentRunRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "inputTokenLimit") ? { ["inputTokenLimit"]: normalizeWireInteger(value["inputTokenLimit"]) } : {}), ...(Object.hasOwn(value, "outputTokenLimit") ? { ["outputTokenLimit"]: normalizeWireInteger(value["outputTokenLimit"]) } : {}) } : value);
}

function normalizeCreateCodeGenerationTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "schema") ? { ["schema"]: normalizeCodeGenerationPreviewRequestIntegerJson(value["schema"]) } : {}) } : value);
}

function normalizeCreateConfigEntryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateDictItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateDictTypeRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateHostDocumentCategoryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}) } : value);
}

function normalizeCreateHostDocumentItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sort") ? { ["sort"]: normalizeWireInteger(value["sort"]) } : {}) } : value);
}

function normalizeCreateHostDocumentShareRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "maxAccessCount") ? { ["maxAccessCount"]: normalizeWireInteger(value["maxAccessCount"]) } : {}), ...(Object.hasOwn(value, "validDays") ? { ["validDays"]: normalizeWireInteger(value["validDays"]) } : {}) } : value);
}

function normalizeCreateHostFolderRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateHostJobDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "args") ? { ["args"]: normalizeIntegerUnion(value["args"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHttpJobArgs(value), normalize: (value: unknown) => normalizeHttpJobArgsIntegerJson(value) }]) } : {}) } : value);
}

function normalizeCreateHostMenuRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateHostUserRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profile") ? { ["profile"]: normalizeIntegerUnion(value["profile"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostUserProfileWriteRequest(value), normalize: (value: unknown) => normalizeHostUserProfileWriteRequestIntegerJson(value) }]) } : {}) } : value);
}

function normalizeCreateK3CloudConnectionConfigRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "lcid") ? { ["lcid"]: normalizeWireInteger(value["lcid"]) } : {}) } : value);
}

function normalizeCreateNotificationBindingRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "targets") ? { ["targets"]: (Array.isArray(value["targets"]) ? value["targets"].map((item16: unknown) => normalizeNotificationBindingTargetInputIntegerJson(item16)) : value["targets"]) } : {}) } : value);
}

function normalizeCreateNotificationTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "parameterSchema") ? { ["parameterSchema"]: normalizeNotificationTemplateParameterSchemaIntegerJson(value["parameterSchema"]) } : {}) } : value);
}

function normalizeCreateOrganizationPositionLevelRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateOrganizationPositionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreateOrganizationUnitRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeCreatePaymentOrderRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "amountMinor") ? { ["amountMinor"]: normalizeWireInteger(value["amountMinor"]) } : {}) } : value);
}

function normalizeCreatePaymentRefundRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "amountMinor") ? { ["amountMinor"]: normalizeWireInteger(value["amountMinor"]) } : {}) } : value);
}

function normalizeCreateReportingDataSourceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "port") ? { ["port"]: normalizeWireInteger(value["port"]) } : {}) } : value);
}

function normalizeCreateReportingExportTaskRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeCreateReportingGroupRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}) } : value);
}

function normalizeCreateSerialNumberRuleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "maximumValue") ? { ["maximumValue"]: normalizeWireInteger(value["maximumValue"]) } : {}), ...(Object.hasOwn(value, "minimumValue") ? { ["minimumValue"]: normalizeWireInteger(value["minimumValue"]) } : {}) } : value);
}

function normalizeCreateWorkflowDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draft") ? { ["draft"]: normalizeWorkflowDefinitionDraftIntegerJson(value["draft"]) } : {}) } : value);
}

function normalizeCreateWorkflowFormRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draft") ? { ["draft"]: normalizeWorkflowFormSchemaIntegerJson(value["draft"]) } : {}) } : value);
}

function normalizeCurrentUserResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profileVersion") ? { ["profileVersion"]: normalizeWireInteger(value["profileVersion"]) } : {}) } : value);
}

function normalizeDataApprovalRequestResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "applicationAttemptCount") ? { ["applicationAttemptCount"]: normalizeWireInteger(value["applicationAttemptCount"]) } : {}), ...(Object.hasOwn(value, "recoveryAttemptCount") ? { ["recoveryAttemptCount"]: normalizeWireInteger(value["recoveryAttemptCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}), ...(Object.hasOwn(value, "workflowRevision") ? { ["workflowRevision"]: normalizeWireInteger(value["workflowRevision"]) } : {}) } : value);
}

function normalizeDataApprovalScenarioResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDecideAiAgentApprovalRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedVersion") ? { ["expectedVersion"]: normalizeWireInteger(value["expectedVersion"]) } : {}) } : value);
}

function normalizeDeleteAdministrativeRegionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteCodeGenerationTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteConfigEntryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteDictItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteDictTypeRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteEnterpriseRequestRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteHostDocumentCategoryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteHostDocumentItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteHostDocumentTagRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteHostDocumentVersionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteHostFolderRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeDeleteHostJobDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDeleteHostReleaseNoteRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDiagnosticPolicyResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "activeRules") ? { ["activeRules"]: (Array.isArray(value["activeRules"]) ? value["activeRules"].map((item20: unknown) => normalizeDiagnosticPolicyRuleResponseIntegerJson(item20)) : value["activeRules"]) } : {}), ...(Object.hasOwn(value, "configEntryVersion") ? { ["configEntryVersion"]: normalizeWireInteger(value["configEntryVersion"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDiagnosticPolicyRuleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "bestEffortCapacityOverride") ? { ["bestEffortCapacityOverride"]: normalizeWireInteger(value["bestEffortCapacityOverride"]) } : {}), ...(Object.hasOwn(value, "maxRequestPayloadBytesOverride") ? { ["maxRequestPayloadBytesOverride"]: normalizeWireInteger(value["maxRequestPayloadBytesOverride"]) } : {}), ...(Object.hasOwn(value, "maxResponsePayloadBytesOverride") ? { ["maxResponsePayloadBytesOverride"]: normalizeWireInteger(value["maxResponsePayloadBytesOverride"]) } : {}) } : value);
}

function normalizeDiagnosticPolicyRuleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "bestEffortCapacityOverride") ? { ["bestEffortCapacityOverride"]: normalizeWireInteger(value["bestEffortCapacityOverride"]) } : {}), ...(Object.hasOwn(value, "maxRequestPayloadBytesOverride") ? { ["maxRequestPayloadBytesOverride"]: normalizeWireInteger(value["maxRequestPayloadBytesOverride"]) } : {}), ...(Object.hasOwn(value, "maxResponsePayloadBytesOverride") ? { ["maxResponsePayloadBytesOverride"]: normalizeWireInteger(value["maxResponsePayloadBytesOverride"]) } : {}) } : value);
}

function normalizeDictItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDictTypeResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeDisableHostJobDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeEnterpriseRequestResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeEnumCatalogDetailIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "members") ? { ["members"]: (Array.isArray(value["members"]) ? value["members"].map((item16: unknown) => normalizeEnumCatalogMemberIntegerJson(item16)) : value["members"]) } : {}) } : value);
}

function normalizeEnumCatalogDictGenerationItemPreviewIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeEnumCatalogDictGenerationPreviewIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeEnumCatalogDictGenerationItemPreviewIntegerJson(item14)) : value["items"]) } : {}) } : value);
}

function normalizeEnumCatalogDictGenerationResultIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeEnumCatalogDictGenerationItemPreviewIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "itemsConflicted") ? { ["itemsConflicted"]: normalizeWireInteger(value["itemsConflicted"]) } : {}), ...(Object.hasOwn(value, "itemsCreated") ? { ["itemsCreated"]: normalizeWireInteger(value["itemsCreated"]) } : {}), ...(Object.hasOwn(value, "itemsInvalid") ? { ["itemsInvalid"]: normalizeWireInteger(value["itemsInvalid"]) } : {}), ...(Object.hasOwn(value, "itemsSkipped") ? { ["itemsSkipped"]: normalizeWireInteger(value["itemsSkipped"]) } : {}) } : value);
}

function normalizeEnumCatalogMemberIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeEnumCatalogSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "memberCount") ? { ["memberCount"]: normalizeWireInteger(value["memberCount"]) } : {}) } : value);
}

function normalizeExecuteReportingDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeGoViewProjectPreviewResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeGoViewProjectResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "latestPublishedVersionNumber") ? { ["latestPublishedVersionNumber"]: normalizeWireInteger(value["latestPublishedVersionNumber"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeGoViewProjectVersionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeHostAnnouncementReadStatsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "eligibleRecipientCount") ? { ["eligibleRecipientCount"]: normalizeWireInteger(value["eligibleRecipientCount"]) } : {}), ...(Object.hasOwn(value, "readCount") ? { ["readCount"]: normalizeWireInteger(value["readCount"]) } : {}), ...(Object.hasOwn(value, "unreadCount") ? { ["unreadCount"]: normalizeWireInteger(value["unreadCount"]) } : {}) } : value);
}

function normalizeHostAnnouncementResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostAnnouncementUnreadCountResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "unreadCount") ? { ["unreadCount"]: normalizeWireInteger(value["unreadCount"]) } : {}) } : value);
}

function normalizeHostDashboardBusinessEntryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "count") ? { ["count"]: normalizeWireInteger(value["count"]) } : {}) } : value);
}

function normalizeHostDashboardSummaryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "accessTrafficTrend") ? { ["accessTrafficTrend"]: normalizeIntegerUnion(value["accessTrafficTrend"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostDashboardTrafficTrendResponse(value), normalize: (value: unknown) => normalizeHostDashboardTrafficTrendResponseIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "activeTenantCount") ? { ["activeTenantCount"]: normalizeWireInteger(value["activeTenantCount"]) } : {}), ...(Object.hasOwn(value, "businessEntries") ? { ["businessEntries"]: (Array.isArray(value["businessEntries"]) ? value["businessEntries"].map((item24: unknown) => normalizeHostDashboardBusinessEntryResponseIntegerJson(item24)) : value["businessEntries"]) } : {}), ...(Object.hasOwn(value, "onlineSessionCount") ? { ["onlineSessionCount"]: normalizeWireInteger(value["onlineSessionCount"]) } : {}), ...(Object.hasOwn(value, "todayRequestCount") ? { ["todayRequestCount"]: normalizeWireInteger(value["todayRequestCount"]) } : {}) } : value);
}

function normalizeHostDashboardTrafficTrendBucketResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "errorCount") ? { ["errorCount"]: normalizeWireInteger(value["errorCount"]) } : {}), ...(Object.hasOwn(value, "eventCount") ? { ["eventCount"]: normalizeWireInteger(value["eventCount"]) } : {}) } : value);
}

function normalizeHostDashboardTrafficTrendResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "buckets") ? { ["buckets"]: (Array.isArray(value["buckets"]) ? value["buckets"].map((item16: unknown) => normalizeHostDashboardTrafficTrendBucketResponseIntegerJson(item16)) : value["buckets"]) } : {}), ...(Object.hasOwn(value, "bucketSizeMinutes") ? { ["bucketSizeMinutes"]: normalizeWireInteger(value["bucketSizeMinutes"]) } : {}) } : value);
}

function normalizeHostDocumentCategoryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostDocumentItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "accessCount") ? { ["accessCount"]: normalizeWireInteger(value["accessCount"]) } : {}), ...(Object.hasOwn(value, "currentVersion") ? { ["currentVersion"]: normalizeIntegerUnion(value["currentVersion"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostDocumentVersionResponse(value), normalize: (value: unknown) => normalizeHostDocumentVersionResponseIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "sizeKb") ? { ["sizeKb"]: normalizeWireInteger(value["sizeKb"]) } : {}), ...(Object.hasOwn(value, "sort") ? { ["sort"]: normalizeWireInteger(value["sort"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostDocumentPreviewTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostDocumentShareAccessResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "accessCountRemaining") ? { ["accessCountRemaining"]: normalizeWireInteger(value["accessCountRemaining"]) } : {}), ...(Object.hasOwn(value, "fileSizeBytes") ? { ["fileSizeBytes"]: normalizeWireInteger(value["fileSizeBytes"]) } : {}) } : value);
}

function normalizeHostDocumentShareResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "accessCount") ? { ["accessCount"]: normalizeWireInteger(value["accessCount"]) } : {}), ...(Object.hasOwn(value, "maxAccessCount") ? { ["maxAccessCount"]: normalizeWireInteger(value["maxAccessCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostDocumentStatisticsCategoryItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "count") ? { ["count"]: normalizeWireInteger(value["count"]) } : {}) } : value);
}

function normalizeHostDocumentStatisticsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "byCategory") ? { ["byCategory"]: (Array.isArray(value["byCategory"]) ? value["byCategory"].map((item19: unknown) => normalizeHostDocumentStatisticsCategoryItemIntegerJson(item19)) : value["byCategory"]) } : {}), ...(Object.hasOwn(value, "byType") ? { ["byType"]: (Array.isArray(value["byType"]) ? value["byType"].map((item15: unknown) => normalizeHostDocumentStatisticsTypeItemIntegerJson(item15)) : value["byType"]) } : {}), ...(Object.hasOwn(value, "recycleBinCount") ? { ["recycleBinCount"]: normalizeWireInteger(value["recycleBinCount"]) } : {}), ...(Object.hasOwn(value, "shareCount") ? { ["shareCount"]: normalizeWireInteger(value["shareCount"]) } : {}), ...(Object.hasOwn(value, "summary") ? { ["summary"]: normalizeHostDocumentStatisticsSummaryResponseIntegerJson(value["summary"]) } : {}), ...(Object.hasOwn(value, "todayAccessCount") ? { ["todayAccessCount"]: normalizeWireInteger(value["todayAccessCount"]) } : {}), ...(Object.hasOwn(value, "todayCreatedCount") ? { ["todayCreatedCount"]: normalizeWireInteger(value["todayCreatedCount"]) } : {}), ...(Object.hasOwn(value, "todayDownloadCount") ? { ["todayDownloadCount"]: normalizeWireInteger(value["todayDownloadCount"]) } : {}) } : value);
}

function normalizeHostDocumentStatisticsSummaryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "totalItems") ? { ["totalItems"]: normalizeWireInteger(value["totalItems"]) } : {}), ...(Object.hasOwn(value, "totalSizeKb") ? { ["totalSizeKb"]: normalizeWireInteger(value["totalSizeKb"]) } : {}), ...(Object.hasOwn(value, "totalVersions") ? { ["totalVersions"]: normalizeWireInteger(value["totalVersions"]) } : {}) } : value);
}

function normalizeHostDocumentStatisticsTypeItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "count") ? { ["count"]: normalizeWireInteger(value["count"]) } : {}), ...(Object.hasOwn(value, "totalSizeKb") ? { ["totalSizeKb"]: normalizeWireInteger(value["totalSizeKb"]) } : {}) } : value);
}

function normalizeHostDocumentTagResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "useCount") ? { ["useCount"]: normalizeWireInteger(value["useCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostDocumentVersionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sizeBytes") ? { ["sizeBytes"]: normalizeWireInteger(value["sizeBytes"]) } : {}), ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeHostDocumentVersionRetentionSettingsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "batchSize") ? { ["batchSize"]: normalizeWireInteger(value["batchSize"]) } : {}), ...(Object.hasOwn(value, "maximumRetainedHistoryVersions") ? { ["maximumRetainedHistoryVersions"]: normalizeWireInteger(value["maximumRetainedHistoryVersions"]) } : {}), ...(Object.hasOwn(value, "minimumRetainedVersionsPerItem") ? { ["minimumRetainedVersionsPerItem"]: normalizeWireInteger(value["minimumRetainedVersionsPerItem"]) } : {}), ...(Object.hasOwn(value, "pollSeconds") ? { ["pollSeconds"]: normalizeWireInteger(value["pollSeconds"]) } : {}) } : value);
}

function normalizeHostFileResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}), ...(Object.hasOwn(value, "sizeBytes") ? { ["sizeBytes"]: normalizeWireInteger(value["sizeBytes"]) } : {}) } : value);
}

function normalizeHostFolderResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeHostFolderTreeNodeIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "children") ? { ["children"]: (Array.isArray(value["children"]) ? value["children"].map((item17: unknown) => normalizeHostFolderTreeNodeIntegerJson(item17)) : value["children"]) } : {}), ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeHostJobDefinitionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "args") ? { ["args"]: normalizeIntegerUnion(value["args"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHttpJobArgs(value), normalize: (value: unknown) => normalizeHttpJobArgsIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostJobExecutionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "attemptCount") ? { ["attemptCount"]: normalizeWireInteger(value["attemptCount"]) } : {}) } : value);
}

function normalizeHostJobHealthBacklogSnapshotIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "dueRetryCount") ? { ["dueRetryCount"]: normalizeWireInteger(value["dueRetryCount"]) } : {}), ...(Object.hasOwn(value, "pendingCount") ? { ["pendingCount"]: normalizeWireInteger(value["pendingCount"]) } : {}) } : value);
}

function normalizeHostJobHealthResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "backlog") ? { ["backlog"]: normalizeHostJobHealthBacklogSnapshotIntegerJson(value["backlog"]) } : {}) } : value);
}

function normalizeHostJobScheduleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "numberOfErrors") ? { ["numberOfErrors"]: normalizeWireInteger(value["numberOfErrors"]) } : {}), ...(Object.hasOwn(value, "numberOfRuns") ? { ["numberOfRuns"]: normalizeWireInteger(value["numberOfRuns"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostMenuResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostNavigationCatalogSyncResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "created") ? { ["created"]: normalizeWireInteger(value["created"]) } : {}), ...(Object.hasOwn(value, "reparented") ? { ["reparented"]: normalizeWireInteger(value["reparented"]) } : {}), ...(Object.hasOwn(value, "skipped") ? { ["skipped"]: normalizeWireInteger(value["skipped"]) } : {}) } : value);
}

function normalizeHostReleaseNoteResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}), ...(Object.hasOwn(value, "versionSortKey") ? { ["versionSortKey"]: normalizeWireInteger(value["versionSortKey"]) } : {}) } : value);
}

function normalizeHostRoleDataScopeResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostRoleFieldGrantsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostRoleMembersAssignmentResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostRoleMembersPageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostRoleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostTenantAdministratorsPageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizeHostTenantMembersPageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizeHostUserManagementOrganizationReferenceResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "positions") ? { ["positions"]: (Array.isArray(value["positions"]) ? value["positions"].map((item18: unknown) => normalizeOrganizationPositionResponseIntegerJson(item18)) : value["positions"]) } : {}), ...(Object.hasOwn(value, "units") ? { ["units"]: (Array.isArray(value["units"]) ? value["units"].map((item14: unknown) => normalizeOrganizationUnitResponseIntegerJson(item14)) : value["units"]) } : {}), ...(Object.hasOwn(value, "userPositions") ? { ["userPositions"]: (Array.isArray(value["userPositions"]) ? value["userPositions"].map((item22: unknown) => normalizeOrganizationUserPositionResponseIntegerJson(item22)) : value["userPositions"]) } : {}), ...(Object.hasOwn(value, "userUnits") ? { ["userUnits"]: (Array.isArray(value["userUnits"]) ? value["userUnits"].map((item18: unknown) => normalizeOrganizationUserUnitResponseIntegerJson(item18)) : value["userUnits"]) } : {}) } : value);
}

function normalizeHostUserProfileResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostUserProfileWriteRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostUserProjectedFieldsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "failedLoginCount") ? { ["failedLoginCount"]: normalizeWireInteger(value["failedLoginCount"]) } : {}) } : value);
}

function normalizeHostUserResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profile") ? { ["profile"]: normalizeIntegerUnion(value["profile"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostUserProfileResponse(value), normalize: (value: unknown) => normalizeHostUserProfileResponseIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "projectedFields") ? { ["projectedFields"]: normalizeIntegerUnion(value["projectedFields"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostUserProjectedFieldsResponse(value), normalize: (value: unknown) => normalizeHostUserProjectedFieldsResponseIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHostUserRolesResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeHttpJobArgsIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "successStatusCodes") ? { ["successStatusCodes"]: (Array.isArray(value["successStatusCodes"]) ? value["successStatusCodes"].map((item27: unknown) => normalizeWireInteger(item27)) : value["successStatusCodes"]) } : {}), ...(Object.hasOwn(value, "timeoutSeconds") ? { ["timeoutSeconds"]: normalizeWireInteger(value["timeoutSeconds"]) } : {}) } : value);
}

function normalizeImportAdministrativeRegionAddedSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}) } : value);
}

function normalizeImportAdministrativeRegionItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}) } : value);
}

function normalizeImportAdministrativeRegionsApplyResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "addedCount") ? { ["addedCount"]: normalizeWireInteger(value["addedCount"]) } : {}), ...(Object.hasOwn(value, "manifest") ? { ["manifest"]: normalizeAdministrativeRegionDatasetManifestResponseIntegerJson(value["manifest"]) } : {}), ...(Object.hasOwn(value, "removedCount") ? { ["removedCount"]: normalizeWireInteger(value["removedCount"]) } : {}), ...(Object.hasOwn(value, "skippedCount") ? { ["skippedCount"]: normalizeWireInteger(value["skippedCount"]) } : {}), ...(Object.hasOwn(value, "updatedCount") ? { ["updatedCount"]: normalizeWireInteger(value["updatedCount"]) } : {}) } : value);
}

function normalizeImportAdministrativeRegionsPreviewResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "added") ? { ["added"]: (Array.isArray(value["added"]) ? value["added"].map((item14: unknown) => normalizeImportAdministrativeRegionAddedSummaryIntegerJson(item14)) : value["added"]) } : {}), ...(Object.hasOwn(value, "skippedCount") ? { ["skippedCount"]: normalizeWireInteger(value["skippedCount"]) } : {}) } : value);
}

function normalizeImportAdministrativeRegionsRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeImportAdministrativeRegionItemIntegerJson(item14)) : value["items"]) } : {}) } : value);
}

function normalizeImportExportTaskDetailResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "executionFailedRowCount") ? { ["executionFailedRowCount"]: normalizeWireInteger(value["executionFailedRowCount"]) } : {}), ...(Object.hasOwn(value, "invalidRowCount") ? { ["invalidRowCount"]: normalizeWireInteger(value["invalidRowCount"]) } : {}), ...(Object.hasOwn(value, "nextLineNumber") ? { ["nextLineNumber"]: normalizeWireInteger(value["nextLineNumber"]) } : {}), ...(Object.hasOwn(value, "previewRows") ? { ["previewRows"]: (Array.isArray(value["previewRows"]) ? value["previewRows"].map((item20: unknown) => normalizeStaticImportRowPreviewResultIntegerJson(item20)) : value["previewRows"]) } : {}), ...(Object.hasOwn(value, "processedRowCount") ? { ["processedRowCount"]: normalizeWireInteger(value["processedRowCount"]) } : {}), ...(Object.hasOwn(value, "succeededRowCount") ? { ["succeededRowCount"]: normalizeWireInteger(value["succeededRowCount"]) } : {}), ...(Object.hasOwn(value, "totalRows") ? { ["totalRows"]: normalizeWireInteger(value["totalRows"]) } : {}), ...(Object.hasOwn(value, "validRowCount") ? { ["validRowCount"]: normalizeWireInteger(value["validRowCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeImportExportTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "executionFailedRowCount") ? { ["executionFailedRowCount"]: normalizeWireInteger(value["executionFailedRowCount"]) } : {}), ...(Object.hasOwn(value, "invalidRowCount") ? { ["invalidRowCount"]: normalizeWireInteger(value["invalidRowCount"]) } : {}), ...(Object.hasOwn(value, "nextLineNumber") ? { ["nextLineNumber"]: normalizeWireInteger(value["nextLineNumber"]) } : {}), ...(Object.hasOwn(value, "processedRowCount") ? { ["processedRowCount"]: normalizeWireInteger(value["processedRowCount"]) } : {}), ...(Object.hasOwn(value, "succeededRowCount") ? { ["succeededRowCount"]: normalizeWireInteger(value["succeededRowCount"]) } : {}), ...(Object.hasOwn(value, "totalRows") ? { ["totalRows"]: normalizeWireInteger(value["totalRows"]) } : {}), ...(Object.hasOwn(value, "validRowCount") ? { ["validRowCount"]: normalizeWireInteger(value["validRowCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeImportHostUserRowResultIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "line") ? { ["line"]: normalizeWireInteger(value["line"]) } : {}) } : value);
}

function normalizeImportHostUsersRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "rows") ? { ["rows"]: (Array.isArray(value["rows"]) ? value["rows"].map((item13: unknown) => normalizeCreateHostUserRequestIntegerJson(item13)) : value["rows"]) } : {}) } : value);
}

function normalizeImportHostUsersResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "results") ? { ["results"]: (Array.isArray(value["results"]) ? value["results"].map((item16: unknown) => normalizeImportHostUserRowResultIntegerJson(item16)) : value["results"]) } : {}), ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeImportOrganizationPositionRowIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}) } : value);
}

function normalizeImportOrganizationPositionRowResultIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "line") ? { ["line"]: normalizeWireInteger(value["line"]) } : {}) } : value);
}

function normalizeImportOrganizationPositionsRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "rows") ? { ["rows"]: (Array.isArray(value["rows"]) ? value["rows"].map((item13: unknown) => normalizeImportOrganizationPositionRowIntegerJson(item13)) : value["rows"]) } : {}) } : value);
}

function normalizeImportOrganizationPositionsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "results") ? { ["results"]: (Array.isArray(value["results"]) ? value["results"].map((item16: unknown) => normalizeImportOrganizationPositionRowResultIntegerJson(item16)) : value["results"]) } : {}), ...(Object.hasOwn(value, "succeededCount") ? { ["succeededCount"]: normalizeWireInteger(value["succeededCount"]) } : {}) } : value);
}

function normalizeInboxUnreadCountResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "unreadCount") ? { ["unreadCount"]: normalizeWireInteger(value["unreadCount"]) } : {}) } : value);
}

function normalizeK3CloudConnectionConfigResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "lcid") ? { ["lcid"]: normalizeWireInteger(value["lcid"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeK3CloudDocumentSyncResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeLocalePreferenceResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profileVersion") ? { ["profileVersion"]: normalizeWireInteger(value["profileVersion"]) } : {}) } : value);
}

function normalizeLogFileSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sizeBytes") ? { ["sizeBytes"]: normalizeWireInteger(value["sizeBytes"]) } : {}) } : value);
}

function normalizeLogFileTailIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "bytesRead") ? { ["bytesRead"]: normalizeWireInteger(value["bytesRead"]) } : {}) } : value);
}

function normalizeMyReleaseNoteResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionSortKey") ? { ["versionSortKey"]: normalizeWireInteger(value["versionSortKey"]) } : {}) } : value);
}

function normalizeNotificationBindingResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draftRevision") ? { ["draftRevision"]: normalizeWireInteger(value["draftRevision"]) } : {}), ...(Object.hasOwn(value, "latestPublishedVersionNumber") ? { ["latestPublishedVersionNumber"]: normalizeWireInteger(value["latestPublishedVersionNumber"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeNotificationBindingTargetInputIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "order") ? { ["order"]: normalizeWireInteger(value["order"]) } : {}) } : value);
}

function normalizeNotificationDeliveryAttemptResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "attemptNumber") ? { ["attemptNumber"]: normalizeWireInteger(value["attemptNumber"]) } : {}) } : value);
}

function normalizeNotificationDeliveryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "attempts") ? { ["attempts"]: (Array.isArray(value["attempts"]) ? value["attempts"].map((item17: unknown) => normalizeNotificationDeliveryAttemptResponseIntegerJson(item17)) : value["attempts"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeNotificationProviderProfileResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draftRevision") ? { ["draftRevision"]: normalizeWireInteger(value["draftRevision"]) } : {}), ...(Object.hasOwn(value, "latestPublishedVersionNumber") ? { ["latestPublishedVersionNumber"]: normalizeWireInteger(value["latestPublishedVersionNumber"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeNotificationTemplateParameterDefinitionIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "maxLength") ? { ["maxLength"]: normalizeWireInteger(value["maxLength"]) } : {}) } : value);
}

function normalizeNotificationTemplateParameterSchemaIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "parameters") ? { ["parameters"]: (Array.isArray(value["parameters"]) ? value["parameters"].map((item19: unknown) => normalizeNotificationTemplateParameterDefinitionIntegerJson(item19)) : value["parameters"]) } : {}), ...(Object.hasOwn(value, "schemaVersion") ? { ["schemaVersion"]: normalizeWireInteger(value["schemaVersion"]) } : {}) } : value);
}

function normalizeNotificationTemplateResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draftRevision") ? { ["draftRevision"]: normalizeWireInteger(value["draftRevision"]) } : {}), ...(Object.hasOwn(value, "latestPublishedVersionNumber") ? { ["latestPublishedVersionNumber"]: normalizeWireInteger(value["latestPublishedVersionNumber"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOcrIdCardTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOcrProviderConfigResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOperationLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "durationMs") ? { ["durationMs"]: normalizeWireInteger(value["durationMs"]) } : {}), ...(Object.hasOwn(value, "statusCode") ? { ["statusCode"]: normalizeWireInteger(value["statusCode"]) } : {}) } : value);
}

function normalizeOrganizationPositionLevelResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOrganizationPositionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOrganizationUnitResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOrganizationUserPositionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOrganizationUserUnitResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeOutboundCallLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "durationMs") ? { ["durationMs"]: normalizeWireInteger(value["durationMs"]) } : {}), ...(Object.hasOwn(value, "retryCount") ? { ["retryCount"]: normalizeWireInteger(value["retryCount"]) } : {}), ...(Object.hasOwn(value, "statusCode") ? { ["statusCode"]: normalizeWireInteger(value["statusCode"]) } : {}) } : value);
}

function normalizePagedResultOfAccessLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAccessLogResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfAdministrativeRegionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAdministrativeRegionResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfAiAgentToolCallListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAiAgentToolCallListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfAiChatSessionListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAiChatSessionListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfAiModelConfigListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAiModelConfigListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfAiTenantQuotaListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeAiTenantQuotaListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfCodeGenerationRunResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeCodeGenerationRunResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfCodeGenerationTemplateResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeCodeGenerationTemplateResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfConfigEntryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeConfigEntryResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfDataApprovalRequestResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeDataApprovalRequestResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfDictItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeDictItemResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfDictTypeResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeDictTypeResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfEnterpriseRequestResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeEnterpriseRequestResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfExceptionLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostAnnouncementReadReceiptResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostAnnouncementResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostAnnouncementResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostApiKeyResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostDocumentAccessLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostDocumentItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostDocumentItemResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostDocumentPreviewTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostDocumentPreviewTaskResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostDocumentShareResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostDocumentShareResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostFileReferenceClaimResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostFileResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostFileResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostJobDefinitionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostJobDefinitionResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostJobExecutionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostJobExecutionResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostJobScheduleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostJobScheduleResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostMenuResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostMenuResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostOnlineSessionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostReleaseNoteResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostReleaseNoteResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostRoleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostRoleResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfHostUserResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeHostUserResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfImportExportTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeImportExportTaskResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfInboxMessageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfK3CloudDocumentSyncResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeK3CloudDocumentSyncResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfMyReleaseNoteResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeMyReleaseNoteResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfNotificationBindingResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeNotificationBindingResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfNotificationDeliveryResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeNotificationDeliveryResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfNotificationProviderProfileResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeNotificationProviderProfileResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfNotificationTemplateResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeNotificationTemplateResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOcrIdCardTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOcrIdCardTaskResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOperationLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOperationLogResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOrganizationAssignableUserResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOrganizationPositionLevelResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOrganizationPositionLevelResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOrganizationPositionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOrganizationPositionResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOrganizationUnitResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOrganizationUnitResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOrganizationUserPositionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOrganizationUserPositionResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOrganizationUserUnitResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOrganizationUserUnitResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfOutboundCallLogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeOutboundCallLogResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfPaymentMerchantConfigListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizePaymentMerchantConfigListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfPaymentOrderListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizePaymentOrderListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfPaymentRefundListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizePaymentRefundListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfPersonalScheduleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizePersonalScheduleResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfReceivedHostAnnouncementListItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfReportingDataSourceListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeReportingDataSourceListItemIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfReportingExportTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeReportingExportTaskResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfSerialNumberRuleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeSerialNumberRuleResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfTenantPackageSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeTenantPackageSummaryIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfTenantSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeTenantSummaryIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfWorkflowInstanceListItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfWorkflowRecoveryTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeWorkflowRecoveryTaskResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePagedResultOfWorkflowTodoListItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "items") ? { ["items"]: (Array.isArray(value["items"]) ? value["items"].map((item14: unknown) => normalizeWorkflowTodoListItemResponseIntegerJson(item14)) : value["items"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizePauseWorkflowInstanceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizePaymentMerchantConfigListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePaymentMerchantConfigResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePaymentOrderListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "amountMinor") ? { ["amountMinor"]: normalizeWireInteger(value["amountMinor"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePaymentOrderResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "amountMinor") ? { ["amountMinor"]: normalizeWireInteger(value["amountMinor"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePaymentRefundListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "amountMinor") ? { ["amountMinor"]: normalizeWireInteger(value["amountMinor"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePaymentRefundResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "amountMinor") ? { ["amountMinor"]: normalizeWireInteger(value["amountMinor"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePersonalScheduleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePreviewGoViewProjectRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizePreviewPrintingTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizePreviewSerialNumberRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sequenceValue") ? { ["sequenceValue"]: normalizeWireInteger(value["sequenceValue"]) } : {}) } : value);
}

function normalizePrintingTemplatePreviewResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizePrintingTemplateResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "latestPublishedVersionNumber") ? { ["latestPublishedVersionNumber"]: normalizeWireInteger(value["latestPublishedVersionNumber"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePrintingTemplateVersionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeProblemDetailsIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "status") ? { ["status"]: normalizeWireInteger(value["status"]) } : {}) } : value);
}

function normalizePublishGoViewProjectRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishHostAnnouncementRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishHostReleaseNoteRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishNotificationBindingRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishNotificationProviderProfileRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishNotificationTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishPrintingTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishReportingDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizePublishWorkflowDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizePublishWorkflowFormRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeReassignWorkflowInstanceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeReconcileWorkflowRecoveryTaskRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeRecoverWorkflowInstanceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeReplaceHostRoleFieldGrantsRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReplaceHostRoleMembersRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReplaceHostRolePermissionsRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReplaceHostUserRolesRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReportingDataSourceListItemIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReportingDataSourceResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "port") ? { ["port"]: normalizeWireInteger(value["port"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReportingDefinitionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "latestPublishedVersionNumber") ? { ["latestPublishedVersionNumber"]: normalizeWireInteger(value["latestPublishedVersionNumber"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReportingDefinitionVersionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeReportingExecutionPageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "commandTimeoutSeconds") ? { ["commandTimeoutSeconds"]: normalizeWireInteger(value["commandTimeoutSeconds"]) } : {}), ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "totalRows") ? { ["totalRows"]: normalizeWireInteger(value["totalRows"]) } : {}), ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeReportingExportTaskDetailResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "rowCount") ? { ["rowCount"]: normalizeWireInteger(value["rowCount"]) } : {}), ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeReportingExportTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "rowCount") ? { ["rowCount"]: normalizeWireInteger(value["rowCount"]) } : {}), ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeReportingGroupResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeReportingQueryPortDefinitionIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "parameters") ? { ["parameters"]: (Array.isArray(value["parameters"]) ? value["parameters"].map((item19: unknown) => normalizeReportingQueryPortParameterDefinitionIntegerJson(item19)) : value["parameters"]) } : {}) } : value);
}

function normalizeReportingQueryPortParameterDefinitionIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "maximum") ? { ["maximum"]: normalizeWireInteger(value["maximum"]) } : {}), ...(Object.hasOwn(value, "minimum") ? { ["minimum"]: normalizeWireInteger(value["minimum"]) } : {}) } : value);
}

function normalizeRestoreDiagnosticPolicyRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "configEntryVersion") ? { ["configEntryVersion"]: normalizeWireInteger(value["configEntryVersion"]) } : {}) } : value);
}

function normalizeRestoreHostDocumentItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeResumeWorkflowInstanceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeRetractHostReleaseNoteRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeRetryDataApprovalRequestBodyIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeRetryNotificationDeliveryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeRetryWorkflowRecoveryTaskRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeReturnWorkflowTodoRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeRevokeAiAgentDelegationRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedVersion") ? { ["expectedVersion"]: normalizeWireInteger(value["expectedVersion"]) } : {}) } : value);
}

function normalizeRevokeAllHostUserSessionsResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "revokedSessionCount") ? { ["revokedSessionCount"]: normalizeWireInteger(value["revokedSessionCount"]) } : {}) } : value);
}

function normalizeRollbackHostDocumentVersionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeSelfServiceProfileResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profile") ? { ["profile"]: normalizeIntegerUnion(value["profile"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostUserProfileResponse(value), normalize: (value: unknown) => normalizeHostUserProfileResponseIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "userVersion") ? { ["userVersion"]: normalizeWireInteger(value["userVersion"]) } : {}) } : value);
}

function normalizeSerialNumberPreviewResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sequenceValue") ? { ["sequenceValue"]: normalizeWireInteger(value["sequenceValue"]) } : {}) } : value);
}

function normalizeSerialNumberRuleResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "maximumValue") ? { ["maximumValue"]: normalizeWireInteger(value["maximumValue"]) } : {}), ...(Object.hasOwn(value, "minimumValue") ? { ["minimumValue"]: normalizeWireInteger(value["minimumValue"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeSerialRuleDisableApprovalPreviewResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeSerialRuleDisableApprovalSubmissionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "requestVersion") ? { ["requestVersion"]: normalizeWireInteger(value["requestVersion"]) } : {}) } : value);
}

function normalizeSerialRuleUpdateApprovalSubmissionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "requestVersion") ? { ["requestVersion"]: normalizeWireInteger(value["requestVersion"]) } : {}) } : value);
}

function normalizeServerRuntimeMetricIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "longValue") ? { ["longValue"]: normalizeWireInteger(value["longValue"]) } : {}) } : value);
}

function normalizeServerRuntimeSnapshotIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "metrics") ? { ["metrics"]: (Array.isArray(value["metrics"]) ? value["metrics"].map((item16: unknown) => normalizeServerRuntimeMetricIntegerJson(item16)) : value["metrics"]) } : {}), ...(Object.hasOwn(value, "processId") ? { ["processId"]: normalizeWireInteger(value["processId"]) } : {}), ...(Object.hasOwn(value, "uptimeSeconds") ? { ["uptimeSeconds"]: normalizeWireInteger(value["uptimeSeconds"]) } : {}) } : value);
}

function normalizeSetNotificationProviderProfileEnabledRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeSetPersonalScheduleStatusRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeSetWorkflowDefinitionStatusRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedVersion") ? { ["expectedVersion"]: normalizeWireInteger(value["expectedVersion"]) } : {}) } : value);
}

function normalizeSetWorkflowFormStatusRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedVersion") ? { ["expectedVersion"]: normalizeWireInteger(value["expectedVersion"]) } : {}) } : value);
}

function normalizeStaticImportRowPreviewResultIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "lineNumber") ? { ["lineNumber"]: normalizeWireInteger(value["lineNumber"]) } : {}) } : value);
}

function normalizeSubmitSerialRuleDisableApprovalRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "statusChange") ? { ["statusChange"]: normalizeChangeSerialNumberRuleStatusRequestIntegerJson(value["statusChange"]) } : {}) } : value);
}

function normalizeSubmitSerialRuleUpdateApprovalRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "update") ? { ["update"]: normalizeUpdateSerialNumberRuleRequestIntegerJson(value["update"]) } : {}) } : value);
}

function normalizeTenantBrandingResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeTenantPackageSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "assignedTenantCount") ? { ["assignedTenantCount"]: normalizeWireInteger(value["assignedTenantCount"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeTenantSummaryIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeTestAiModelEmbeddingResultIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "dimensions") ? { ["dimensions"]: normalizeWireInteger(value["dimensions"]) } : {}), ...(Object.hasOwn(value, "inputCount") ? { ["inputCount"]: normalizeWireInteger(value["inputCount"]) } : {}), ...(Object.hasOwn(value, "inputTokens") ? { ["inputTokens"]: normalizeWireInteger(value["inputTokens"]) } : {}) } : value);
}

function normalizeUpdateAdministrativeRegionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "level") ? { ["level"]: normalizeWireInteger(value["level"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateAiChatSessionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateAiMcpRemoteConnectionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateAiModelConfigRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateAiTenantQuotaRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "monthlyRequestLimit") ? { ["monthlyRequestLimit"]: normalizeWireInteger(value["monthlyRequestLimit"]) } : {}), ...(Object.hasOwn(value, "monthlyTokenLimit") ? { ["monthlyTokenLimit"]: normalizeWireInteger(value["monthlyTokenLimit"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateCodeGenerationTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "schema") ? { ["schema"]: normalizeCodeGenerationPreviewRequestIntegerJson(value["schema"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateConfigEntryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateDataApprovalScenarioBindingBodyIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateDiagnosticPolicyRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "configEntryVersion") ? { ["configEntryVersion"]: normalizeWireInteger(value["configEntryVersion"]) } : {}), ...(Object.hasOwn(value, "rules") ? { ["rules"]: (Array.isArray(value["rules"]) ? value["rules"].map((item14: unknown) => normalizeDiagnosticPolicyRuleRequestIntegerJson(item14)) : value["rules"]) } : {}) } : value);
}

function normalizeUpdateDictItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateDictTypeRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateEnterpriseRequestRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateGoViewProjectRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostAnnouncementRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostDocumentCategoryRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostDocumentItemRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sort") ? { ["sort"]: normalizeWireInteger(value["sort"]) } : {}), ...(Object.hasOwn(value, "status") ? { ["status"]: normalizeIntegerUnion(value["status"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostDocumentStatus(value), normalize: (value: unknown) => value }]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostDocumentShareStatusRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostDocumentTagRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostDocumentVersionRetentionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "batchSize") ? { ["batchSize"]: normalizeWireInteger(value["batchSize"]) } : {}), ...(Object.hasOwn(value, "maximumRetainedHistoryVersions") ? { ["maximumRetainedHistoryVersions"]: normalizeWireInteger(value["maximumRetainedHistoryVersions"]) } : {}), ...(Object.hasOwn(value, "minimumRetainedVersionsPerItem") ? { ["minimumRetainedVersionsPerItem"]: normalizeWireInteger(value["minimumRetainedVersionsPerItem"]) } : {}), ...(Object.hasOwn(value, "pollSeconds") ? { ["pollSeconds"]: normalizeWireInteger(value["pollSeconds"]) } : {}) } : value);
}

function normalizeUpdateHostFileMetadataRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeUpdateHostFolderRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeUpdateHostJobDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "args") ? { ["args"]: normalizeIntegerUnion(value["args"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHttpJobArgs(value), normalize: (value: unknown) => normalizeHttpJobArgsIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostJobScheduleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostMenuRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostReleaseNoteRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostRoleDataScopeRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostRoleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostTenantPackageRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostTenantRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateHostUserRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profile") ? { ["profile"]: normalizeIntegerUnion(value["profile"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostUserProfileWriteRequest(value), normalize: (value: unknown) => normalizeHostUserProfileWriteRequestIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateK3CloudConnectionConfigRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "lcid") ? { ["lcid"]: normalizeWireInteger(value["lcid"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateLocaleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profileVersion") ? { ["profileVersion"]: normalizeWireInteger(value["profileVersion"]) } : {}) } : value);
}

function normalizeUpdateNotificationBindingRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "targets") ? { ["targets"]: (Array.isArray(value["targets"]) ? value["targets"].map((item16: unknown) => normalizeNotificationBindingTargetInputIntegerJson(item16)) : value["targets"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateNotificationProviderProfileRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateNotificationTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "parameterSchema") ? { ["parameterSchema"]: normalizeNotificationTemplateParameterSchemaIntegerJson(value["parameterSchema"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateOcrProviderConfigRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateOrganizationPositionLevelRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateOrganizationPositionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateOrganizationUnitRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateOrganizationUserPositionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateOrganizationUserUnitRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdatePaymentMerchantConfigRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdatePersonalScheduleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdatePrintingTemplateRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateReportingDataSourceRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "port") ? { ["port"]: normalizeWireInteger(value["port"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateReportingDefinitionRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateReportingGroupRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "sortOrder") ? { ["sortOrder"]: normalizeWireInteger(value["sortOrder"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateSelfServiceProfileRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profile") ? { ["profile"]: normalizeIntegerUnion(value["profile"], [{ matches: (value: unknown) => value === null, normalize: (value: unknown) => value }, { matches: (value: unknown) => isHostUserProfileWriteRequest(value), normalize: (value: unknown) => normalizeHostUserProfileWriteRequestIntegerJson(value) }]) } : {}), ...(Object.hasOwn(value, "userVersion") ? { ["userVersion"]: normalizeWireInteger(value["userVersion"]) } : {}) } : value);
}

function normalizeUpdateSerialNumberRuleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "displayOrder") ? { ["displayOrder"]: normalizeWireInteger(value["displayOrder"]) } : {}), ...(Object.hasOwn(value, "maximumValue") ? { ["maximumValue"]: normalizeWireInteger(value["maximumValue"]) } : {}), ...(Object.hasOwn(value, "minimumValue") ? { ["minimumValue"]: normalizeWireInteger(value["minimumValue"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateTenantBrandingRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeUpdateWorkflowDefinitionDraftRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draft") ? { ["draft"]: normalizeWorkflowDefinitionDraftIntegerJson(value["draft"]) } : {}), ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeUpdateWorkflowFormDraftRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draft") ? { ["draft"]: normalizeWorkflowFormSchemaIntegerJson(value["draft"]) } : {}), ...(Object.hasOwn(value, "expectedRevision") ? { ["expectedRevision"]: normalizeWireInteger(value["expectedRevision"]) } : {}) } : value);
}

function normalizeWorkflowDefinitionDraftIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "nodes") ? { ["nodes"]: (Array.isArray(value["nodes"]) ? value["nodes"].map((item14: unknown) => normalizeWorkflowNodeDraftIntegerJson(item14)) : value["nodes"]) } : {}), ...(Object.hasOwn(value, "schemaVersion") ? { ["schemaVersion"]: normalizeWireInteger(value["schemaVersion"]) } : {}) } : value);
}

function normalizeWorkflowDefinitionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draft") ? { ["draft"]: normalizeWorkflowDefinitionDraftIntegerJson(value["draft"]) } : {}), ...(Object.hasOwn(value, "draftRevision") ? { ["draftRevision"]: normalizeWireInteger(value["draftRevision"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeWorkflowDefinitionVersionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "schemaVersion") ? { ["schemaVersion"]: normalizeWireInteger(value["schemaVersion"]) } : {}), ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeWorkflowFormComponentCatalogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "adapterVersion") ? { ["adapterVersion"]: normalizeWireInteger(value["adapterVersion"]) } : {}), ...(Object.hasOwn(value, "catalogVersion") ? { ["catalogVersion"]: normalizeWireInteger(value["catalogVersion"]) } : {}), ...(Object.hasOwn(value, "schemaVersion") ? { ["schemaVersion"]: normalizeWireInteger(value["schemaVersion"]) } : {}) } : value);
}

function normalizeWorkflowFormResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "draft") ? { ["draft"]: normalizeWorkflowFormSchemaIntegerJson(value["draft"]) } : {}), ...(Object.hasOwn(value, "draftRevision") ? { ["draftRevision"]: normalizeWireInteger(value["draftRevision"]) } : {}), ...(Object.hasOwn(value, "version") ? { ["version"]: normalizeWireInteger(value["version"]) } : {}) } : value);
}

function normalizeWorkflowFormSchemaIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "adapterVersion") ? { ["adapterVersion"]: normalizeWireInteger(value["adapterVersion"]) } : {}), ...(Object.hasOwn(value, "schemaVersion") ? { ["schemaVersion"]: normalizeWireInteger(value["schemaVersion"]) } : {}) } : value);
}

function normalizeWorkflowFormVersionResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "adapterVersion") ? { ["adapterVersion"]: normalizeWireInteger(value["adapterVersion"]) } : {}), ...(Object.hasOwn(value, "componentCatalogVersion") ? { ["componentCatalogVersion"]: normalizeWireInteger(value["componentCatalogVersion"]) } : {}), ...(Object.hasOwn(value, "schemaVersion") ? { ["schemaVersion"]: normalizeWireInteger(value["schemaVersion"]) } : {}), ...(Object.hasOwn(value, "versionNumber") ? { ["versionNumber"]: normalizeWireInteger(value["versionNumber"]) } : {}) } : value);
}

function normalizeWorkflowGatewayJoinResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "arrivedBranchCount") ? { ["arrivedBranchCount"]: normalizeWireInteger(value["arrivedBranchCount"]) } : {}), ...(Object.hasOwn(value, "requiredBranchCount") ? { ["requiredBranchCount"]: normalizeWireInteger(value["requiredBranchCount"]) } : {}) } : value);
}

function normalizeWorkflowInstanceResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "approvedCount") ? { ["approvedCount"]: normalizeWireInteger(value["approvedCount"]) } : {}), ...(Object.hasOwn(value, "gatewayJoins") ? { ["gatewayJoins"]: (Array.isArray(value["gatewayJoins"]) ? value["gatewayJoins"].map((item21: unknown) => normalizeWorkflowGatewayJoinResponseIntegerJson(item21)) : value["gatewayJoins"]) } : {}), ...(Object.hasOwn(value, "pendingCount") ? { ["pendingCount"]: normalizeWireInteger(value["pendingCount"]) } : {}), ...(Object.hasOwn(value, "rejectedCount") ? { ["rejectedCount"]: normalizeWireInteger(value["rejectedCount"]) } : {}), ...(Object.hasOwn(value, "reminderCount") ? { ["reminderCount"]: normalizeWireInteger(value["reminderCount"]) } : {}), ...(Object.hasOwn(value, "requiredApprovalCount") ? { ["requiredApprovalCount"]: normalizeWireInteger(value["requiredApprovalCount"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeWorkflowNodeDraftIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "nodeSchemaVersion") ? { ["nodeSchemaVersion"]: normalizeWireInteger(value["nodeSchemaVersion"]) } : {}) } : value);
}

function normalizeWorkflowNodeTypeCatalogResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "catalogVersion") ? { ["catalogVersion"]: normalizeWireInteger(value["catalogVersion"]) } : {}), ...(Object.hasOwn(value, "definitionSchemaVersion") ? { ["definitionSchemaVersion"]: normalizeWireInteger(value["definitionSchemaVersion"]) } : {}), ...(Object.hasOwn(value, "nodeTypes") ? { ["nodeTypes"]: (Array.isArray(value["nodeTypes"]) ? value["nodeTypes"].map((item18: unknown) => normalizeWorkflowNodeTypeResponseIntegerJson(item18)) : value["nodeTypes"]) } : {}) } : value);
}

function normalizeWorkflowNodeTypeResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "nodeSchemaVersion") ? { ["nodeSchemaVersion"]: normalizeWireInteger(value["nodeSchemaVersion"]) } : {}) } : value);
}

function normalizeWorkflowRecipientCandidatePageResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "page") ? { ["page"]: normalizeWireInteger(value["page"]) } : {}), ...(Object.hasOwn(value, "pageSize") ? { ["pageSize"]: normalizeWireInteger(value["pageSize"]) } : {}), ...(Object.hasOwn(value, "total") ? { ["total"]: normalizeWireInteger(value["total"]) } : {}) } : value);
}

function normalizeWorkflowRecoveryTaskResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "attemptCount") ? { ["attemptCount"]: normalizeWireInteger(value["attemptCount"]) } : {}), ...(Object.hasOwn(value, "leaseGeneration") ? { ["leaseGeneration"]: normalizeWireInteger(value["leaseGeneration"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeWorkflowTodoDetailResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "approvedCount") ? { ["approvedCount"]: normalizeWireInteger(value["approvedCount"]) } : {}), ...(Object.hasOwn(value, "pendingCount") ? { ["pendingCount"]: normalizeWireInteger(value["pendingCount"]) } : {}), ...(Object.hasOwn(value, "rejectedCount") ? { ["rejectedCount"]: normalizeWireInteger(value["rejectedCount"]) } : {}), ...(Object.hasOwn(value, "requiredApprovalCount") ? { ["requiredApprovalCount"]: normalizeWireInteger(value["requiredApprovalCount"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}), ...(Object.hasOwn(value, "submissionRevision") ? { ["submissionRevision"]: normalizeWireInteger(value["submissionRevision"]) } : {}) } : value);
}

function normalizeWorkflowTodoListItemResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}) } : value);
}

function normalizeWorkflowTodoRuntimeResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "approvedCount") ? { ["approvedCount"]: normalizeWireInteger(value["approvedCount"]) } : {}), ...(Object.hasOwn(value, "pendingCount") ? { ["pendingCount"]: normalizeWireInteger(value["pendingCount"]) } : {}), ...(Object.hasOwn(value, "rejectedCount") ? { ["rejectedCount"]: normalizeWireInteger(value["rejectedCount"]) } : {}), ...(Object.hasOwn(value, "requiredApprovalCount") ? { ["requiredApprovalCount"]: normalizeWireInteger(value["requiredApprovalCount"]) } : {}), ...(Object.hasOwn(value, "revision") ? { ["revision"]: normalizeWireInteger(value["revision"]) } : {}), ...(Object.hasOwn(value, "submissionRevision") ? { ["submissionRevision"]: normalizeWireInteger(value["submissionRevision"]) } : {}) } : value);
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 服务端 Int64 可按 JSON 字符串输出；客户端仅在无精度损失时归一为既有 number 契约。
function normalizeWireInteger(value: unknown): unknown {
  if (typeof value !== 'string' || !/^-?(?:0|[1-9]\d*)$/.test(value)) return value;
  const number = Number(value);
  return Number.isSafeInteger(number) ? number : value;
}

function normalizeIntegerIntersection(value: unknown, normalizers: ReadonlyArray<(value: unknown) => unknown>): unknown {
  for (const normalize of normalizers) value = normalize(value);
  return value;
}

function normalizeIntegerUnion(value: unknown, branches: ReadonlyArray<{ matches: (value: unknown) => boolean; normalize: (value: unknown) => unknown }>): unknown {
  if (branches.some(branch => branch.matches(value))) return value;
  for (const branch of branches) {
    const candidate = branch.normalize(value);
    if (branch.matches(candidate)) return candidate;
  }
  return value;
}

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
