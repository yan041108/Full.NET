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

export interface AdministrativeRegionChildResponse {
  readonly code?: string;
  readonly displayOrder?: number;
  readonly hasChildren?: boolean;
  readonly id?: string;
  readonly level?: number;
  readonly name?: string;
  readonly parentId?: string;
}

export interface AdministrativeRegionDatasetManifestResponse {
  readonly appliedAtUtc?: string;
  readonly appliedByUserId?: string;
  readonly datasetKey?: string;
  readonly datasetVersion?: string;
  readonly id?: string;
  readonly recordCount?: number;
  readonly sourceDigest?: string;
}

export interface AdministrativeRegionResponse {
  readonly cityCode?: string;
  readonly code?: string;
  readonly createdAtUtc?: string;
  readonly displayOrder?: number;
  readonly id?: string;
  readonly latitude?: number;
  readonly level?: number;
  readonly longitude?: number;
  readonly mergerName?: string;
  readonly name?: string;
  readonly parentId?: string;
  readonly pinYin?: string;
  readonly regionType?: string;
  readonly remark?: string;
  readonly shortName?: string;
  readonly updatedAtUtc?: string;
  readonly version?: number;
  readonly zipCode?: string;
}

export interface AdministrativeRegionTreeNodeResponse {
  readonly children?: Array<AdministrativeRegionTreeNodeResponse>;
  readonly code?: string;
  readonly displayOrder?: number;
  readonly id?: string;
  readonly level?: number;
  readonly name?: string;
  readonly parentId?: string;
}

export interface AiAgentToolCallListItem {
  readonly actorUserId: string;
  readonly createdAtUtc: string;
  readonly durationMs?: null | number | string;
  readonly errorCode?: null | string;
  readonly id: string;
  readonly inputSummary: string;
  readonly outputSummary?: null | string;
  readonly permissionCode: string;
  readonly statusKey: string;
  readonly tenantId?: null | string;
  readonly toolName: string;
  readonly traceId?: null | string;
}

export interface AiAgentToolCatalogItem {
  readonly description: string;
  readonly displayName: string;
  readonly inputSchemaJson: string;
  readonly isEnabled: boolean;
  readonly mcpExposureKey: string;
  readonly outputSchemaJson: string;
  readonly permissionCode: string;
  readonly sideEffectKey: string;
  readonly toolName: string;
}

export interface AiChatMessageResponse {
  readonly completionTokens?: null | number | string;
  readonly content: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly promptTokens?: null | number | string;
  readonly roleKey: string;
  readonly sessionId: string;
  readonly statusKey: string;
}

export interface AiChatSessionListItem {
  readonly createdAtUtc: string;
  readonly id: string;
  readonly lastMessageAtUtc?: null | string;
  readonly messageCount: number | string;
  readonly modelConfigId: string;
  readonly modelName: string;
  readonly ownerUserId: string;
  readonly tenantId?: null | string;
  readonly title: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface AiChatSessionResponse {
  readonly createdAtUtc: string;
  readonly id: string;
  readonly isGenerating: boolean;
  readonly messages: Array<AiChatMessageResponse>;
  readonly modelConfigId: string;
  readonly modelName: string;
  readonly ownerUserId: string;
  readonly tenantId?: null | string;
  readonly title: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface AiModelConfigListItem {
  readonly createdAtUtc: string;
  readonly hasApiKey: boolean;
  readonly id: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly lastTestedAtUtc?: null | string;
  readonly lastTestMessage?: null | string;
  readonly lastTestStatusKey?: null | string;
  readonly maskedEndpointBaseUrl: string;
  readonly modelId: string;
  readonly name: string;
  readonly providerKey: string;
  readonly tenantId?: null | string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface AiModelConfigResponse {
  readonly createdAtUtc: string;
  readonly endpointBaseUrl: string;
  readonly hasApiKey: boolean;
  readonly id: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly lastTestedAtUtc?: null | string;
  readonly lastTestMessage?: null | string;
  readonly lastTestStatusKey?: null | string;
  readonly modelId: string;
  readonly name: string;
  readonly organizationId?: null | string;
  readonly providerKey: string;
  readonly tenantId?: null | string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface AiTenantQuotaListItem {
  readonly createdAtUtc: string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly monthlyRequestLimit?: null | number | string;
  readonly monthlyTokenLimit?: null | number | string;
  readonly quotaMonthKey: string;
  readonly tenantId: string;
  readonly updatedAtUtc?: null | string;
  readonly usedRequestsThisMonth: number | string;
  readonly usedTokensThisMonth: number | string;
  readonly version: number | string;
}

export type AiTenantQuotaResponse = AiTenantQuotaListItem;

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

export interface BatchChangeHostJobScheduleStateItem {
  readonly scheduleId: string;
  readonly version: number;
}

export interface BatchChangeHostJobScheduleStateRequest {
  readonly items: Array<BatchChangeHostJobScheduleStateItem>;
}

export interface BatchChangeHostJobScheduleStateResponse {
  readonly results: Array<BatchChangeHostJobScheduleStateResultItem>;
  readonly succeededCount: number;
}

export interface BatchChangeHostJobScheduleStateResultItem {
  readonly errorCode: null | string;
  readonly message: null | string;
  readonly schedule?: null | HostJobScheduleResponse;
  readonly scheduleId: string;
  readonly succeeded: boolean;
}

export interface BatchDeleteConfigEntriesRequest {
  readonly ids: Array<string>;
}

export interface BatchDeleteHostFileItem {
  readonly errorCode: null | string;
  readonly fileId: string;
  readonly message: null | string;
  readonly succeeded: boolean;
}

export interface BatchDeleteHostFilesRequest {
  readonly fileIds: Array<string>;
}

export interface BatchDeleteHostFilesResponse {
  readonly results: Array<BatchDeleteHostFileItem>;
  readonly succeededCount: number;
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

export interface BatchUploadHostFileItem {
  readonly errorCode: null | string;
  readonly file: null | HostFileResponse;
  readonly message: null | string;
  readonly originalFileName: string;
  readonly succeeded: boolean;
}

export interface BatchUploadHostFilesResponse {
  readonly results: Array<BatchUploadHostFileItem>;
  readonly succeededCount: number;
}

export interface BeginTotpEnrollmentResponse {
  readonly otpAuthUri: string;
  readonly sharedSecretBase32: string;
}

export interface CacheInvalidationOperationSummary {
  readonly displayName: string;
  readonly operationKey: string;
  readonly parameters: Array<CacheInvalidationParameterSummary>;
}

export interface CacheInvalidationParameterSummary {
  readonly name: string;
  readonly required: boolean;
  readonly valueType: string;
}

export interface CacheInvalidationRequest {
  readonly operationKey: string;
  readonly parameters?: Readonly<Record<string, unknown>>;
  readonly scope?: string | null;
}

export interface CacheInvalidationResult {
  readonly entryName: string;
  readonly invalidatedTargets: Array<string>;
  readonly operationKey: string;
  readonly scope: string;
}

export interface CachePolicySummary {
  readonly accessKind: string;
  readonly canInvalidate: boolean;
  readonly consistencyClass: string;
  readonly entryName: string;
  readonly invalidationOperations: Array<CacheInvalidationOperationSummary>;
  readonly l1DurationSeconds?: number | null;
  readonly l2DurationSeconds?: number | null;
  readonly ownerModule: string;
  readonly requiresDirectInvalidation: boolean;
}

export interface CancelDataApprovalRequestBody {
  readonly idempotencyKey: string;
}

export interface CancelWorkflowInstanceRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: null | string;
}

export interface ChangeHostJobScheduleStateRequest {
  readonly version: number;
}

export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
}

export interface ChangePersonalScheduleRequest {
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

export interface CodeGenerationCatalogMetadataColumnResponse {
  readonly columnName: string;
  readonly columnType: string;
  readonly dataType: string;
  readonly isNullable: boolean;
  readonly maxLength: null | number;
  readonly numericPrecision: null | number;
  readonly numericScale: null | number;
  readonly ordinalPosition: number;
}

export interface CodeGenerationCatalogMetadataResponse {
  readonly columns: Array<CodeGenerationCatalogMetadataColumnResponse>;
  readonly objectKind: string;
  readonly objectName: string;
}

export interface CodeGenerationCatalogMigrationDraftRequest {
  readonly tableName: string;
}

export interface CodeGenerationCatalogMigrationDraftResponse {
  readonly mySqlDraft: string;
  readonly sqlServerDraft: string;
  readonly tableName: string;
  readonly warnings: Array<string>;
}

export interface CodeGenerationCatalogObjectResponse {
  readonly objectKind: string;
  readonly objectName: string;
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
  readonly hasVersion?: null | boolean;
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

export interface ConfirmOcrIdCardTaskRequest {
  readonly address?: null | string;
  readonly birthDate?: null | string;
  readonly gender?: null | string;
  readonly idNumber: string;
  readonly name: string;
  readonly nation?: null | string;
  readonly version: number | string;
}

export interface ConfirmTotpEnrollmentRequest {
  readonly totpCode: string;
}

export interface CopyHostRoleRequest {
  readonly code: string;
  readonly name: string;
}

export interface CreateAdministrativeRegionRequest {
  readonly cityCode?: string;
  readonly code?: string;
  readonly displayOrder?: number;
  readonly latitude?: number;
  readonly level?: number;
  readonly longitude?: number;
  readonly mergerName?: string;
  readonly name?: string;
  readonly parentId?: string;
  readonly pinYin?: string;
  readonly regionType?: string;
  readonly remark?: string;
  readonly shortName?: string;
  readonly zipCode?: string;
}

export interface CreateAiChatSessionRequest {
  readonly modelConfigId: string;
  readonly title?: null | string;
}

export interface CreateAiModelConfigRequest {
  readonly apiKey?: null | string;
  readonly endpointBaseUrl: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly modelId: string;
  readonly name: string;
  readonly organizationId?: null | string;
  readonly providerKey: string;
  readonly tenantId?: null | string;
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

export interface CreateDataApprovalRequestBody {
  readonly idempotencyKey: string;
  readonly proposedChangeJson: string;
  readonly scenarioKey: string;
  readonly targetEntityId: string;
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

export interface CreateGoViewProjectRequest {
  readonly canvasJson?: null | string;
  readonly isEnabled: boolean;
  readonly name: string;
  readonly projectKey: string;
}

export interface CreateHostAnnouncementRequest {
  readonly audienceKind?: null | string;
  readonly content: string;
  readonly kind?: null | string;
  readonly targetOrganizations?: null | Array<HostAnnouncementTargetOrganization>;
  readonly targetUserIds?: null | Array<string>;
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

export interface CreateHostDocumentPreviewTaskRequest {
  readonly documentItemId: string;
  readonly versionId?: null | string;
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

export interface CreateHostFolderRequest {
  readonly displayOrder?: number;
  readonly name: string;
  readonly parentId?: null | string;
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

export interface CreateHostReleaseNoteRequest {
  readonly content: string;
  readonly title: string;
  readonly versionLabel: string;
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

export interface CreateK3CloudConnectionConfigRequest {
  readonly acctId: string;
  readonly baseUrl: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly lcid: number | string;
  readonly name: string;
  readonly password: string;
  readonly username: string;
}

export interface CreateK3CloudDocumentSyncRequest {
  readonly businessKey: string;
  readonly connectionConfigId: string;
  readonly documentTypeKey: string;
  readonly payloadJson: string;
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
  readonly defaultLocaleTag?: null | string;
  readonly draftBody: NotificationTemplateBody;
  readonly draftSubject: string;
  readonly localeTag?: null | string;
  readonly parameterSchema: NotificationTemplateParameterSchema;
  readonly templateKey: string;
}

export interface CreateOcrIdCardTaskRequest {
  readonly sourceFileId: string;
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

export interface CreatePaymentMerchantConfigRequest {
  readonly apiV3Key?: null | string;
  readonly appId: string;
  readonly certificateSerialNo: string;
  readonly channelKey: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly merchantId: string;
  readonly name: string;
  readonly notifyUrl: string;
  readonly privateKeyPem?: null | string;
  readonly returnUrl: string;
  readonly tenantId?: null | string;
}

export interface CreatePaymentOrderRequest {
  readonly amountMinor: number | string;
  readonly channelKey?: null | string;
  readonly currency: string;
  readonly description?: null | string;
  readonly merchantConfigId?: null | string;
  readonly subject: string;
  readonly tenantId: string;
}

export interface CreatePaymentRefundRequest {
  readonly amountMinor?: null | number | string;
  readonly reason: string;
}

export interface CreatePersonalScheduleRequest {
  readonly content: string;
  readonly endAtUtc: string;
  readonly startAtUtc: string;
}

export interface CreatePrintingTemplateRequest {
  readonly formSchemaKey: string;
  readonly isEnabled: boolean;
  readonly layoutHtml: string;
  readonly name: string;
  readonly templateKey: string;
}

export interface CreateReportingDataSourceRequest {
  readonly databaseName: string;
  readonly isEnabled: boolean;
  readonly name: string;
  readonly password: string;
  readonly port: number | string;
  readonly providerKey: string;
  readonly serverHost: string;
  readonly tenantId?: null | string;
  readonly trustServerCertificate: boolean;
  readonly username: string;
}

export interface CreateReportingDefinitionRequest {
  readonly dataSourceId: string;
  readonly definitionKey: string;
  readonly description?: null | string;
  readonly groupId: string;
  readonly isEnabled: boolean;
  readonly layoutConfigJson?: null | string;
  readonly name: string;
  readonly parameterSchema: Array<ReportingParameterSchemaEntry>;
  readonly queryPortKey: string;
}

export interface CreateReportingExportTaskRequest {
  readonly definitionId: string;
  readonly formatKey: string;
  readonly parameters: Array<ReportingExecutionParameterValue>;
  readonly versionNumber?: null | number | string;
}

export interface CreateReportingGroupRequest {
  readonly isEnabled: boolean;
  readonly name: string;
  readonly parentId?: null | string;
  readonly sortOrder: number | string;
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
  readonly businessTitleTemplate?: null | string;
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

export interface DataApprovalRequestResponse {
  readonly afterSnapshotJson: string;
  readonly applicationAttemptCount: number;
  readonly applicationStatusKey: string;
  readonly beforeSnapshotJson?: string | null;
  readonly id: string;
  readonly lastApplicationAttemptAtUtc?: string | null;
  readonly lastApplicationFailureCode?: string | null;
  readonly lastApplicationFailureMessage?: string | null;
  readonly lastFailureCode?: string | null;
  readonly lastFailureMessage?: string | null;
  readonly lastRecoveryAttemptAtUtc?: string | null;
  readonly recoveryAttemptCount: number;
  readonly recoveryStatusKey: string;
  readonly resolvedAtUtc?: string | null;
  readonly scenarioKey: string;
  readonly statusKey: string;
  readonly submittedAtUtc: string;
  readonly submittedByUserId: string;
  readonly targetEntityId: string;
  readonly version: number;
  readonly workflowDefinitionVersionId: string;
  readonly workflowInstanceId?: string | null;
  readonly workflowRevision?: number | null;
}

export interface DataApprovalScenarioResponse {
  readonly isEnabled: boolean;
  readonly isRegistered: boolean;
  readonly scenarioKey: string;
  readonly scopeKey: string;
  readonly version?: number | null;
  readonly workflowDefinitionKey?: string | null;
  readonly workflowDefinitionVersionId?: string | null;
}

export interface DeleteAdministrativeRegionRequest {
  readonly version?: number;
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

export interface DeleteHostDocumentVersionRequest {
  readonly version: number;
}

export interface DeleteHostFolderRequest {
  readonly expectedRevision: number | string;
}

export interface DeleteHostJobDefinitionRequest {
  readonly version: number;
}

export interface DeleteHostReleaseNoteRequest {
  readonly version: number | string;
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

export interface EnumCatalogDictGenerationItemPreview {
  readonly action: string;
  readonly displayOrder: number;
  readonly existingLabel: null | string;
  readonly proposedLabel: string;
  readonly value: string;
}

export interface EnumCatalogDictGenerationPreview {
  readonly catalogKey: string;
  readonly dictTypeCode: string;
  readonly dictTypeExists: boolean;
  readonly dictTypeName: string;
  readonly items: Array<EnumCatalogDictGenerationItemPreview>;
  readonly unmanagedItems: Array<EnumCatalogDictGenerationUnmanagedItem>;
  readonly willCreateDictType: boolean;
}

export interface EnumCatalogDictGenerationResult {
  readonly catalogKey: string;
  readonly dictTypeCode: string;
  readonly dictTypeCreated: boolean;
  readonly dictTypeId: null | string;
  readonly items: Array<EnumCatalogDictGenerationItemPreview>;
  readonly itemsConflicted: number;
  readonly itemsCreated: number;
  readonly itemsInvalid: number;
  readonly itemsSkipped: number;
}

export interface EnumCatalogDictGenerationUnmanagedItem {
  readonly isActive: boolean;
  readonly label: string;
  readonly value: string;
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

export interface ExecuteReportingDefinitionRequest {
  readonly parameters: Array<ReportingExecutionParameterValue>;
  readonly versionNumber?: null | number | string;
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

export interface GoViewProjectPreviewResponse {
  readonly canvasJson: string;
  readonly generatedAtUtc: string;
  readonly projectId: string;
  readonly projectKey: string;
  readonly projectName: string;
  readonly versionNumber: number | string;
}

export interface GoViewProjectResponse {
  readonly canvasJson: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly latestPublishedVersionNumber: number | string;
  readonly name: string;
  readonly projectKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface GoViewProjectVersionResponse {
  readonly canvasJson: string;
  readonly changeNote?: null | string;
  readonly id: string;
  readonly projectId: string;
  readonly publishedAtUtc: string;
  readonly publishedByUserId: string;
  readonly versionNumber: number | string;
}

export interface GrantSuperAdministratorRequest {
  readonly currentPassword: string;
  readonly totpCode?: null | string;
  readonly username: string;
}

export interface HostAnnouncementReadReceiptResponse {
  readonly displayName: null | string;
  readonly readAtUtc: string;
  readonly userId: string;
  readonly username: null | string;
}

export interface HostAnnouncementReadStatsResponse {
  readonly eligibleRecipientCount: number;
  readonly readCount: number;
  readonly unreadCount: number;
}

export interface HostAnnouncementResponse {
  readonly audienceKind: string;
  readonly content: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly kind: string;
  readonly publishedAtUtc: null | string;
  readonly publishedByUserId: null | string;
  readonly retractedAtUtc: null | string;
  readonly retractedByUserId: null | string;
  readonly status: string;
  readonly targetOrganizations: Array<HostAnnouncementTargetOrganization>;
  readonly targetUserIds: Array<string>;
  readonly title: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface HostAnnouncementTargetOrganization {
  readonly organizationUnitId: string;
  readonly tenantId: string;
}

export interface HostAnnouncementUnreadCountResponse {
  readonly unreadCount: number;
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

export interface HostDocumentAccessLogResponse {
  readonly accessTypeKey: string;
  readonly actorUserId?: null | string;
  readonly clientIpFingerprint?: null | string;
  readonly documentItemId: string;
  readonly documentTitle: string;
  readonly id: string;
  readonly occurredAtUtc: string;
  readonly sourceKey: string;
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

export interface HostDocumentPreviewTaskResponse {
  readonly completedAtUtc?: null | string;
  readonly createdAtUtc: string;
  readonly documentItemId: string;
  readonly documentTitle: string;
  readonly errorCode?: null | string;
  readonly id: string;
  readonly outputFileId?: null | string;
  readonly providerKey: string;
  readonly requestedByUserId: string;
  readonly sourceFileId: string;
  readonly startedAtUtc?: null | string;
  readonly statusKey: string;
  readonly version: number;
  readonly versionId?: null | string;
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

export interface HostFileReferenceClaimResponse {
  readonly confirmedAtUtc: null | string;
  readonly consumerModule: string;
  readonly consumerReferenceId: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly idempotencyKey: string;
  readonly releasedAtUtc: null | string;
  readonly state: string;
  readonly updatedAtUtc: string;
}

export interface HostFileResponse {
  readonly contentHash: null | string;
  readonly contentType: string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly folderId: null | string;
  readonly id: string;
  readonly originalFileName: string;
  readonly revision: number | string;
  readonly sizeBytes: number;
  readonly updatedAtUtc: null | string;
  readonly updatedByUserId: null | string;
}

export interface HostFolderResponse {
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly displayOrder: number;
  readonly id: string;
  readonly name: string;
  readonly parentId: null | string;
  readonly revision: number | string;
  readonly updatedAtUtc: null | string;
  readonly updatedByUserId: null | string;
}

export interface HostFolderTreeNode {
  readonly children: Array<HostFolderTreeNode>;
  readonly displayOrder: number;
  readonly id: string;
  readonly name: string;
  readonly parentId: null | string;
  readonly revision: number | string;
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

export interface HostReleaseNoteResponse {
  readonly content: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly publishedAtUtc: null | string;
  readonly publishedByUserId: null | string;
  readonly retractedAtUtc: null | string;
  readonly retractedByUserId: null | string;
  readonly status: string;
  readonly title: string;
  readonly updatedAtUtc: null | string;
  readonly version: number | string;
  readonly versionLabel: string;
  readonly versionSortKey: number | string;
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

export interface HostRoleMemberResponse {
  readonly displayName: string;
  readonly isActive: boolean;
  readonly userId: string;
  readonly username: string;
}

export interface HostRoleMembersAssignmentResponse {
  readonly roleId: string;
  readonly userIds: Array<string>;
  readonly version: number;
}

export interface HostRoleMembersPageResponse {
  readonly items: Array<HostRoleMemberResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly roleId: string;
  readonly total: number;
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

export interface HostTenantAdministratorsPageResponse {
  readonly items: Array<HostTenantMemberResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly tenantId: string;
  readonly total: number;
}

export interface HostTenantMemberResponse {
  readonly accountType: string;
  readonly displayName: string;
  readonly isActive: boolean;
  readonly userId: string;
  readonly username: string;
}

export interface HostTenantMembersPageResponse {
  readonly items: Array<HostTenantMemberResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly tenantId: string;
  readonly total: number;
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

export type IdentitySessionLoginPolicy = "AllowMultiple" | "SingleSession";

export interface IdentitySessionPolicyResponse {
  readonly loginPolicy: IdentitySessionLoginPolicy;
}

export type IFormFile = Blob;

export interface ImportAdministrativeRegionItem {
  readonly cityCode?: string;
  readonly code?: string;
  readonly displayOrder?: number;
  readonly latitude?: number;
  readonly level?: number;
  readonly longitude?: number;
  readonly mergerName?: string;
  readonly name?: string;
  readonly parentCode?: string;
  readonly pinYin?: string;
  readonly regionType?: string;
  readonly shortName?: string;
  readonly zipCode?: string;
}

export interface ImportAdministrativeRegionsApplyResponse {
  readonly addedCount?: number;
  readonly manifest?: AdministrativeRegionDatasetManifestResponse;
  readonly removedCount?: number;
  readonly skippedCount?: number;
  readonly updatedCount?: number;
}

export interface ImportAdministrativeRegionsPreviewResponse {
  readonly added?: Array<Readonly<Record<string, unknown>>>;
  readonly removed?: Array<Readonly<Record<string, unknown>>>;
  readonly skippedCount?: number;
  readonly updated?: Array<Readonly<Record<string, unknown>>>;
}

export interface ImportAdministrativeRegionsRequest {
  readonly datasetKey?: string;
  readonly datasetVersion?: string;
  readonly items?: Array<ImportAdministrativeRegionItem>;
  readonly mergeMode?: string;
  readonly sourceDigest?: string;
}

export interface ImportExportTaskDetailResponse {
  readonly createdAtUtc: string;
  readonly errorCode?: null | string;
  readonly id: string;
  readonly invalidRowCount: number | string;
  readonly previewCompletedAtUtc?: null | string;
  readonly previewRows: Array<StaticImportRowPreviewResult>;
  readonly requestedByUserId: string;
  readonly schemaDisplayName: string;
  readonly schemaKey: string;
  readonly sourceFileId: string;
  readonly sourceFileName?: null | string;
  readonly statusKey: string;
  readonly tenantId: string;
  readonly totalRows: number | string;
  readonly validRowCount: number | string;
  readonly version: number | string;
  readonly worksheetKey: string;
}

export interface ImportExportTaskResponse {
  readonly createdAtUtc: string;
  readonly errorCode?: null | string;
  readonly executionCompletedAtUtc?: null | string;
  readonly executionFailedRowCount: number | string;
  readonly executionStartedAtUtc?: null | string;
  readonly hasErrorReceipt: boolean;
  readonly id: string;
  readonly invalidRowCount: number | string;
  readonly nextLineNumber: number | string;
  readonly previewCompletedAtUtc?: null | string;
  readonly processedRowCount: number | string;
  readonly requestedByUserId: string;
  readonly schemaDisplayName: string;
  readonly schemaKey: string;
  readonly sourceFileId: string;
  readonly sourceFileName?: null | string;
  readonly statusKey: string;
  readonly succeededRowCount: number | string;
  readonly tenantId: string;
  readonly totalRows: number | string;
  readonly validRowCount: number | string;
  readonly version: number | string;
  readonly worksheetKey: string;
}

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

export interface ImportOrganizationPositionRow {
  readonly code: string;
  readonly displayOrder: number;
  readonly name: string;
  readonly positionLevelCode: null | string;
  readonly unitCode: null | string;
}

export interface ImportOrganizationPositionRowResult {
  readonly errorCode: null | string;
  readonly line: number;
  readonly message: null | string;
  readonly positionId: null | string;
  readonly succeeded: boolean;
}

export interface ImportOrganizationPositionsRequest {
  readonly rows: Array<ImportOrganizationPositionRow>;
}

export interface ImportOrganizationPositionsResponse {
  readonly results: Array<ImportOrganizationPositionRowResult>;
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

export interface K3CloudConnectionConfigResponse {
  readonly acctId: string;
  readonly baseUrl: string;
  readonly createdAtUtc: string;
  readonly hasPassword: boolean;
  readonly id: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly lastTestedAtUtc?: null | string;
  readonly lastTestMessage?: null | string;
  readonly lastTestStatusKey?: null | string;
  readonly lcid: number | string;
  readonly name: string;
  readonly updatedAtUtc?: null | string;
  readonly username: string;
  readonly version: number | string;
}

export interface K3CloudDocumentSyncResponse {
  readonly businessKey: string;
  readonly connectionConfigId: string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly documentTypeKey: string;
  readonly externalBillId?: null | string;
  readonly externalBillNo?: null | string;
  readonly id: string;
  readonly lastErrorCode?: null | string;
  readonly lastErrorMessage?: null | string;
  readonly lastStepKey?: null | string;
  readonly statusKey: string;
  readonly submittedAtUtc?: null | string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

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

export interface ModuleSelectionAnalysisResponse {
  readonly deploymentNotice: string;
  readonly enabledModuleKeys: Array<string>;
  readonly issues: Array<ModuleSelectionIssueResponse>;
  readonly isValid: boolean;
  readonly modules: Array<ModuleSelectionModuleStateResponse>;
  readonly officialModuleKeys: Array<string>;
  readonly preset: null | string;
  readonly sourceKind: string;
}

export interface ModuleSelectionIssueResponse {
  readonly code: string;
  readonly message: string;
  readonly moduleKey: null | string;
  readonly relatedModuleKey: null | string;
}

export interface ModuleSelectionModuleStateResponse {
  readonly dependencies: Array<string>;
  readonly isEnabled: boolean;
  readonly missingDependencies: Array<string>;
  readonly moduleKey: string;
}

export interface ModuleSelectionValidateRequest {
  readonly enabled?: null | Array<string>;
  readonly preset?: null | string;
}

export interface MyReleaseNoteResponse {
  readonly content: string;
  readonly id: string;
  readonly isRead: boolean;
  readonly publishedAtUtc: string;
  readonly readAtUtc: null | string;
  readonly title: string;
  readonly versionLabel: string;
  readonly versionSortKey: number | string;
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

export interface NotificationDeliveryReceiptResponse {
  readonly externalStatusKey: string;
  readonly id: string;
  readonly mappedStatusKey: string;
  readonly processedAtUtc: null | string;
  readonly processStatusKey: string;
  readonly providerMessageId: null | string;
  readonly providerTypeKey: string;
  readonly receivedAtUtc: string;
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
  readonly receipts: Array<NotificationDeliveryReceiptResponse>;
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
  readonly defaultLocaleTag: string;
  readonly draftBodyJson: string;
  readonly draftParameterSchemaJson: string;
  readonly draftRevision: number;
  readonly draftSubject: string;
  readonly id: string;
  readonly latestContentClassificationKey: null | string;
  readonly latestContentHash: null | string;
  readonly latestPublishedVersionId: null | string;
  readonly latestPublishedVersionNumber: null | number;
  readonly localeTag: string;
  readonly missingLocaleTags: Array<string>;
  readonly publishedLocaleTags: Array<string>;
  readonly templateKey: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface OcrIdCardTaskResponse {
  readonly confirmedAddress?: null | string;
  readonly confirmedAtUtc?: null | string;
  readonly confirmedBirthDate?: null | string;
  readonly confirmedGender?: null | string;
  readonly confirmedIdNumber?: null | string;
  readonly confirmedName?: null | string;
  readonly confirmedNation?: null | string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly failureMessage?: null | string;
  readonly id: string;
  readonly recognizedAddress?: null | string;
  readonly recognizedAtUtc?: null | string;
  readonly recognizedBirthDate?: null | string;
  readonly recognizedGender?: null | string;
  readonly recognizedIdNumber?: null | string;
  readonly recognizedName?: null | string;
  readonly recognizedNation?: null | string;
  readonly rejectedAtUtc?: null | string;
  readonly sourceFileId: string;
  readonly statusKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface OcrProviderConfigResponse {
  readonly baseUrl: string;
  readonly createdAtUtc: string;
  readonly hasApiKey: boolean;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly lastTestedAtUtc?: null | string;
  readonly lastTestMessage?: null | string;
  readonly lastTestStatusKey?: null | string;
  readonly name: string;
  readonly providerKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
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

export interface PagedResultOfAdministrativeRegionResponse {
  readonly items?: Array<AdministrativeRegionResponse>;
  readonly page?: number;
  readonly pageSize?: number;
  readonly totalCount?: number;
}

export interface PagedResultOfAiAgentToolCallListItem {
  readonly items: Array<AiAgentToolCallListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfAiChatSessionListItem {
  readonly items: Array<AiChatSessionListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfAiModelConfigListItem {
  readonly items: Array<AiModelConfigListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfAiTenantQuotaListItem {
  readonly items: Array<AiTenantQuotaListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
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

export interface PagedResultOfDataApprovalRequestResponse {
  readonly items: Array<DataApprovalRequestResponse>;
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

export interface PagedResultOfHostAnnouncementReadReceiptResponse {
  readonly items: Array<HostAnnouncementReadReceiptResponse>;
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

export interface PagedResultOfHostDocumentAccessLogResponse {
  readonly items: Array<HostDocumentAccessLogResponse>;
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

export interface PagedResultOfHostDocumentPreviewTaskResponse {
  readonly items: Array<HostDocumentPreviewTaskResponse>;
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

export interface PagedResultOfHostFileReferenceClaimResponse {
  readonly items: Array<HostFileReferenceClaimResponse>;
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

export interface PagedResultOfHostReleaseNoteResponse {
  readonly items: Array<HostReleaseNoteResponse>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
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

export interface PagedResultOfImportExportTaskResponse {
  readonly items: Array<ImportExportTaskResponse>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfInboxMessageResponse {
  readonly items: Array<InboxMessageResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfK3CloudDocumentSyncResponse {
  readonly items: Array<K3CloudDocumentSyncResponse>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfMyReleaseNoteResponse {
  readonly items: Array<MyReleaseNoteResponse>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
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

export interface PagedResultOfOcrIdCardTaskResponse {
  readonly items: Array<OcrIdCardTaskResponse>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
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

export interface PagedResultOfPaymentMerchantConfigListItem {
  readonly items: Array<PaymentMerchantConfigListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfPaymentOrderListItem {
  readonly items: Array<PaymentOrderListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfPaymentRefundListItem {
  readonly items: Array<PaymentRefundListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfPersonalScheduleResponse {
  readonly items: Array<PersonalScheduleResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfReceivedHostAnnouncementListItemResponse {
  readonly items: Array<ReceivedHostAnnouncementListItemResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfReportingDataSourceListItem {
  readonly items: Array<ReportingDataSourceListItem>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
}

export interface PagedResultOfReportingExportTaskResponse {
  readonly items: Array<ReportingExportTaskResponse>;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly total: number | string;
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

export interface PagedResultOfWorkflowInstanceListItemResponse {
  readonly items: Array<WorkflowInstanceListItemResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfWorkflowRecoveryTaskResponse {
  readonly items: Array<WorkflowRecoveryTaskResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfWorkflowTodoListItemResponse {
  readonly items: Array<WorkflowTodoListItemResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PauseWorkflowInstanceRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: null | string;
}

export interface PaymentMerchantConfigListItem {
  readonly channelKey: string;
  readonly createdAtUtc: string;
  readonly hasApiV3Key: boolean;
  readonly hasPrivateKey: boolean;
  readonly id: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly maskedAppId: string;
  readonly maskedCertificateSerialNo: string;
  readonly maskedMerchantId: string;
  readonly maskedNotifyUrl: string;
  readonly maskedReturnUrl: string;
  readonly name: string;
  readonly tenantId?: null | string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PaymentMerchantConfigResponse {
  readonly appId: string;
  readonly certificateSerialNo: string;
  readonly channelKey: string;
  readonly createdAtUtc: string;
  readonly hasApiV3Key: boolean;
  readonly hasPrivateKey: boolean;
  readonly id: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly merchantId: string;
  readonly name: string;
  readonly notifyUrl: string;
  readonly returnUrl: string;
  readonly tenantId?: null | string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PaymentOrderListItem {
  readonly amountMinor: number | string;
  readonly channelKey: string;
  readonly codeUrl?: null | string;
  readonly createdAtUtc: string;
  readonly currency: string;
  readonly description?: null | string;
  readonly failMessage?: null | string;
  readonly id: string;
  readonly merchantConfigId: string;
  readonly outTradeNo: string;
  readonly paidAtUtc?: null | string;
  readonly providerTransactionId?: null | string;
  readonly subject: string;
  readonly tenantId: string;
  readonly tradeStateKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PaymentOrderResponse {
  readonly amountMinor: number | string;
  readonly channelKey: string;
  readonly codeUrl?: null | string;
  readonly createdAtUtc: string;
  readonly currency: string;
  readonly description?: null | string;
  readonly failMessage?: null | string;
  readonly id: string;
  readonly merchantConfigId: string;
  readonly outTradeNo: string;
  readonly paidAtUtc?: null | string;
  readonly providerTransactionId?: null | string;
  readonly subject: string;
  readonly tenantId: string;
  readonly tradeStateKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PaymentRefundListItem {
  readonly amountMinor: number | string;
  readonly completedAtUtc?: null | string;
  readonly createdAtUtc: string;
  readonly currency: string;
  readonly failMessage?: null | string;
  readonly id: string;
  readonly orderId: string;
  readonly outRefundNo: string;
  readonly outTradeNo: string;
  readonly providerRefundId?: null | string;
  readonly reason: string;
  readonly refundStateKey: string;
  readonly tenantId: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PaymentRefundResponse {
  readonly amountMinor: number | string;
  readonly completedAtUtc?: null | string;
  readonly createdAtUtc: string;
  readonly currency: string;
  readonly failMessage?: null | string;
  readonly id: string;
  readonly merchantConfigId: string;
  readonly orderId: string;
  readonly outRefundNo: string;
  readonly outTradeNo: string;
  readonly providerRefundId?: null | string;
  readonly reason: string;
  readonly refundStateKey: string;
  readonly tenantId: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PersonalScheduleResponse {
  readonly completedAtUtc: null | string;
  readonly content: string;
  readonly createdAtUtc: string;
  readonly endAtUtc: string;
  readonly id: string;
  readonly startAtUtc: string;
  readonly status: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface PreviewGoViewProjectRequest {
  readonly versionNumber?: null | number | string;
}

export interface PreviewPrintingTemplateRequest {
  readonly versionNumber?: null | number | string;
}

export interface PreviewSerialNumberRequest {
  readonly atUtc: string;
  readonly pattern: string;
  readonly resetInterval?: SerialNumberResetInterval;
  readonly scope: SerialNumberRuleScope;
  readonly sequenceValue: number;
  readonly tenantIdentifier: null | string;
}

export interface PreviewWorkflowAssigneeRequest {
  readonly assigneePolicy: Readonly<Record<string, unknown>>;
  readonly initiatorUserId?: null | string;
}

export interface PrintingFormFieldDefinition {
  readonly displayName: string;
  readonly fieldKey: string;
}

export interface PrintingFormSchemaDefinition {
  readonly description: string;
  readonly displayName: string;
  readonly fields: Array<PrintingFormFieldDefinition>;
  readonly formSchemaKey: string;
}

export interface PrintingTemplatePreviewResponse {
  readonly boundFields: Readonly<Record<string, unknown>>;
  readonly formSchemaKey: string;
  readonly generatedAtUtc: string;
  readonly html: string;
  readonly templateId: string;
  readonly templateKey: string;
  readonly templateName: string;
  readonly versionNumber: number | string;
}

export interface PrintingTemplateResponse {
  readonly createdAtUtc: string;
  readonly formSchemaKey: string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly latestPublishedVersionNumber: number | string;
  readonly layoutHtml: string;
  readonly name: string;
  readonly templateKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface PrintingTemplateVersionResponse {
  readonly changeNote?: null | string;
  readonly id: string;
  readonly layoutHtml: string;
  readonly publishedAtUtc: string;
  readonly publishedByUserId: string;
  readonly templateId: string;
  readonly versionNumber: number | string;
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

export interface PublishGoViewProjectRequest {
  readonly changeNote?: null | string;
  readonly version: number | string;
}

export interface PublishHostAnnouncementRequest {
  readonly version: number;
}

export interface PublishHostReleaseNoteRequest {
  readonly version: number | string;
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

export interface PublishPrintingTemplateRequest {
  readonly changeNote?: null | string;
  readonly version: number | string;
}

export interface PublishReportingDefinitionRequest {
  readonly changeNote?: null | string;
  readonly version: number | string;
}

export interface PublishWorkflowDefinitionRequest {
  readonly expectedRevision: number;
  readonly formVersionId: string;
}

export interface PublishWorkflowFormRequest {
  readonly expectedRevision: number;
}

export interface ReassignWorkflowInstanceRequest {
  readonly assigneeUserId: string;
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: null | string;
}

export interface ReceivedHostAnnouncementDetailResponse {
  readonly audienceKind: string;
  readonly content: string;
  readonly id: string;
  readonly isRead: boolean;
  readonly kind: string;
  readonly publishedAtUtc: string;
  readonly publishedByUserId: null | string;
  readonly readAtUtc: null | string;
  readonly title: string;
}

export interface ReceivedHostAnnouncementListItemResponse {
  readonly audienceKind: string;
  readonly id: string;
  readonly isRead: boolean;
  readonly kind: string;
  readonly publishedAtUtc: string;
  readonly readAtUtc: null | string;
  readonly title: string;
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

export interface ReconcileWorkflowRecoveryTaskRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: null | string;
}

export interface RecoverWorkflowInstanceRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: string;
}

export interface ReplaceHostRoleFieldGrantsRequest {
  readonly fieldKeys: Array<string>;
  readonly resourceKey: string;
  readonly version: number;
}

export interface ReplaceHostRoleMembersRequest {
  readonly userIds: Array<string>;
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

export interface ReportingDataSourceListItem {
  readonly createdAtUtc: string;
  readonly hasPassword: boolean;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly lastTestedAtUtc?: null | string;
  readonly lastTestMessage?: null | string;
  readonly lastTestStatusKey?: null | string;
  readonly maskedDatabaseName: string;
  readonly maskedServerEndpoint: string;
  readonly maskedUsername: string;
  readonly name: string;
  readonly providerKey: string;
  readonly tenantId?: null | string;
  readonly trustServerCertificate: boolean;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface ReportingDataSourceResponse {
  readonly createdAtUtc: string;
  readonly databaseName: string;
  readonly hasPassword: boolean;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly lastTestedAtUtc?: null | string;
  readonly lastTestMessage?: null | string;
  readonly lastTestStatusKey?: null | string;
  readonly name: string;
  readonly port: number | string;
  readonly providerKey: string;
  readonly serverHost: string;
  readonly tenantId?: null | string;
  readonly trustServerCertificate: boolean;
  readonly updatedAtUtc?: null | string;
  readonly username: string;
  readonly version: number | string;
}

export interface ReportingDefinitionResponse {
  readonly createdAtUtc: string;
  readonly dataSourceId: string;
  readonly definitionKey: string;
  readonly description?: null | string;
  readonly groupId: string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly latestPublishedVersionNumber: number | string;
  readonly layoutConfigJson: string;
  readonly name: string;
  readonly parameterSchema: Array<ReportingParameterSchemaEntry>;
  readonly queryPortKey: string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface ReportingDefinitionVersionResponse {
  readonly changeNote?: null | string;
  readonly dataSourceId: string;
  readonly definitionId: string;
  readonly id: string;
  readonly layoutConfigJson: string;
  readonly parameterSchema: Array<ReportingParameterSchemaEntry>;
  readonly publishedAtUtc: string;
  readonly publishedByUserId: string;
  readonly queryPortKey: string;
  readonly versionNumber: number | string;
}

export interface ReportingExecutionColumnDefinition {
  readonly columnKey: string;
  readonly displayName: string;
}

export interface ReportingExecutionPageResponse {
  readonly columns: Array<ReportingExecutionColumnDefinition>;
  readonly commandTimeoutSeconds: number | string;
  readonly definitionId: string;
  readonly definitionKey: string;
  readonly definitionName: string;
  readonly executedAtUtc: string;
  readonly hasMore: boolean;
  readonly page: number | string;
  readonly pageSize: number | string;
  readonly queryPortKey: string;
  readonly rows: Array<ReportingExecutionRow>;
  readonly totalRows?: null | number | string;
  readonly versionNumber: number | string;
}

export interface ReportingExecutionParameterValue {
  readonly parameterKey: string;
  readonly value?: null | string;
}

export interface ReportingExecutionRow {
  readonly values: Readonly<Record<string, unknown>>;
}

export type ReportingExportTaskDetailResponse = ReportingExportTaskResponse & Readonly<Record<string, unknown>>;

export interface ReportingExportTaskResponse {
  readonly completedAtUtc?: null | string;
  readonly createdAtUtc: string;
  readonly definitionId: string;
  readonly definitionKey: string;
  readonly definitionName: string;
  readonly errorCode?: null | string;
  readonly errorMessage?: null | string;
  readonly formatKey: string;
  readonly id: string;
  readonly outputFileName?: null | string;
  readonly requestedByUserId: string;
  readonly rowCount: number | string;
  readonly statusKey: string;
  readonly versionNumber: number | string;
}

export interface ReportingGroupResponse {
  readonly createdAtUtc: string;
  readonly id: string;
  readonly isEnabled: boolean;
  readonly name: string;
  readonly parentId?: null | string;
  readonly sortOrder: number | string;
  readonly updatedAtUtc?: null | string;
  readonly version: number | string;
}

export interface ReportingParameterSchemaEntry {
  readonly dataTypeKey: string;
  readonly defaultValue?: null | string;
  readonly displayName: string;
  readonly isRequired: boolean;
  readonly parameterKey: string;
}

export interface ReportingQueryPortDefinition {
  readonly description: string;
  readonly displayName: string;
  readonly parameters: Array<ReportingQueryPortParameterDefinition>;
  readonly queryPortKey: string;
  readonly supportedProviderKeys: Array<string>;
}

export interface ReportingQueryPortParameterDefinition {
  readonly dataTypeKey: string;
  readonly defaultValue?: null | string;
  readonly displayName: string;
  readonly isRequired: boolean;
  readonly maximum?: null | number | string;
  readonly minimum?: null | number | string;
  readonly parameterKey: string;
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

export interface ResumeWorkflowInstanceRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: null | string;
}

export interface RetractHostReleaseNoteRequest {
  readonly version: number | string;
}

export interface RetryDataApprovalRequestBody {
  readonly version: number;
}

export interface RetryNotificationDeliveryRequest {
  readonly reason: string;
  readonly revision: number;
}

export interface RetryWorkflowRecoveryTaskRequest {
  readonly expectedRevision: number;
  readonly idempotencyKey: string;
  readonly reason: string;
}

export interface ReturnWorkflowTodoRequest {
  readonly comment: string;
  readonly expectedRevision: number;
  readonly fieldPatch: JsonElement;
  readonly idempotencyKey: string;
  readonly targetStepId: string;
}

export interface RevealHostUserProfileFieldsRequest {
  readonly fieldKeys: Array<string>;
}

export interface RevealHostUserProfileFieldsResponse {
  readonly values: Readonly<Record<string, unknown>>;
}

export interface RevokeAllHostUserSessionsResponse {
  readonly displayName: string;
  readonly revokedSessionCount: number;
  readonly userId: string;
  readonly username: string;
}

export interface RevokeSuperAdministratorRequest {
  readonly currentPassword: string;
  readonly totpCode?: null | string;
}

export interface RollbackHostDocumentVersionRequest {
  readonly version: number;
}

export interface SelfServiceProfileResponse {
  readonly accountType: string;
  readonly avatarFileId: null | string;
  readonly displayName: string;
  readonly profile: null | HostUserProfileResponse;
  readonly readableFieldKeys: Array<string>;
  readonly signatureFileId: null | string;
  readonly userId: string;
  readonly username: string;
  readonly userVersion: number;
  readonly writableFieldKeys: Array<string>;
}

export interface SendHostInboxMessageRequest {
  readonly content: string;
  readonly recipientUserId: string;
  readonly title: string;
}

export interface SendRecipientEndpointVerificationResponse {
  readonly expiresAtUtc: string;
  readonly resendAvailableAtUtc: string;
}

export interface SerialNumberPreviewResponse {
  readonly resetBucket: string;
  readonly sequenceValue: number;
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

export interface SerialRuleDisableApprovalPreviewResponse {
  readonly afterSnapshotJson: string;
  readonly beforeSnapshotJson: string;
  readonly displayName: string;
  readonly ruleId: string;
  readonly ruleKey: string;
  readonly version: number;
}

export interface SerialRuleDisableApprovalSubmissionResponse {
  readonly afterSnapshotJson: string;
  readonly beforeSnapshotJson?: string | null;
  readonly requestId: string;
  readonly requestVersion: number;
  readonly statusKey: string;
  readonly workflowDefinitionVersionId: string;
}

export interface SerialRuleFieldChange {
  readonly afterValue?: string | null;
  readonly beforeValue?: string | null;
  readonly changed: boolean;
  readonly fieldKey: string;
}

export interface SerialRuleUpdateApprovalPreviewResponse {
  readonly afterSnapshotJson: string;
  readonly beforeSnapshotJson: string;
  readonly changes: Array<SerialRuleFieldChange>;
  readonly displayName: string;
  readonly ruleId: string;
  readonly ruleKey: string;
}

export interface SerialRuleUpdateApprovalSubmissionResponse {
  readonly afterSnapshotJson: string;
  readonly beforeSnapshotJson?: string | null;
  readonly changes: Array<SerialRuleFieldChange>;
  readonly requestId: string;
  readonly requestVersion: number;
  readonly statusKey: string;
  readonly workflowDefinitionVersionId: string;
}

export interface ServerInstanceCatalogEntry {
  readonly displayName: string;
  readonly hostRole: string;
  readonly instanceKey: string;
  readonly isCurrent: boolean;
  readonly runtimeQueryability: string;
}

export interface ServerRuntimeMetric {
  readonly availability: string;
  readonly doubleValue?: number | null;
  readonly key: string;
  readonly label: string;
  readonly longValue?: number | null;
  readonly unavailableReason?: string | null;
  readonly unit?: string | null;
}

export interface ServerRuntimeSnapshot {
  readonly applicationVersion: string;
  readonly capturedAtUtc: string;
  readonly displayName: string;
  readonly frameworkDescription: string;
  readonly hostRole: string;
  readonly instanceKey: string;
  readonly machineName: string;
  readonly metrics: Array<ServerRuntimeMetric>;
  readonly operatingSystemDescription: string;
  readonly processArchitecture: string;
  readonly processId: number;
  readonly processStartedAtUtc: string;
  readonly uptimeSeconds: number;
}

export interface SetHostDocumentPermissionsRequest {
  readonly documentId: string;
  readonly permissions: Array<HostDocumentPermissionEntry>;
}

export interface SetNotificationProviderProfileEnabledRequest {
  readonly version: number;
}

export interface SetPersonalScheduleStatusRequest {
  readonly status: string;
  readonly version: number;
}

export interface SetWorkflowDefinitionStatusRequest {
  readonly expectedVersion: number;
  readonly statusKey: string;
}

export interface SetWorkflowFormStatusRequest {
  readonly expectedVersion: number;
  readonly statusKey: string;
}

export interface StartWorkflowInstanceRequest {
  readonly businessId: string;
  readonly businessTitle?: null | string;
  readonly businessType: string;
  readonly definitionVersionId: string;
  readonly idempotencyKey: string;
  readonly initialValues: JsonElement;
}

export interface StaticImportRowPreviewResult {
  readonly errorCode?: null | string;
  readonly isValid: boolean;
  readonly lineNumber: number | string;
  readonly message?: null | string;
}

export interface StaticImportSchemaDefinition {
  readonly displayName: string;
  readonly requiredPermission: string;
  readonly schemaKey: string;
  readonly scopeKey: string;
  readonly worksheets: Array<StaticImportWorksheetDefinition>;
}

export interface StaticImportWorksheetDefinition {
  readonly displayName: string;
  readonly headerColumns: Array<string>;
  readonly worksheetKey: string;
}

export type Stream = Blob;

export interface StreamAiChatMessageRequest {
  readonly content: string;
}

export interface SubmitSerialRuleDisableApprovalRequest {
  readonly idempotencyKey: string;
  readonly statusChange: ChangeSerialNumberRuleStatusRequest;
}

export interface SubmitSerialRuleUpdateApprovalRequest {
  readonly idempotencyKey: string;
  readonly update: UpdateSerialNumberRuleRequest;
}

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

export interface TenantBrandingResponse {
  readonly contactAddress: null | string;
  readonly contactEmail: null | string;
  readonly contactPhone: null | string;
  readonly copyright: null | string;
  readonly logoFileId: null | string;
  readonly systemTitle: null | string;
  readonly tenantId: string;
  readonly version: number;
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

export interface TenantRuntimeBrandingResponse {
  readonly contactAddress: null | string;
  readonly contactEmail: null | string;
  readonly contactPhone: null | string;
  readonly copyright: null | string;
  readonly hasLogo: boolean;
  readonly systemTitle: null | string;
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

export interface TestAiModelConfigResult {
  readonly message: string;
  readonly succeeded: boolean;
}

export interface TestK3CloudConnectionConfigResult {
  readonly message: string;
  readonly succeeded: boolean;
}

export interface TestOcrProviderConfigResult {
  readonly message: string;
  readonly succeeded: boolean;
}

export interface TestReportingDataSourceResult {
  readonly message: string;
  readonly succeeded: boolean;
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

export interface UpdateAdministrativeRegionRequest {
  readonly cityCode?: string;
  readonly displayOrder?: number;
  readonly latitude?: number;
  readonly level?: number;
  readonly longitude?: number;
  readonly mergerName?: string;
  readonly name?: string;
  readonly parentId?: string;
  readonly pinYin?: string;
  readonly regionType?: string;
  readonly remark?: string;
  readonly shortName?: string;
  readonly version?: number;
  readonly zipCode?: string;
}

export interface UpdateAiChatSessionRequest {
  readonly title: string;
  readonly version: number | string;
}

export interface UpdateAiModelConfigRequest {
  readonly apiKey?: null | string;
  readonly clearApiKey: boolean;
  readonly endpointBaseUrl: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly modelId: string;
  readonly name: string;
  readonly organizationId?: null | string;
  readonly providerKey: string;
  readonly version: number | string;
}

export interface UpdateAiTenantQuotaRequest {
  readonly isEnabled: boolean;
  readonly monthlyRequestLimit?: null | number | string;
  readonly monthlyTokenLimit?: null | number | string;
  readonly version: number | string;
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

export interface UpdateDataApprovalScenarioBindingBody {
  readonly isEnabled: boolean;
  readonly version?: number | null;
  readonly workflowDefinitionVersionId?: string | null;
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

export interface UpdateGoViewProjectRequest {
  readonly canvasJson: string;
  readonly isEnabled: boolean;
  readonly name: string;
  readonly version: number | string;
}

export interface UpdateHostAnnouncementRequest {
  readonly audienceKind?: null | string;
  readonly content: string;
  readonly kind?: null | string;
  readonly targetOrganizations?: null | Array<HostAnnouncementTargetOrganization>;
  readonly targetUserIds?: null | Array<string>;
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

export interface UpdateHostFileMetadataRequest {
  readonly expectedRevision: number | string;
  readonly folderId: null | string;
  readonly originalFileName: string;
}

export interface UpdateHostFolderRequest {
  readonly displayOrder: number;
  readonly expectedRevision: number | string;
  readonly name: string;
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

export interface UpdateHostReleaseNoteRequest {
  readonly content: string;
  readonly title: string;
  readonly version: number | string;
  readonly versionLabel: string;
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

export interface UpdateK3CloudConnectionConfigRequest {
  readonly acctId: string;
  readonly baseUrl: string;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly lcid: number | string;
  readonly name: string;
  readonly password?: null | string;
  readonly username: string;
  readonly version: number | string;
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

export interface UpdateOcrProviderConfigRequest {
  readonly apiKey?: null | string;
  readonly baseUrl: string;
  readonly isEnabled: boolean;
  readonly name: string;
  readonly version: number | string;
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

export interface UpdatePaymentMerchantConfigRequest {
  readonly apiV3Key?: null | string;
  readonly appId: string;
  readonly certificateSerialNo: string;
  readonly channelKey: string;
  readonly clearApiV3Key: boolean;
  readonly clearPrivateKey: boolean;
  readonly isDefault: boolean;
  readonly isEnabled: boolean;
  readonly merchantId: string;
  readonly name: string;
  readonly notifyUrl: string;
  readonly privateKeyPem?: null | string;
  readonly returnUrl: string;
  readonly version: number | string;
}

export interface UpdatePersonalScheduleRequest {
  readonly content: string;
  readonly endAtUtc: string;
  readonly startAtUtc: string;
  readonly version: number;
}

export interface UpdatePrintingTemplateRequest {
  readonly isEnabled: boolean;
  readonly layoutHtml: string;
  readonly name: string;
  readonly version: number | string;
}

export interface UpdateReportingDataSourceRequest {
  readonly databaseName: string;
  readonly isEnabled: boolean;
  readonly name: string;
  readonly password?: null | string;
  readonly port: number | string;
  readonly providerKey: string;
  readonly serverHost: string;
  readonly trustServerCertificate: boolean;
  readonly username: string;
  readonly version: number | string;
}

export interface UpdateReportingDefinitionRequest {
  readonly dataSourceId: string;
  readonly description?: null | string;
  readonly groupId: string;
  readonly isEnabled: boolean;
  readonly layoutConfigJson?: null | string;
  readonly name: string;
  readonly parameterSchema: Array<ReportingParameterSchemaEntry>;
  readonly queryPortKey: string;
  readonly version: number | string;
}

export interface UpdateReportingGroupRequest {
  readonly isEnabled: boolean;
  readonly name: string;
  readonly parentId?: null | string;
  readonly sortOrder: number | string;
  readonly version: number | string;
}

export interface UpdateSelfServiceProfileRequest {
  readonly displayName?: null | string;
  readonly profile?: null | HostUserProfileWriteRequest;
  readonly userVersion?: null | number;
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

export interface UpdateTenantBrandingRequest {
  readonly contactAddress?: null | string;
  readonly contactEmail?: null | string;
  readonly contactPhone?: null | string;
  readonly copyright?: null | string;
  readonly systemTitle?: null | string;
  readonly version: number;
}

export interface UpdateWorkflowDefinitionDraftRequest {
  readonly businessTitleTemplate?: null | string;
  readonly draft: WorkflowDefinitionDraft;
  readonly expectedRevision: number;
}

export interface UpdateWorkflowFormDraftRequest {
  readonly draft: WorkflowFormSchema;
  readonly expectedRevision: number;
}

export interface VerifyRecipientEndpointCodeRequest {
  readonly code: string;
}

export interface WeChatPayNotifyAckResponse {
  readonly code: string;
  readonly message: string;
}

export interface WorkflowAssigneePreviewResponse {
  readonly users: Array<WorkflowRecipientCandidateResponse>;
}

export interface WorkflowCcReadResponse {
  readonly id: string;
  readonly readAtUtc: string;
}

export interface WorkflowCcResponse {
  readonly businessId: string;
  readonly businessTitle?: null | string;
  readonly businessType: string;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly instanceId: string;
  readonly nodeKey: string;
  readonly readAtUtc: null | string;
  readonly stepId: null | string;
}

export interface WorkflowDefinitionDraft {
  readonly nodes: Array<WorkflowNodeDraft>;
  readonly schemaVersion: number;
}

export interface WorkflowDefinitionResponse {
  readonly businessTitleTemplate?: null | string;
  readonly createdAtUtc: string;
  readonly definitionKey: string;
  readonly draft: WorkflowDefinitionDraft;
  readonly draftRevision: number;
  readonly id: string;
  readonly latestPublishedVersionId: null | string;
  readonly statusKey: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
}

export interface WorkflowDefinitionVersionResponse {
  readonly businessTitleTemplate?: null | string;
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
  readonly statusKey: string;
  readonly updatedAtUtc: null | string;
  readonly version: number;
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

export interface WorkflowInstanceListItemResponse {
  readonly businessId: string;
  readonly businessTitle?: null | string;
  readonly businessType: string;
  readonly completedAtUtc: null | string;
  readonly definitionKey: string;
  readonly definitionVersionId: string;
  readonly id: string;
  readonly startedAtUtc: string;
  readonly startedById: string;
  readonly statusKey: string;
}

export interface WorkflowInstanceResponse {
  readonly activeNodeKey?: null | string;
  readonly activeTodoId: null | string;
  readonly approvalModeKey?: null | string;
  readonly approvedCount?: number | null;
  readonly businessId: string;
  readonly businessTitle?: null | string;
  readonly businessType: string;
  readonly definitionVersionId: string;
  readonly dueAtUtc?: null | string;
  readonly escalatedAtUtc?: null | string;
  readonly formVersionId: string;
  readonly id: string;
  readonly pendingCount?: number | null;
  readonly rejectedCount?: number | null;
  readonly reminderCount?: number;
  readonly requiredApprovalCount?: number | null;
  readonly revision: number;
  readonly startedAtUtc: string;
  readonly statusKey: string;
  readonly timeoutStatusKey?: string;
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

export interface WorkflowRecipientCandidatePageResponse {
  readonly items: Array<WorkflowRecipientCandidateResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface WorkflowRecipientCandidateResponse {
  readonly displayName: string;
  readonly id: string;
  readonly username: string;
}

export interface WorkflowRecoveryTaskResponse {
  readonly attemptCount: number;
  readonly createdAtUtc: string;
  readonly id: string;
  readonly instanceId: string;
  readonly kindKey: string;
  readonly lastError: null | string;
  readonly leaseExpiresAtUtc: null | string;
  readonly leaseGeneration: number;
  readonly leaseOwnerKey: null | string;
  readonly nextAttemptAtUtc: null | string;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: null | string;
  readonly updatedAtUtc: string;
}

export interface WorkflowTodoDetailResponse {
  readonly approvalModeKey: string;
  readonly approvedCount: number;
  readonly assigneeUserId: string;
  readonly fieldPolicies: Readonly<Record<string, unknown>>;
  readonly formSchema: JsonElement;
  readonly formVersionId: string;
  readonly id: string;
  readonly instanceId: string;
  readonly pendingCount: number;
  readonly rejectedCount: number;
  readonly requiredApprovalCount: number;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: string;
  readonly submission: JsonElement;
  readonly submissionRevision: number;
}

export interface WorkflowTodoListItemResponse {
  readonly arrivedAtUtc: string;
  readonly businessId: string;
  readonly businessTitle?: null | string;
  readonly businessType: string;
  readonly completedAtUtc: null | string;
  readonly definitionKey: string;
  readonly id: string;
  readonly instanceId: string;
  readonly instanceStatusKey: string;
  readonly nodeKey: string;
  readonly resultActionKey: null | string;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: string;
}

export interface WorkflowTodoReturnTargetResponse {
  readonly assigneeUserId: string;
  readonly completedAtUtc: string;
  readonly nodeKey: string;
  readonly stepId: string;
}

export interface WorkflowTodoRuntimeResponse {
  readonly approvalModeKey: string;
  readonly approvedCount: number;
  readonly assigneeUserId: string;
  readonly fieldPolicies: Readonly<Record<string, unknown>>;
  readonly formSchema: JsonElement;
  readonly formSchemaHash: string;
  readonly formVersionId: string;
  readonly id: string;
  readonly instanceId: string;
  readonly pendingCount: number;
  readonly rejectedCount: number;
  readonly requiredApprovalCount: number;
  readonly revision: number;
  readonly statusKey: string;
  readonly stepId: string;
  readonly submission: JsonElement;
  readonly submissionRevision: number;
}
