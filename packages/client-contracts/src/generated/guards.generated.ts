// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 运行时响应守卫。

import type {
  AuthorizationTreeActionResponse,
  AuthorizationTreeModuleResponse,
  AuthorizationTreePageResponse,
  BatchDeleteConfigEntriesRequest,
  BatchHostUserIdsRequest,
  BatchHostUserStatusItem,
  BatchHostUserStatusResponse,
  BatchUpdateConfigValuesRequest,
  BeginTotpEnrollmentResponse,
  ConfigEntryResponse,
  ConfigValueUpdate,
  ConfirmTotpEnrollmentRequest,
  CreateConfigEntryRequest,
  CreateHostApiKeyRequest,
  CreateHostApiKeyResponse,
  CreateHostMenuRequest,
  CreateHostRoleRequest,
  CreateHostUserRequest,
  CurrentUserResponse,
  DeleteConfigEntryRequest,
  FieldProjectionDefaultVisibility,
  FieldProjectionFieldDefinition,
  FieldProjectionResourceDefinition,
  FieldProjectionSensitivity,
  GrantSuperAdministratorRequest,
  HostApiKeyResponse,
  HostFileResponse,
  HostMenuPermissionOptionResponse,
  HostMenuResponse,
  HostNavigationCatalogSyncResponse,
  HostOnlineSessionResponse,
  HostRoleDataScopeResponse,
  HostRoleFieldGrantsResponse,
  HostRoleResponse,
  HostUserProfileResponse,
  HostUserProfileWriteRequest,
  HostUserProjectedFieldsResponse,
  HostUserResponse,
  HostUserRolesResponse,
  IFormFile,
  ImportHostUserRowResult,
  ImportHostUsersRequest,
  ImportHostUsersResponse,
  ModuleCatalogEntryResponse,
  PagedResultOfConfigEntryResponse,
  PagedResultOfHostApiKeyResponse,
  PagedResultOfHostFileResponse,
  PagedResultOfHostMenuResponse,
  PagedResultOfHostOnlineSessionResponse,
  PagedResultOfHostRoleResponse,
  PagedResultOfHostUserResponse,
  ProblemDetails,
  ReplaceHostRoleFieldGrantsRequest,
  ReplaceHostRolePermissionsRequest,
  ReplaceHostUserRolesRequest,
  ResetHostUserPasswordRequest,
  RevokeSuperAdministratorRequest,
  Stream,
  SuperAdministratorAuditResponse,
  SuperAdministratorChangeResponse,
  SuperAdministratorResponse,
  TotpEnrollmentStatusResponse,
  UpdateConfigEntryRequest,
  UpdateHostMenuRequest,
  UpdateHostRoleDataScopeRequest,
  UpdateHostRoleRequest,
  UpdateHostUserRequest
} from './models.generated.js';

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

export function readCreateConfigEntryRequest(value: unknown): CreateConfigEntryRequest {
  if (!(isCreateConfigEntryRequest(value))) {
    throw new Error('client.invalid_create_config_entry_request');
  }
  return value;
}

function isCreateConfigEntryRequest(value: unknown): value is CreateConfigEntryRequest {
  return isRecord(value) && (typeof value["configKey"] === 'string') && ((value["description"] === null) || (typeof value["description"] === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["displayOrder"] === 'number' && Number.isInteger(value["displayOrder"])) && ((value["groupName"] === null) || (typeof value["groupName"] === 'string')) && (typeof value["value"] === 'string') && (typeof value["valueKind"] === 'string' && ["string", "boolean", "integer", "decimal", "json", "secret"].includes(value["valueKind"]));
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

export function readCreateHostUserRequest(value: unknown): CreateHostUserRequest {
  if (!(isCreateHostUserRequest(value))) {
    throw new Error('client.invalid_create_host_user_request');
  }
  return value;
}

function isCreateHostUserRequest(value: unknown): value is CreateHostUserRequest {
  return isRecord(value) && (value["accountType"] === undefined || ((value["accountType"] === null) || (typeof value["accountType"] === 'string'))) && (typeof value["displayName"] === 'string') && (typeof value["password"] === 'string') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"])))) && (typeof value["username"] === 'string');
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

export function readDeleteConfigEntryRequest(value: unknown): DeleteConfigEntryRequest {
  if (!(isDeleteConfigEntryRequest(value))) {
    throw new Error('client.invalid_delete_config_entry_request');
  }
  return value;
}

function isDeleteConfigEntryRequest(value: unknown): value is DeleteConfigEntryRequest {
  return isRecord(value) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
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

export function readHostApiKeyResponse(value: unknown): HostApiKeyResponse {
  if (!(isHostApiKeyResponse(value))) {
    throw new Error('client.invalid_host_api_key_response');
  }
  return value;
}

function isHostApiKeyResponse(value: unknown): value is HostApiKeyResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["displayName"] === 'string') && ((value["expiresAtUtc"] === null) || (typeof value["expiresAtUtc"] === 'string')) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["keyPrefix"] === 'string') && ((value["lastUsedAtUtc"] === null) || (typeof value["lastUsedAtUtc"] === 'string')) && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => typeof item20 === 'string')) && (typeof value["userId"] === 'string' && guidPattern.test(value["userId"])) && (typeof value["username"] === 'string');
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

export function readModuleCatalogEntryResponse(value: unknown): ModuleCatalogEntryResponse {
  if (!(isModuleCatalogEntryResponse(value))) {
    throw new Error('client.invalid_module_catalog_entry_response');
  }
  return value;
}

function isModuleCatalogEntryResponse(value: unknown): value is ModuleCatalogEntryResponse {
  return isRecord(value) && (Array.isArray(value["dependencies"]) && value["dependencies"].every(item21 => typeof item21 === 'string')) && (typeof value["displayName"] === 'string') && (typeof value["healthCapability"] === 'string') && (Array.isArray(value["hostProfiles"]) && value["hostProfiles"].every(item21 => typeof item21 === 'string')) && (typeof value["moduleKey"] === 'string') && (typeof value["sourceClassification"] === 'string') && (typeof value["version"] === 'string');
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

export function readPagedResultOfHostApiKeyResponse(value: unknown): PagedResultOfHostApiKeyResponse {
  if (!(isPagedResultOfHostApiKeyResponse(value))) {
    throw new Error('client.invalid_paged_result_of_host_api_key_response');
  }
  return value;
}

function isPagedResultOfHostApiKeyResponse(value: unknown): value is PagedResultOfHostApiKeyResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isHostApiKeyResponse(item14))) && (typeof value["page"] === 'number' && Number.isInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isInteger(value["total"]));
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

export function readProblemDetails(value: unknown): ProblemDetails {
  if (!(isProblemDetails(value))) {
    throw new Error('client.invalid_problem_details');
  }
  return value;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return isRecord(value) && (value["detail"] === undefined || ((value["detail"] === null) || (typeof value["detail"] === 'string'))) && (value["instance"] === undefined || ((value["instance"] === null) || (typeof value["instance"] === 'string'))) && (value["status"] === undefined || ((value["status"] === null) || (typeof value["status"] === 'number' && Number.isInteger(value["status"])))) && (value["title"] === undefined || ((value["title"] === null) || (typeof value["title"] === 'string'))) && (value["type"] === undefined || ((value["type"] === null) || (typeof value["type"] === 'string')));
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

export function readRevokeSuperAdministratorRequest(value: unknown): RevokeSuperAdministratorRequest {
  if (!(isRevokeSuperAdministratorRequest(value))) {
    throw new Error('client.invalid_revoke_super_administrator_request');
  }
  return value;
}

function isRevokeSuperAdministratorRequest(value: unknown): value is RevokeSuperAdministratorRequest {
  return isRecord(value) && (typeof value["currentPassword"] === 'string') && (value["totpCode"] === undefined || ((value["totpCode"] === null) || (typeof value["totpCode"] === 'string')));
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

export function readTotpEnrollmentStatusResponse(value: unknown): TotpEnrollmentStatusResponse {
  if (!(isTotpEnrollmentStatusResponse(value))) {
    throw new Error('client.invalid_totp_enrollment_status_response');
  }
  return value;
}

function isTotpEnrollmentStatusResponse(value: unknown): value is TotpEnrollmentStatusResponse {
  return isRecord(value) && (typeof value["isEnabled"] === 'boolean') && (typeof value["isEnrolled"] === 'boolean');
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

export function readUpdateHostUserRequest(value: unknown): UpdateHostUserRequest {
  if (!(isUpdateHostUserRequest(value))) {
    throw new Error('client.invalid_update_host_user_request');
  }
  return value;
}

function isUpdateHostUserRequest(value: unknown): value is UpdateHostUserRequest {
  return isRecord(value) && (value["accountType"] === undefined || ((value["accountType"] === null) || (typeof value["accountType"] === 'string'))) && (typeof value["displayName"] === 'string') && (value["profile"] === undefined || ((value["profile"] === null) || (isHostUserProfileWriteRequest(value["profile"])))) && (typeof value["version"] === 'number' && Number.isInteger(value["version"]));
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

export function readSettingsListHostConfigEntryGroupsResponse(value: unknown): Array<string> {
  if (!(Array.isArray(value) && value.every(item5 => typeof item5 === 'string'))) {
    throw new Error('client.invalid_settings_list_host_config_entry_groups_response');
  }
  return value as Array<string>;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
