// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 数据模型。

export interface AccessHostDocumentShareRequest {
  readonly password?: null | string;
}

export interface AccessLogCursorPageResponse {
  readonly hasMore: boolean;
  readonly items: Array<AccessLogResponse>;
  readonly nextCursor: null | string;
}

export interface AccessLogResponse {
  readonly clientIpFingerprint: null | string;
  readonly durationMs: number;
  readonly httpMethod: string;
  readonly id: string;
  readonly isAuthenticated: boolean;
  readonly occurredAtUtc: string;
  readonly requestPath: string;
  readonly statusCode: number;
  readonly tenantId: null | string;
  readonly traceId: null | string;
  readonly userId: null | string;
}

export interface ActWorkflowTodoRequest {
  readonly comment: null | string;
  readonly expectedRevision: number;
  readonly fieldPatch: JsonElement;
  readonly idempotencyKey: string;
}

export interface AddHostDocumentVersionRequest {
  readonly changeDescription: null | string;
  readonly fileId: string;
}

export interface AssignHostTenantPackageRequest {
  readonly tenantPackageId: null | string;
  readonly version: number;
}

export interface AssignOrganizationPositionLevelRequest {
  readonly positionLevelId: null | string;
  readonly version: number;
}

export interface AssignOrganizationPositionUnitRequest {
  readonly unitId: null | string;
  readonly version: number;
}

export interface AuthorizationTreeActionResponse {
  readonly id: string;
  readonly name: string;
  readonly order: number;
  readonly permissionCode: string;
}

export interface AuthorizationTreeModuleResponse {
  readonly id: string;
  readonly order: number;
  readonly pages: Array<AuthorizationTreePageResponse>;
  readonly title: string;
}

export interface AuthorizationTreePageResponse {
  readonly actions: Array<AuthorizationTreeActionResponse>;
  readonly children: Array<AuthorizationTreePageResponse>;
  readonly id: string;
  readonly order: number;
  readonly permissionCode: string;
  readonly title: string;
}

export interface BatchDeleteConfigEntriesRequest {
  readonly ids: Array<string>;
}

export interface BatchHostUserIdsRequest {
  readonly userIds: Array<string>;
}

export interface BatchHostUserStatusItem {
  readonly errorCode: null | string;
  readonly message: null | string;
  readonly succeeded: boolean;
  readonly userId: string;
}

export interface BatchHostUserStatusResponse {
  readonly results: Array<BatchHostUserStatusItem>;
  readonly succeededCount: number;
}

export interface BatchUpdateConfigValuesRequest {
  readonly updates: Array<ConfigValueUpdate>;
}

export interface BeginTotpEnrollmentResponse {
  readonly otpAuthUri: string;
  readonly sharedSecretBase32: string;
}

export interface CancelWorkflowInstanceRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: null | string;
}

export interface ChangeHostJobScheduleStateRequest {
  readonly version: number;
}

export interface ChangeSerialNumberRuleStatusRequest {
  readonly version: number;
}

export interface CodeGenerationCatalogColumnListResponse {
  readonly columns: Array<CodeGenerationPreviewColumnRequest>;
  readonly skippedColumnNames: Array<string>;
  readonly tableName: string;
}

export interface CodeGenerationCatalogColumnSyncRequest {
  readonly columns: Array<CodeGenerationPreviewColumnRequest>;
  readonly tableName: string;
}

export interface CodeGenerationCatalogColumnSyncResponse {
  readonly addedColumnNames: Array<string>;
  readonly columns: Array<CodeGenerationPreviewColumnRequest>;
  readonly removedColumnNames: Array<string>;
  readonly skippedColumnNames: Array<string>;
  readonly tableName: string;
}

export interface CodeGenerationCatalogTableResponse {
  readonly tableName: string;
}

export interface CodeGenerationClientRouteTargetRequest {
  readonly layuiControllerExport?: null | string;
  readonly layuiControllerPath?: null | string;
  readonly routePath: string;
  readonly vueComponentPath: string;
  readonly vueRouteName: string;
}

export interface CodeGenerationEntityCapabilitiesRequest {
  readonly deleteMode: string;
  readonly hasCreatedAudit: boolean;
  readonly hasDeletedAudit: boolean;
  readonly hasUpdatedAudit: boolean;
  readonly hasVersion: boolean;
  readonly ownershipMode: string;
}

export interface CodeGenerationIntegrationTargetRequest {
  readonly authorizationContributorPath?: null | string;
  readonly clientRoute?: null | CodeGenerationClientRouteTargetRequest;
  readonly compositionCatalogPath: string;
  readonly compositionProjectPath: string;
  readonly layuiRouterPath?: null | string;
  readonly moduleEntryPointPath: string;
  readonly moduleName: string;
  readonly moduleProjectPath: string;
  readonly vueRouterPath: string;
}

export interface CodeGenerationPreviewArtifactResponse {
  readonly content: string;
  readonly kind: string;
  readonly path: string;
  readonly sha256: string;
}

export interface CodeGenerationPreviewColumnRequest {
  readonly clrPropertyName: string;
  readonly databaseName: string;
  readonly isNullable: boolean;
  readonly jsonPropertyName: string;
  readonly maxLength: null | number;
  readonly numericPrecision: null | number;
  readonly numericScale: null | number;
  readonly scalarType: string;
  readonly ui?: null | CodeGenerationPreviewColumnUiRequest;
}

export interface CodeGenerationPreviewColumnUiRequest {
  readonly controlKind: string;
  readonly includeInCreate: boolean;
  readonly includeInImportExport: boolean;
  readonly includeInUpdate: boolean;
  readonly queryable: boolean;
  readonly queryKind: string;
  readonly required: boolean;
  readonly showInList: boolean;
  readonly sortable: boolean;
  readonly unique: boolean;
}

export interface CodeGenerationPreviewRequest {
  readonly apiResourceName: string;
  readonly clrTypeName: string;
  readonly columns: Array<CodeGenerationPreviewColumnRequest>;
  readonly databaseTableName: string;
  readonly dataScope: string;
  readonly entityCapabilities?: null | CodeGenerationEntityCapabilitiesRequest;
  readonly entityKey: string;
  readonly hasVersion: null | boolean;
  readonly moduleKey: string;
  readonly ownerKey: string;
  readonly permissionResourceName: string;
  readonly relationships?: null | Array<CodeGenerationRelationshipRequest>;
  readonly rootNamespace: string;
  readonly scene?: null | string;
}

export interface CodeGenerationPreviewResponse {
  readonly artifacts: Array<CodeGenerationPreviewArtifactResponse>;
  readonly createPermission?: null | string;
  readonly databaseTableName: string;
  readonly disablePermission?: null | string;
  readonly readPermission: string;
  readonly updatePermission?: null | string;
  readonly writePermission: string;
}

export interface CodeGenerationRelationshipRequest {
  readonly cascadeDelete?: null | boolean;
  readonly compositeKeyColumnNames?: null | Array<string>;
  readonly dependentColumnName: string;
  readonly dependentDataScope: string;
  readonly dependentEntityKey: string;
  readonly principalColumnName: string;
  readonly principalDataScope: string;
  readonly principalEntityKey: string;
}

export interface CodeGenerationRunApplyRequest {
  readonly integrationTarget?: null | CodeGenerationIntegrationTargetRequest;
  readonly previewRunId: string;
}

export interface CodeGenerationRunApplyResponse {
  readonly artifactCount: number;
  readonly changedArtifactCount: number;
  readonly manifestSha256: string;
  readonly previewRunId: string;
  readonly runId: string;
}

export interface CodeGenerationRunPreviewRequest {
  readonly schema: null | CodeGenerationPreviewRequest;
  readonly templateId: null | string;
  readonly templateVersion: null | number;
}

export interface CodeGenerationRunPreviewResponse {
  readonly preview: CodeGenerationPreviewResponse;
  readonly runId: string;
}

export interface CodeGenerationRunResponse {
  readonly artifactCount: number;
  readonly entityKey: null | string;
  readonly errorCode: null | string;
  readonly finishedAtUtc: string;
  readonly id: string;
  readonly manifestSha256: null | string;
  readonly moduleKey: null | string;
  readonly operationKind: string;
  readonly requestedByUserId: string;
  readonly schemaSha256: null | string;
  readonly sourceApplyRunId: null | string;
  readonly startedAtUtc: string;
  readonly status: string;
  readonly templateId: null | string;
  readonly templateVersion: null | number;
}

export interface CodeGenerationRunRollbackChainRequest {
  readonly applyRunIds: Array<string>;
}

export interface CodeGenerationRunRollbackChainResponse {
  readonly rollbacks: Array<CodeGenerationRunRollbackResponse>;
}

export interface CodeGenerationRunRollbackRequest {
  readonly applyRunId: string;
}

export interface CodeGenerationRunRollbackResponse {
  readonly applyRunId: string;
  readonly artifactCount: number;
  readonly changedArtifactCount: number;
  readonly manifestSha256: string;
  readonly runId: string;
}

export interface CodeGenerationTemplateResponse {
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly description: null | string;
  readonly id: string;
  readonly name: string;
  readonly schema: CodeGenerationPreviewRequest;
  readonly schemaSha256: string;
  readonly updatedAtUtc: null | string;
  readonly updatedByUserId: null | string;
  readonly version: number;
}

export interface ConfigEntryResponse {
  readonly configKey: string;
  readonly createdAtUtc: string;
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly groupName: null | string;
  readonly hasValue: boolean;
  readonly id: string;
  readonly isActive: boolean;
  readonly updatedAtUtc: null | string;
  readonly value: string;
  readonly valueKind: "string" | "boolean" | "integer" | "decimal" | "json" | "secret";
  readonly version: number;
}

export interface ConfigValueUpdate {
  readonly configKey: string;
  readonly value: string;
}

export interface ConfirmTotpEnrollmentRequest {
  readonly totpCode: string;
}

export interface CreateCodeGenerationTemplateRequest {
  readonly description: null | string;
  readonly name: string;
  readonly schema: CodeGenerationPreviewRequest;
}

export interface CreateConfigEntryRequest {
  readonly configKey: string;
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly groupName: null | string;
  readonly value: string;
  readonly valueKind: "string" | "boolean" | "integer" | "decimal" | "json" | "secret";
}

export interface CreateDictItemRequest {
  readonly color: null | string;
  readonly displayOrder: number;
  readonly label: string;
  readonly value: string;
}

export interface CreateDictTypeRequest {
  readonly code: string;
  readonly description: null | string;
  readonly displayOrder: number;
  readonly name: string;
}

export interface CreateHostAnnouncementRequest {
  readonly content: string;
  readonly title: string;
}

export interface CreateHostApiKeyRequest {
  readonly displayName: string;
  readonly expiresAtUtc: null | string;
  readonly permissions: Array<string>;
  readonly userId: string;
}

export interface CreateHostApiKeyResponse {
  readonly key: HostApiKeyResponse;
  readonly secret: string;
}

export interface CreateHostDocumentCategoryRequest {
  readonly code: null | string;
  readonly color: null | string;
  readonly description: null | string;
  readonly icon: null | string;
  readonly name: string;
  readonly parentId: null | string;
  readonly sortOrder: number;
}

export interface CreateHostDocumentItemRequest {
  readonly categoryId: null | string;
  readonly description: null | string;
  readonly documentType: HostDocumentType;
  readonly sort: number;
  readonly status: HostDocumentStatus;
  readonly tagIds: null | Array<string>;
  readonly thumbnail: null | string;
  readonly title: string;
}

export interface CreateHostDocumentShareRequest {
  readonly documentId: string;
  readonly maxAccessCount?: null | number;
  readonly password?: null | string;
  readonly validDays: number;
}

export interface CreateHostDocumentTagRequest {
  readonly code: null | string;
  readonly color: null | string;
  readonly description: null | string;
  readonly icon: null | string;
  readonly name: string;
}

export interface CreateHostJobDefinitionRequest {
  readonly allowConcurrentExecutions?: boolean;
  readonly args: null | HttpJobArgs;
  readonly description: null | string;
  readonly displayName: string;
  readonly groupName: null | string;
  readonly handlerKind: string;
  readonly jobKey: string;
}

export interface CreateHostJobScheduleRequest {
  readonly args: null | string;
  readonly cronExpression: null | string;
  readonly endTime: null | string;
  readonly jobDefinitionId: string;
  readonly misfirePolicy: string;
  readonly oneTimeAtUtc: null | string;
  readonly startTime: null | string;
  readonly timeZoneId: string;
  readonly triggerKind: string;
}

export interface CreateHostMenuRequest {
  readonly caption: string;
  readonly componentKey: string;
  readonly displayOrder: number;
  readonly icon: string;
  readonly isAffix?: boolean;
  readonly isEmbedded?: boolean;
  readonly isHidden?: boolean;
  readonly isKeepAlive?: boolean;
  readonly linkUrl?: null | string;
  readonly menuType?: string;
  readonly parentId: null | string;
  readonly path: string;
  readonly redirect?: null | string;
  readonly remark?: null | string;
  readonly requiredPermission: string;
  readonly routeName: string;
  readonly title: string;
}

export interface CreateHostRoleRequest {
  readonly code: string;
  readonly name: string;
}

export interface CreateHostTenantPackageRequest {
  readonly code: string;
  readonly description: null | string;
  readonly name: string;
}

export interface CreateHostUserRequest {
  readonly accountType?: null | string;
  readonly displayName: string;
  readonly password: string;
  readonly profile?: null | HostUserProfileWriteRequest;
  readonly username: string;
}

export interface CreateMyRecipientEndpointRequest {
  readonly endpointKindKey: string;
  readonly providerProfileVersionId: string;
  readonly rawValue: string;
}

export interface CreateNotificationBindingRequest {
  readonly bindingKey: string;
  readonly channelKey: string;
  readonly dispatchModeKey: string;
  readonly producerKey: string;
  readonly sceneKey: string;
  readonly targets: Array<NotificationBindingTargetInput>;
}

export interface CreateNotificationProviderProfileRequest {
  readonly nonSecretConfig: JsonElement;
  readonly profileKey: string;
  readonly providerTypeKey: string;
  readonly secretReference: null | string;
}

export interface CreateNotificationTemplateRequest {
  readonly channelKey: string;
  readonly contentCategoryKey: string;
  readonly draftBody: NotificationTemplateBody;
  readonly draftSubject: string;
  readonly parameterSchema: NotificationTemplateParameterSchema;
  readonly templateKey: string;
}

export interface CreateOrganizationPositionLevelRequest {
  readonly code: string;
  readonly displayOrder: number;
  readonly name: string;
}

export interface CreateOrganizationPositionRequest {
  readonly code: string;
  readonly displayOrder: number;
  readonly name: string;
}

export interface CreateOrganizationUnitRequest {
  readonly code: string;
  readonly displayOrder: number;
  readonly name: string;
  readonly parentId: null | string;
}

export interface CreateOrganizationUserPositionRequest {
  readonly isPrimary: boolean;
  readonly positionId: string;
  readonly userId: string;
}

export interface CreateOrganizationUserUnitRequest {
  readonly isPrimary: boolean;
  readonly unitId: string;
  readonly userId: string;
}

export interface CreateSerialNumberRuleRequest {
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly isEnabled: boolean;
  readonly maximumValue: number;
  readonly minimumValue: number;
  readonly pattern: string;
  readonly resetInterval: SerialNumberResetInterval;
  readonly ruleKey: string;
  readonly scope: SerialNumberRuleScope;
}

export interface CreateWorkflowDefinitionRequest {
  readonly definitionKey: string;
  readonly draft: WorkflowDefinitionDraft;
}

export interface CreateWorkflowFormRequest {
  readonly draft: WorkflowFormSchema;
  readonly formKey: string;
}

export interface CurrentUserResponse {
  readonly actorScope: string;
  readonly displayName: string;
  readonly id: string;
  readonly isSuperAdministrator: boolean;
  readonly permissions: Array<string>;
  readonly preferredLocale: string;
  readonly profileVersion: number;
  readonly scope: string;
  readonly sessionId: string;
  readonly tenantId: null | string;
  readonly username: string;
}

export interface DeleteCodeGenerationTemplateRequest {
  readonly version: number;
}

export interface DeleteConfigEntryRequest {
  readonly version: number;
}

export interface DeleteDictItemRequest {
  readonly version: number;
}

export interface DeleteDictTypeRequest {
  readonly version: number;
}

export interface DeleteHostDocumentCategoryRequest {
  readonly version: number;
}

export interface DeleteHostDocumentItemRequest {
  readonly version: number;
}

export interface DeleteHostDocumentTagRequest {
  readonly version: number;
}

export interface DeleteHostJobDefinitionRequest {
  readonly version: number;
}

export interface DiagnosticPolicyResponse {
  readonly activeRules: Array<DiagnosticPolicyRuleResponse>;
  readonly configEntryVersion: number;
  readonly isDefault: boolean;
  readonly loadedAtUtc: string;
  readonly pressureState: string;
  readonly version: number;
}

export interface DiagnosticPolicyRuleRequest {
  readonly bestEffortCapacityOverride: null | number;
  readonly expiresAtUtc: string;
  readonly maxRequestPayloadBytesOverride: null | number;
  readonly maxResponsePayloadBytesOverride: null | number;
  readonly scopeKind: string;
  readonly scopeValue: string;
  readonly successSampleRateOverride: null | number | string;
}

export interface DiagnosticPolicyRuleResponse {
  readonly bestEffortCapacityOverride: null | number;
  readonly expiresAtUtc: string;
  readonly maxRequestPayloadBytesOverride: null | number;
  readonly maxResponsePayloadBytesOverride: null | number;
  readonly scopeKind: string;
  readonly scopeValue: string;
  readonly successSampleRateOverride: null | number | string;
}

export interface DictItemResponse {
  readonly color: null | string;
  readonly createdAtUtc: string;
  readonly dictTypeId: string;
  readonly displayOrder: number;
  readonly id: string;
  readonly isActive: boolean;
  readonly label: string;
  readonly updatedAtUtc: null | string;
  readonly value: string;
  readonly version: number;
}

export interface DictTypeResponse {
  readonly code: string;
  readonly createdAtUtc: string;
  readonly description: null | string;
  readonly displayOrder: number;
  readonly id: string;
  readonly isActive: boolean;
  readonly name: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface DisableHostJobDefinitionRequest {
  readonly version: number;
}

export interface EnumCatalogDetail {
  readonly description: null | string;
  readonly displayName: string;
  readonly key: string;
  readonly members: Array<EnumCatalogMember>;
}

export interface EnumCatalogMember {
  readonly code: string;
  readonly displayOrder: number;
  readonly label: string;
}

export interface EnumCatalogSummary {
  readonly description: null | string;
  readonly displayName: string;
  readonly key: string;
  readonly memberCount: number;
}

export interface ExceptionLogResponse {
  readonly clientIpFingerprint: null | string;
  readonly exceptionType: string;
  readonly httpMethod: null | string;
  readonly id: string;
  readonly message: string;
  readonly occurredAtUtc: string;
  readonly requestPath: null | string;
  readonly stackTrace: null | string;
  readonly tenantId: null | string;
  readonly traceId: null | string;
  readonly userId: null | string;
}

export type FieldProjectionDefaultVisibility = number;

export interface FieldProjectionFieldDefinition {
  readonly assignable: boolean;
  readonly defaultVisibility: FieldProjectionDefaultVisibility;
  readonly displayName: string;
  readonly fieldKey: string;
  readonly sensitivity: FieldProjectionSensitivity;
}

export interface FieldProjectionResourceDefinition {
  readonly displayName: string;
  readonly fields: Array<FieldProjectionFieldDefinition>;
  readonly resourceKey: string;
}

export type FieldProjectionSensitivity = number;

export interface GrantSuperAdministratorRequest {
  readonly currentPassword: string;
  readonly totpCode?: null | string;
  readonly username: string;
}

export interface HostAnnouncementResponse {
  readonly content: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly publishedAtUtc: null | string;
  readonly status: string;
  readonly title: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostApiKeyResponse {
  readonly createdAtUtc: string;
  readonly displayName: string;
  readonly expiresAtUtc: null | string;
  readonly id: string;
  readonly isActive: boolean;
  readonly keyPrefix: string;
  readonly lastUsedAtUtc: null | string;
  readonly permissions: Array<string>;
  readonly userId: string;
  readonly username: string;
}

export interface HostDashboardActivityResponse {
  readonly actionKey: string;
  readonly httpMethod: string;
  readonly occurredAtUtc: string;
  readonly requestPath: string;
  readonly succeeded: boolean;
}

export interface HostDashboardSummaryResponse {
  readonly activeTenantCount: number;
  readonly onlineSessionCount: number;
  readonly recentActivities: Array<HostDashboardActivityResponse>;
  readonly todayErrorRate: number | string;
  readonly todayRequestCount: number;
}

export interface HostDocumentCategoryResponse {
  readonly code: null | string;
  readonly color: null | string;
  readonly createdAtUtc: string;
  readonly description: null | string;
  readonly icon: null | string;
  readonly id: string;
  readonly name: string;
  readonly parentId: null | string;
  readonly sortOrder: number;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostDocumentItemResponse {
  readonly accessCount: number;
  readonly categoryColor: null | string;
  readonly categoryId: null | string;
  readonly categoryName: null | string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly currentVersion: null | HostDocumentVersionResponse;
  readonly deletedAtUtc: null | string;
  readonly deletedByUserId: null | string;
  readonly description: null | string;
  readonly documentNo: string;
  readonly documentType: HostDocumentType;
  readonly id: string;
  readonly lastAccessTime: null | string;
  readonly sizeKb: number;
  readonly sort: number;
  readonly status: HostDocumentStatus;
  readonly tags: Array<HostDocumentTagAssignmentResponse>;
  readonly thumbnail: null | string;
  readonly title: string;
  readonly updatedAtUtc: null | string;
  readonly updatedByUserId: null | string;
  readonly version: number;
}

export interface HostDocumentPermissionEntry {
  readonly permissionLevel: string;
  readonly userId: string;
}

export interface HostDocumentPermissionResponse {
  readonly createdAtUtc: string;
  readonly documentId: string;
  readonly id: string;
  readonly permissionLevel: string;
  readonly userId: string;
}

export interface HostDocumentShareAccessResponse {
  readonly accessCountRemaining: number;
  readonly documentId: string;
  readonly fileName: null | string;
  readonly fileSizeBytes: number;
  readonly hasPassword: boolean;
  readonly mimeType: null | string;
  readonly shareCode: string;
  readonly shareId: string;
  readonly title: string;
}

export interface HostDocumentShareResponse {
  readonly accessCount: number;
  readonly createdAtUtc: string;
  readonly documentId: string;
  readonly expireTime: string;
  readonly hasPassword: boolean;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly maxAccessCount: null | number;
  readonly shareCode: string;
  readonly version: number;
}

export interface HostDocumentStatisticsCategoryItem {
  readonly categoryId: null | string;
  readonly categoryName: null | string;
  readonly count: number;
}

export interface HostDocumentStatisticsResponse {
  readonly byCategory: Array<HostDocumentStatisticsCategoryItem>;
  readonly byType: Array<HostDocumentStatisticsTypeItem>;
  readonly recycleBinCount: number;
  readonly shareCount: number;
  readonly summary: HostDocumentStatisticsSummaryResponse;
  readonly todayAccessCount: number;
  readonly todayCreatedCount: number;
  readonly todayDownloadCount: number;
}

export interface HostDocumentStatisticsSummaryResponse {
  readonly totalItems: number;
  readonly totalSizeInfo: string;
  readonly totalSizeKb: number;
  readonly totalVersions: number;
}

export interface HostDocumentStatisticsTypeItem {
  readonly count: number;
  readonly extension: null | string;
  readonly totalSizeKb: number;
}

export type HostDocumentStatus = number;

export interface HostDocumentTagAssignmentResponse {
  readonly tagId: string;
  readonly tagName: string;
}

export interface HostDocumentTagResponse {
  readonly code: null | string;
  readonly color: null | string;
  readonly createdAtUtc: string;
  readonly description: null | string;
  readonly icon: null | string;
  readonly id: string;
  readonly name: string;
  readonly updatedAtUtc: null | string;
  readonly useCount: number;
  readonly version: number;
}

export type HostDocumentType = number;

export interface HostDocumentVersionResponse {
  readonly changeDescription: null | string;
  readonly contentHash: null | string;
  readonly createdAtUtc: string;
  readonly fileId: string;
  readonly id: string;
  readonly sizeBytes: number;
  readonly uploadedByUserId: string;
  readonly versionNumber: number;
}

export interface HostFileResponse {
  readonly contentHash: null | string;
  readonly contentType: string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly id: string;
  readonly originalFileName: string;
  readonly sizeBytes: number;
}

export interface HostJobDefinitionResponse {
  readonly allowConcurrentExecutions: boolean;
  readonly args: null | HttpJobArgs;
  readonly createdAtUtc: string;
  readonly description: null | string;
  readonly displayName: string;
  readonly groupName: null | string;
  readonly handlerKind: string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly jobKey: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostJobExecutionResponse {
  readonly attemptCount: number;
  readonly createdAtUtc: string;
  readonly errorMessage: null | string;
  readonly finishedAtUtc: null | string;
  readonly id: string;
  readonly jobDefinitionId: string;
  readonly jobScheduleId: null | string;
  readonly nextAttemptAtUtc: null | string;
  readonly scheduledForUtc: null | string;
  readonly startedAtUtc: null | string;
  readonly status: string;
  readonly triggerKind: string;
}

export interface HostJobGroupResponse {
  readonly groupName: string;
}

export interface HostJobHealthBacklogSnapshot {
  readonly dueRetryCount: number;
  readonly oldestClaimableCreatedAtUtc: null | string;
  readonly oldestDueRetryAtUtc: null | string;
  readonly pendingCount: number;
}

export interface HostJobHealthResponse {
  readonly backlog: HostJobHealthBacklogSnapshot;
  readonly registeredHandlers: Array<string>;
  readonly workers: Array<HostJobWorkerInstanceResponse>;
}

export interface HostJobScheduleCronPreviewResponse {
  readonly humanDescription: string;
  readonly nextExecutionAtUtc: string;
  readonly nextOccurrencesUtc: Array<string>;
}

export interface HostJobScheduleDefinitionOptionResponse {
  readonly displayName: string;
  readonly handlerKind: string;
  readonly id: string;
  readonly jobKey: string;
}

export interface HostJobScheduleResponse {
  readonly args: null | string;
  readonly completedAtUtc: null | string;
  readonly createdAtUtc: string;
  readonly cronExpression: null | string;
  readonly endTime: null | string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly jobDefinitionDisplayName: string;
  readonly jobDefinitionId: string;
  readonly jobDefinitionJobKey: string;
  readonly lastExecutionAtUtc: null | string;
  readonly misfirePolicy: string;
  readonly nextExecutionAtUtc: null | string;
  readonly numberOfErrors: number;
  readonly numberOfRuns: number;
  readonly oneTimeAtUtc: null | string;
  readonly startTime: null | string;
  readonly timeZoneId: string;
  readonly triggerKind: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostJobWorkerInstanceResponse {
  readonly hostProfile: string;
  readonly instanceId: string;
  readonly isStale: boolean;
  readonly lastHeartbeatAtUtc: string;
  readonly startedAtUtc: string;
  readonly workerVersion: null | string;
}

export interface HostMenuPermissionOptionResponse {
  readonly actionId?: null | string;
  readonly actionKey?: null | string;
  readonly code: string;
  readonly displayName: string;
  readonly displayNameKey: string;
  readonly kind: string;
  readonly moduleKey: string;
  readonly moduleTitle: string;
  readonly pageId: string;
  readonly pageTitle: string;
}

export interface HostMenuResponse {
  readonly caption: string;
  readonly componentKey: string;
  readonly createdAtUtc: string;
  readonly displayOrder: number;
  readonly icon: string;
  readonly id: string;
  readonly isActive: boolean;
  readonly isAffix: boolean;
  readonly isEmbedded: boolean;
  readonly isHidden: boolean;
  readonly isKeepAlive: boolean;
  readonly isSystem: boolean;
  readonly linkUrl: null | string;
  readonly menuType: string;
  readonly parentId: null | string;
  readonly path: string;
  readonly redirect: null | string;
  readonly remark: null | string;
  readonly requiredPermission: string;
  readonly routeName: string;
  readonly title: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostNavigationCatalogSyncResponse {
  readonly created: number;
  readonly reparented: number;
  readonly skipped: number;
}

export interface HostOnlineSessionResponse {
  readonly activeTenantId: null | string;
  readonly clientId: string;
  readonly createdAtUtc: string;
  readonly displayName: string;
  readonly expiresAtUtc: string;
  readonly id: string;
  readonly userId: string;
  readonly username: string;
}

export interface HostRoleDataScopeResponse {
  readonly dataScopeKind: string;
  readonly roleId: string;
  readonly unitIds: Array<string>;
  readonly version: number;
}

export interface HostRoleFieldGrantsResponse {
  readonly fieldKeys: Array<string>;
  readonly resourceKey: string;
  readonly roleId: string;
  readonly version: number;
}

export interface HostRoleResponse {
  readonly code: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly isActive: boolean;
  readonly isSuperAdministrator: boolean;
  readonly isSystem: boolean;
  readonly name: string;
  readonly permissionCodes: Array<string>;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostUserManagementOrganizationReferenceResponse {
  readonly positions: Array<OrganizationPositionResponse>;
  readonly units: Array<OrganizationUnitResponse>;
  readonly userPositions: Array<OrganizationUserPositionResponse>;
  readonly userUnits: Array<OrganizationUserUnitResponse>;
}

export interface HostUserProfileResponse {
  readonly address: null | string;
  readonly birthDate: null | string;
  readonly educationLevel: null | string;
  readonly email: null | string;
  readonly emergencyContact: null | string;
  readonly emergencyContactAddress: null | string;
  readonly emergencyContactPhone: null | string;
  readonly emergencyContactRelation: null | string;
  readonly employeeNumber: null | string;
  readonly ethnicity: null | string;
  readonly gender: null | string;
  readonly graduatedSchool: null | string;
  readonly idCardNumber: null | string;
  readonly idCardType: null | string;
  readonly joinDateUtc: null | string;
  readonly nickname: null | string;
  readonly officePhone: null | string;
  readonly phoneNumber: null | string;
  readonly politicalStatus: null | string;
  readonly remark: null | string;
  readonly sortOrder: null | number;
  readonly version: number;
}

export interface HostUserProfileWriteRequest {
  readonly address: null | string;
  readonly birthDate: null | string;
  readonly educationLevel: null | string;
  readonly email: null | string;
  readonly emergencyContact: null | string;
  readonly emergencyContactAddress: null | string;
  readonly emergencyContactPhone: null | string;
  readonly emergencyContactRelation: null | string;
  readonly employeeNumber: null | string;
  readonly ethnicity: null | string;
  readonly fieldKeys: null | Array<string>;
  readonly gender: null | string;
  readonly graduatedSchool: null | string;
  readonly idCardNumber: null | string;
  readonly idCardType: null | string;
  readonly joinDateUtc: null | string;
  readonly nickname: null | string;
  readonly officePhone: null | string;
  readonly phoneNumber: null | string;
  readonly politicalStatus: null | string;
  readonly remark: null | string;
  readonly sortOrder: null | number;
  readonly version: null | number;
}

export interface HostUserProjectedFieldsResponse {
  readonly effectiveFieldKeys: Array<string>;
  readonly failedLoginCount: null | number;
  readonly lockoutEndUtc: null | string;
  readonly preferredLocale: null | string;
}

export interface HostUserResponse {
  readonly accountType: string;
  readonly createdAtUtc: string;
  readonly displayName: string;
  readonly id: string;
  readonly isActive: boolean;
  readonly profile?: null | HostUserProfileResponse;
  readonly projectedFields?: null | HostUserProjectedFieldsResponse;
  readonly updatedAtUtc: null | string;
  readonly username: string;
  readonly version: number;
}

export interface HostUserRolesResponse {
  readonly roleIds: Array<string>;
  readonly userId: string;
  readonly version: number;
}

export interface HttpJobArgs {
  readonly headers?: null | Readonly<Record<string, unknown>>;
  readonly method: string;
  readonly secretHeaders?: null | Readonly<Record<string, unknown>>;
  readonly successStatusCodes?: null | Array<number>;
  readonly timeoutSeconds?: null | number;
  readonly url: string;
}

export interface HttpJobSecretHeaderRef {
  readonly configKey: string;
}

export type IFormFile = Blob;

export interface ImportHostUserRowResult {
  readonly errorCode: null | string;
  readonly line: number;
  readonly message: null | string;
  readonly succeeded: boolean;
  readonly userId: null | string;
}

export interface ImportHostUsersRequest {
  readonly rows: Array<CreateHostUserRequest>;
}

export interface ImportHostUsersResponse {
  readonly results: Array<ImportHostUserRowResult>;
  readonly succeededCount: number;
}

export interface InboxMessageResponse {
  readonly content: string;
  readonly createdAtUtc: string;
  readonly createdByUserId: null | string;
  readonly id: string;
  readonly readAtUtc: null | string;
  readonly status: string;
  readonly title: string;
}

export interface InboxUnreadCountResponse {
  readonly unreadCount: number;
}

export type JsonElement = unknown;

export interface LocalePreferenceResponse {
  readonly preferredLocale: string;
  readonly profileVersion: number;
}

export interface LogFileSummary {
  readonly fileName: string;
  readonly id: string;
  readonly lastModifiedUtc: string;
  readonly sizeBytes: number;
}

export interface LogFileTail {
  readonly bytesRead: number;
  readonly content: string;
  readonly fileName: string;
  readonly id: string;
  readonly isTruncated: boolean;
}

export interface LoginRequest {
  readonly password: string;
  readonly username: string;
}

export interface ModuleCatalogEntryResponse {
  readonly dependencies: Array<string>;
  readonly displayName: string;
  readonly healthCapability: string;
  readonly hostProfiles: Array<string>;
  readonly moduleKey: string;
  readonly sourceClassification: string;
  readonly version: string;
}

export interface NotificationBindingResponse {
  readonly bindingKey: string;
  readonly createdAtUtc: string;
  readonly draftDispatchModeKey: string;
  readonly draftJson: string;
  readonly draftRevision: number;
  readonly id: string;
  readonly latestBindingTargetsJson: null | string;
  readonly latestChannelKey: null | string;
  readonly latestDispatchModeKey: null | string;
  readonly latestProducerKey: null | string;
  readonly latestPublishedVersionId: null | string;
  readonly latestPublishedVersionNumber: null | number;
  readonly latestSceneKey: null | string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface NotificationBindingTargetInput {
  readonly order: number;
  readonly profileKey: string;
}

export interface NotificationDeliveryAttemptResponse {
  readonly attemptNumber: number;
  readonly errorCode: null | string;
  readonly finishedAtUtc: null | string;
  readonly id: string;
  readonly providerMessageId: null | string;
  readonly resultCategoryKey: null | string;
  readonly startedAtUtc: string;
  readonly statusKey: string;
}

export interface NotificationDeliveryResponse {
  readonly attempts: Array<NotificationDeliveryAttemptResponse>;
  readonly bindingVersionId: null | string;
  readonly channelKey: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly intentId: string;
  readonly nextAttemptAtUtc: null | string;
  readonly providerProfileVersionId: null | string;
  readonly recipientId: string;
  readonly revision: number;
  readonly statusKey: string;
  readonly updatedAtUtc: null | string;
}

export interface NotificationProviderConfigField {
  readonly name: string;
  readonly required: boolean;
  readonly typeKey: string;
}

export interface NotificationProviderProfileResponse {
  readonly createdAtUtc: string;
  readonly draftRevision: number;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly latestAdapterVersion: null | string;
  readonly latestPublishedVersionId: null | string;
  readonly latestPublishedVersionNumber: null | number;
  readonly nonSecretConfigJson: string;
  readonly profileKey: string;
  readonly providerTypeKey: string;
  readonly secretStatus: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface NotificationProviderTypeDescriptor {
  readonly adapterVersion: string;
  readonly nonSecretFields: Array<NotificationProviderConfigField>;
  readonly providerTypeKey: string;
  readonly receiptModeKey: string;
  readonly secretFieldKeys: Array<string>;
  readonly supportedChannelKeys: Array<string>;
  readonly supportsNativeAot: boolean;
}

export interface NotificationTemplateBody {
  readonly text: string;
}

export interface NotificationTemplateParameterDefinition {
  readonly maxLength: null | number;
  readonly name: string;
  readonly required: boolean;
  readonly typeKey: string;
}

export interface NotificationTemplateParameterSchema {
  readonly parameters: Array<NotificationTemplateParameterDefinition>;
  readonly schemaVersion: number;
}

export interface NotificationTemplateResponse {
  readonly channelKey: string;
  readonly contentCategoryKey: string;
  readonly createdAtUtc: string;
  readonly draftBodyJson: string;
  readonly draftParameterSchemaJson: string;
  readonly draftRevision: number;
  readonly draftSubject: string;
  readonly id: string;
  readonly latestContentClassificationKey: null | string;
  readonly latestContentHash: null | string;
  readonly latestPublishedVersionId: null | string;
  readonly latestPublishedVersionNumber: null | number;
  readonly templateKey: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface OperationLogResponse {
  readonly actionKey: string;
  readonly clientIpFingerprint: null | string;
  readonly durationMs: number;
  readonly httpMethod: string;
  readonly id: string;
  readonly occurredAtUtc: string;
  readonly permissionCode: null | string;
  readonly requestPath: string;
  readonly statusCode: number;
  readonly succeeded: boolean;
  readonly tenantId: null | string;
  readonly traceId: null | string;
  readonly userId: null | string;
}

export interface OrganizationAssignableUserResponse {
  readonly displayName: string;
  readonly id: string;
  readonly username: string;
}

export interface OrganizationPositionLevelResponse {
  readonly code: string;
  readonly createdAtUtc: string;
  readonly displayOrder: number;
  readonly id: string;
  readonly isActive: boolean;
  readonly name: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface OrganizationPositionResponse {
  readonly code: string;
  readonly createdAtUtc: string;
  readonly displayOrder: number;
  readonly id: string;
  readonly isActive: boolean;
  readonly name: string;
  readonly positionLevelCode: null | string;
  readonly positionLevelId: null | string;
  readonly positionLevelName: null | string;
  readonly unitCode: null | string;
  readonly unitId: null | string;
  readonly unitName: null | string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface OrganizationUnitResponse {
  readonly code: string;
  readonly createdAtUtc: string;
  readonly displayOrder: number;
  readonly id: string;
  readonly isActive: boolean;
  readonly name: string;
  readonly parentId: null | string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface OrganizationUserPositionResponse {
  readonly createdAtUtc: string;
  readonly displayName: string;
  readonly id: string;
  readonly isActive: boolean;
  readonly isPrimary: boolean;
  readonly positionCode: string;
  readonly positionId: string;
  readonly positionName: string;
  readonly updatedAtUtc: null | string;
  readonly userId: string;
  readonly username: string;
  readonly version: number;
}

export interface OrganizationUserUnitResponse {
  readonly createdAtUtc: string;
  readonly displayName: string;
  readonly id: string;
  readonly isActive: boolean;
  readonly isPrimary: boolean;
  readonly unitCode: string;
  readonly unitId: string;
  readonly unitName: string;
  readonly updatedAtUtc: null | string;
  readonly userId: string;
  readonly username: string;
  readonly version: number;
}

export interface OutboundCallLogResponse {
  readonly destinationHostCategory: string;
  readonly durationMs: number;
  readonly id: string;
  readonly occurredAtUtc: string;
  readonly operationKey: string;
  readonly providerKey: string;
  readonly retryCount: number;
  readonly safeErrorCode: null | string;
  readonly statusCode: number;
  readonly succeeded: boolean;
  readonly tenantId: null | string;
  readonly traceId: null | string;
  readonly userId: null | string;
}

export interface PagedResultOfAccessLogResponse {
  readonly items: Array<AccessLogResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfCodeGenerationRunResponse {
  readonly items: Array<CodeGenerationRunResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfCodeGenerationTemplateResponse {
  readonly items: Array<CodeGenerationTemplateResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfConfigEntryResponse {
  readonly items: Array<ConfigEntryResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfDictItemResponse {
  readonly items: Array<DictItemResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfDictTypeResponse {
  readonly items: Array<DictTypeResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfExceptionLogResponse {
  readonly items: Array<ExceptionLogResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostAnnouncementResponse {
  readonly items: Array<HostAnnouncementResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostApiKeyResponse {
  readonly items: Array<HostApiKeyResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostDocumentItemResponse {
  readonly items: Array<HostDocumentItemResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostDocumentShareResponse {
  readonly items: Array<HostDocumentShareResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostFileResponse {
  readonly items: Array<HostFileResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostJobDefinitionResponse {
  readonly items: Array<HostJobDefinitionResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostJobExecutionResponse {
  readonly items: Array<HostJobExecutionResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostJobScheduleResponse {
  readonly items: Array<HostJobScheduleResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostMenuResponse {
  readonly items: Array<HostMenuResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostOnlineSessionResponse {
  readonly items: Array<HostOnlineSessionResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostRoleResponse {
  readonly items: Array<HostRoleResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostUserResponse {
  readonly items: Array<HostUserResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfInboxMessageResponse {
  readonly items: Array<InboxMessageResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfNotificationBindingResponse {
  readonly items: Array<NotificationBindingResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfNotificationDeliveryResponse {
  readonly items: Array<NotificationDeliveryResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfNotificationProviderProfileResponse {
  readonly items: Array<NotificationProviderProfileResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfNotificationTemplateResponse {
  readonly items: Array<NotificationTemplateResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOperationLogResponse {
  readonly items: Array<OperationLogResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOrganizationAssignableUserResponse {
  readonly items: Array<OrganizationAssignableUserResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOrganizationPositionLevelResponse {
  readonly items: Array<OrganizationPositionLevelResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOrganizationPositionResponse {
  readonly items: Array<OrganizationPositionResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOrganizationUnitResponse {
  readonly items: Array<OrganizationUnitResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOrganizationUserPositionResponse {
  readonly items: Array<OrganizationUserPositionResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOrganizationUserUnitResponse {
  readonly items: Array<OrganizationUserUnitResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfOutboundCallLogResponse {
  readonly items: Array<OutboundCallLogResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfSerialNumberRuleResponse {
  readonly items: Array<SerialNumberRuleResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfTenantPackageSummary {
  readonly items: Array<TenantPackageSummary>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfTenantSummary {
  readonly items: Array<TenantSummary>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PreviewSerialNumberRequest {
  readonly atUtc: string;
  readonly pattern: string;
  readonly scope: SerialNumberRuleScope;
  readonly sequenceValue: number;
  readonly tenantIdentifier: null | string;
}

export interface ProblemDetails {
  readonly detail?: null | string;
  readonly instance?: null | string;
  readonly status?: null | number;
  readonly title?: null | string;
  readonly type?: null | string;
}

export interface ProvisionTenantRequest {
  readonly domain: string;
  readonly identifier: string;
  readonly name: string;
  readonly tenantPackageId?: null | string;
}

export interface PublishHostAnnouncementRequest {
  readonly version: number;
}

export interface PublishNotificationBindingRequest {
  readonly version: number;
}

export interface PublishNotificationProviderProfileRequest {
  readonly version: number;
}

export interface PublishNotificationTemplateRequest {
  readonly contentClassificationKey: string;
  readonly version: number;
}

export interface PublishWorkflowDefinitionRequest {
  readonly expectedRevision: number;
  readonly formVersionId: string;
}

export interface PublishWorkflowFormRequest {
  readonly expectedRevision: number;
}

export interface RecipientEndpointResponse {
  readonly createdAtUtc: string;
  readonly endpointKindKey: string;
  readonly id: string;
  readonly maskedValue: string;
  readonly providerProfileVersionId: string;
  readonly userId: string;
  readonly verificationStatusKey: string;
}

export interface ReplaceHostRoleFieldGrantsRequest {
  readonly fieldKeys: Array<string>;
  readonly resourceKey: string;
  readonly version: number;
}

export interface ReplaceHostRolePermissionsRequest {
  readonly permissionCodes: Array<string>;
  readonly version: number;
}

export interface ReplaceHostUserRolesRequest {
  readonly roleIds: Array<string>;
  readonly version: number;
}

export interface ResetHostUserPasswordRequest {
  readonly password: string;
}

export interface RestoreDiagnosticPolicyRequest {
  readonly configEntryVersion: number;
}

export interface RestoreHostDocumentItemRequest {
  readonly version: number;
}

export interface RetryNotificationDeliveryRequest {
  readonly reason: string;
  readonly revision: number;
}

export interface RevokeSuperAdministratorRequest {
  readonly currentPassword: string;
  readonly totpCode?: null | string;
}

export interface SendHostInboxMessageRequest {
  readonly content: string;
  readonly recipientUserId: string;
  readonly title: string;
}

export interface SerialNumberPreviewResponse {
  readonly value: string;
}

export type SerialNumberResetInterval = number;

export interface SerialNumberRuleResponse {
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly maximumValue: number;
  readonly minimumValue: number;
  readonly pattern: string;
  readonly resetInterval: SerialNumberResetInterval;
  readonly ruleKey: string;
  readonly scope: SerialNumberRuleScope;
  readonly updatedAtUtc: null | string;
  readonly updatedByUserId: null | string;
  readonly version: number;
}

export type SerialNumberRuleScope = number;

export interface SetHostDocumentPermissionsRequest {
  readonly documentId: string;
  readonly permissions: Array<HostDocumentPermissionEntry>;
}

export interface SetNotificationProviderProfileEnabledRequest {
  readonly version: number;
}

export interface StartWorkflowInstanceRequest {
  readonly businessId: string;
  readonly businessType: string;
  readonly definitionVersionId: string;
  readonly idempotencyKey: string;
  readonly initialValues: JsonElement;
}

export type Stream = Blob;

export interface SuperAdministratorAuditResponse {
  readonly actorUserId: null | string;
  readonly eventType: string;
  readonly id: string;
  readonly occurredAtUtc: string;
  readonly resultCode: string;
  readonly succeeded: boolean;
  readonly targetUserId: string;
}

export interface SuperAdministratorChangeResponse {
  readonly changed: boolean;
  readonly targetUserId: string;
}

export interface SuperAdministratorResponse {
  readonly displayName: string;
  readonly isActive: boolean;
  readonly userId: string;
  readonly username: string;
}

export interface TenantPackageSummary {
  readonly assignedTenantCount?: number;
  readonly code: string;
  readonly description: null | string;
  readonly id: string;
  readonly isActive: boolean;
  readonly name: string;
  readonly version: number;
}

export interface TenantSummary {
  readonly defaultLocale?: string;
  readonly domain: string;
  readonly id: string;
  readonly identifier: string;
  readonly isActive: boolean;
  readonly name: string;
  readonly tenantPackageCode?: null | string;
  readonly tenantPackageId?: null | string;
  readonly tenantPackageName?: null | string;
  readonly version: number;
}

export interface TokenResponse {
  readonly accessToken: string;
  readonly expiresAtUtc: string;
  readonly tokenType: string;
}

export interface TotpEnrollmentStatusResponse {
  readonly isEnabled: boolean;
  readonly isEnrolled: boolean;
}

export interface UpdateCodeGenerationTemplateRequest {
  readonly description: null | string;
  readonly name: string;
  readonly schema: CodeGenerationPreviewRequest;
  readonly version: number;
}

export interface UpdateConfigEntryRequest {
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly groupName: null | string;
  readonly value: string;
  readonly version: number;
}

export interface UpdateDiagnosticPolicyRequest {
  readonly configEntryVersion: number;
  readonly pressureState: string;
  readonly rules: Array<DiagnosticPolicyRuleRequest>;
}

export interface UpdateDictItemRequest {
  readonly color: null | string;
  readonly displayOrder: number;
  readonly label: string;
  readonly version: number;
}

export interface UpdateDictTypeRequest {
  readonly description: null | string;
  readonly displayOrder: number;
  readonly name: string;
  readonly version: number;
}

export interface UpdateHostAnnouncementRequest {
  readonly content: string;
  readonly title: string;
  readonly version: number;
}

export interface UpdateHostDocumentCategoryRequest {
  readonly code: null | string;
  readonly color: null | string;
  readonly description: null | string;
  readonly icon: null | string;
  readonly name: string;
  readonly parentId: null | string;
  readonly sortOrder: number;
  readonly version: number;
}

export interface UpdateHostDocumentItemRequest {
  readonly categoryId: null | string;
  readonly description: null | string;
  readonly sort: null | number;
  readonly status: null | HostDocumentStatus;
  readonly tagIds: null | Array<string>;
  readonly thumbnail: null | string;
  readonly title: string;
  readonly version: number;
}

export interface UpdateHostDocumentShareStatusRequest {
  readonly isEnabled: boolean;
  readonly version: number;
}

export interface UpdateHostDocumentTagRequest {
  readonly code: null | string;
  readonly color: null | string;
  readonly description: null | string;
  readonly icon: null | string;
  readonly name: string;
  readonly version: number;
}

export interface UpdateHostJobDefinitionRequest {
  readonly allowConcurrentExecutions: boolean;
  readonly args: null | HttpJobArgs;
  readonly description: null | string;
  readonly displayName: string;
  readonly groupName: null | string;
  readonly handlerKind: string;
  readonly version: number;
}

export interface UpdateHostJobScheduleRequest {
  readonly args: null | string;
  readonly cronExpression: null | string;
  readonly endTime: null | string;
  readonly misfirePolicy: string;
  readonly oneTimeAtUtc: null | string;
  readonly startTime: null | string;
  readonly timeZoneId: string;
  readonly triggerKind: string;
  readonly version: number;
}

export interface UpdateHostMenuRequest {
  readonly caption: string;
  readonly componentKey: string;
  readonly displayOrder: number;
  readonly icon: string;
  readonly isAffix?: boolean;
  readonly isEmbedded?: boolean;
  readonly isHidden?: boolean;
  readonly isKeepAlive?: boolean;
  readonly linkUrl?: null | string;
  readonly menuType?: string;
  readonly parentId: null | string;
  readonly path: string;
  readonly redirect?: null | string;
  readonly remark?: null | string;
  readonly requiredPermission: string;
  readonly title: string;
  readonly version: number;
}

export interface UpdateHostRoleDataScopeRequest {
  readonly dataScopeKind: string;
  readonly tenantId?: null | string;
  readonly unitIds: null | Array<string>;
  readonly version: number;
}

export interface UpdateHostRoleRequest {
  readonly name: string;
  readonly version: number;
}

export interface UpdateHostTenantPackageRequest {
  readonly description: null | string;
  readonly name: string;
  readonly version: number;
}

export interface UpdateHostTenantRequest {
  readonly name: string;
  readonly version: number;
}

export interface UpdateHostUserRequest {
  readonly accountType?: null | string;
  readonly displayName: string;
  readonly profile?: null | HostUserProfileWriteRequest;
  readonly version: number;
}

export interface UpdateLocaleRequest {
  readonly locale: string;
  readonly profileVersion: number;
}

export interface UpdateNotificationBindingRequest {
  readonly channelKey: string;
  readonly dispatchModeKey: string;
  readonly producerKey: string;
  readonly sceneKey: string;
  readonly targets: Array<NotificationBindingTargetInput>;
  readonly version: number;
}

export interface UpdateNotificationProviderProfileRequest {
  readonly nonSecretConfig: JsonElement;
  readonly secretReference: null | string;
  readonly version: number;
}

export interface UpdateNotificationTemplateRequest {
  readonly draftBody: NotificationTemplateBody;
  readonly draftSubject: string;
  readonly parameterSchema: NotificationTemplateParameterSchema;
  readonly version: number;
}

export interface UpdateOrganizationPositionLevelRequest {
  readonly displayOrder: number;
  readonly name: string;
  readonly version: number;
}

export interface UpdateOrganizationPositionRequest {
  readonly displayOrder: number;
  readonly name: string;
  readonly version: number;
}

export interface UpdateOrganizationUnitRequest {
  readonly displayOrder: number;
  readonly name: string;
  readonly parentId: null | string;
  readonly version: number;
}

export interface UpdateOrganizationUserPositionRequest {
  readonly isPrimary: boolean;
  readonly version: number;
}

export interface UpdateOrganizationUserUnitRequest {
  readonly isPrimary: boolean;
  readonly version: number;
}

export interface UpdateSerialNumberRuleRequest {
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly isEnabled: boolean;
  readonly maximumValue: number;
  readonly minimumValue: number;
  readonly pattern: string;
  readonly resetInterval: SerialNumberResetInterval;
  readonly scope: SerialNumberRuleScope;
  readonly version: number;
}

export interface UpdateWorkflowDefinitionDraftRequest {
  readonly draft: WorkflowDefinitionDraft;
  readonly expectedRevision: number;
}

export interface UpdateWorkflowFormDraftRequest {
  readonly draft: WorkflowFormSchema;
  readonly expectedRevision: number;
}

export interface WorkflowDefinitionDraft {
  readonly nodes: Array<WorkflowNodeDraft>;
  readonly schemaVersion: number;
}

export interface WorkflowDefinitionResponse {
  readonly createdAtUtc: string;
  readonly definitionKey: string;
  readonly draft: WorkflowDefinitionDraft;
  readonly draftRevision: number;
  readonly id: string;
  readonly latestPublishedVersionId: null | string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface WorkflowDefinitionVersionResponse {
  readonly canonicalJson: string;
  readonly contentHash: string;
  readonly definitionId: string;
  readonly formVersionId: string;
  readonly id: string;
  readonly publishedAtUtc: string;
  readonly publishedById: string;
  readonly schemaVersion: number;
  readonly versionNumber: number;
}

export interface WorkflowExecutionLogResponse {
  readonly createdAtUtc: string;
  readonly fromStatusKey: null | string;
  readonly id: string;
  readonly instanceId: string;
  readonly stepId: null | string;
  readonly toStatusKey: string;
  readonly transitionKey: string;
}

export interface WorkflowFormComponentCatalogResponse {
  readonly adapterVersion: number;
  readonly catalogVersion: number;
  readonly components: Array<WorkflowFormComponentResponse>;
  readonly schemaVersion: number;
}

export interface WorkflowFormComponentResponse {
  readonly constraintKeys: Array<string>;
  readonly designable: boolean;
  readonly executable: boolean;
  readonly fieldTypeKey: string;
  readonly publishable: boolean;
}

export interface WorkflowFormField {
  readonly constraints: Readonly<Record<string, unknown>>;
  readonly fieldKey: string;
  readonly fieldTypeKey: string;
  readonly required: boolean;
}

export interface WorkflowFormResponse {
  readonly createdAtUtc: string;
  readonly draft: WorkflowFormSchema;
  readonly draftRevision: number;
  readonly formKey: string;
  readonly id: string;
  readonly latestPublishedVersionId: null | string;
  readonly updatedAtUtc: null | string;
}

export interface WorkflowFormSchema {
  readonly adapterVersion: number;
  readonly schemaVersion: number;
  readonly sections: Array<WorkflowFormSection>;
}

export interface WorkflowFormSection {
  readonly fields: Array<WorkflowFormField>;
  readonly sectionKey: string;
}

export interface WorkflowFormVersionResponse {
  readonly adapterVersion: number;
  readonly componentCatalogVersion: number;
  readonly contentHash: string;
  readonly formDefinitionId: string;
  readonly formSchemaJson: string;
  readonly id: string;
  readonly publishedAtUtc: string;
  readonly publishedById: string;
  readonly schemaVersion: number;
  readonly versionNumber: number;
  readonly webRenderSchemaJson: string;
}

export interface WorkflowInstanceResponse {
  readonly activeTodoId: null | string;
  readonly businessId: string;
  readonly businessType: string;
  readonly definitionVersionId: string;
  readonly formVersionId: string;
  readonly id: string;
  readonly revision: number;
  readonly startedAtUtc: string;
  readonly statusKey: string;
}

export interface WorkflowNodeDraft {
  readonly config: JsonElement;
  readonly nodeKey: string;
  readonly nodeSchemaVersion: number;
  readonly nodeTypeKey: string;
}

export interface WorkflowNodeTypeCatalogResponse {
  readonly catalogVersion: number;
  readonly definitionSchemaVersion: number;
  readonly nodeTypes: Array<WorkflowNodeTypeResponse>;
}

export interface WorkflowNodeTypeResponse {
  readonly designable: boolean;
  readonly executable: boolean;
  readonly nodeSchemaVersion: number;
  readonly nodeTypeKey: string;
  readonly publishable: boolean;
  readonly supportsFieldPolicies: boolean;
}

export interface WorkflowTodoDetailResponse {
  readonly assigneeUserId: string;
  readonly fieldPolicies: Readonly<Record<string, unknown>>;
  readonly formSchema: JsonElement;
  readonly formVersionId: string;
  readonly id: string;
  readonly instanceId: string;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: string;
  readonly submission: JsonElement;
  readonly submissionRevision: number;
}

export interface WorkflowTodoResponse {
  readonly arrivedAtUtc: string;
  readonly assigneeUserId: string;
  readonly completedAtUtc: null | string;
  readonly id: string;
  readonly instanceId: string;
  readonly resultActionKey: null | string;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: string;
}

export interface WorkflowTodoRuntimeResponse {
  readonly assigneeUserId: string;
  readonly fieldPolicies: Readonly<Record<string, unknown>>;
  readonly formSchema: JsonElement;
  readonly formSchemaHash: string;
  readonly formVersionId: string;
  readonly id: string;
  readonly instanceId: string;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: string;
  readonly submission: JsonElement;
  readonly submissionRevision: number;
}
