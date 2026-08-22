// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 低层 HttpClient Operation。

import type { HttpClient, RequestOptions } from '../http.js';
import type {
  AssignHostTenantPackageRequest,
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
  LocalePreferenceResponse,
  LoginRequest,
  ModuleCatalogEntryResponse,
  PagedResultOfConfigEntryResponse,
  PagedResultOfHostApiKeyResponse,
  PagedResultOfHostFileResponse,
  PagedResultOfHostMenuResponse,
  PagedResultOfHostOnlineSessionResponse,
  PagedResultOfHostRoleResponse,
  PagedResultOfHostUserResponse,
  PagedResultOfTenantSummary,
  ProblemDetails,
  ProvisionTenantRequest,
  ReplaceHostRoleFieldGrantsRequest,
  ReplaceHostRolePermissionsRequest,
  ReplaceHostUserRolesRequest,
  ResetHostUserPasswordRequest,
  RevokeSuperAdministratorRequest,
  Stream,
  SuperAdministratorAuditResponse,
  SuperAdministratorChangeResponse,
  SuperAdministratorResponse,
  TenantSummary,
  TokenResponse,
  TotpEnrollmentStatusResponse,
  UpdateConfigEntryRequest,
  UpdateHostMenuRequest,
  UpdateHostRoleDataScopeRequest,
  UpdateHostRoleRequest,
  UpdateHostTenantRequest,
  UpdateHostUserRequest,
  UpdateLocaleRequest
} from './models.generated.js';
import {
  readBatchHostUserStatusResponse,
  readBeginTotpEnrollmentResponse,
  readConfigEntryResponse,
  readCreateHostApiKeyResponse,
  readCurrentUserResponse,
  readHostApiKeyResponse,
  readHostFileResponse,
  readHostMenuResponse,
  readHostNavigationCatalogSyncResponse,
  readHostOnlineSessionResponse,
  readHostRoleDataScopeResponse,
  readHostRoleFieldGrantsResponse,
  readHostRoleResponse,
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
  readLocalePreferenceResponse,
  readModuleCatalogEntryResponse,
  readPagedResultOfConfigEntryResponse,
  readPagedResultOfHostApiKeyResponse,
  readPagedResultOfHostFileResponse,
  readPagedResultOfHostMenuResponse,
  readPagedResultOfHostOnlineSessionResponse,
  readPagedResultOfHostRoleResponse,
  readPagedResultOfHostUserResponse,
  readPagedResultOfTenantSummary,
  readSettingsBatchUpdateHostConfigEntryValuesResponse,
  readSettingsListAllHostConfigEntriesResponse,
  readSettingsListHostConfigEntryGroupsResponse,
  readSuperAdministratorChangeResponse,
  readTenantSummary,
  readTokenResponse,
  readTotpEnrollmentStatusResponse
} from './guards.generated.js';

export type GeneratedJsonOperation<T> = (
  http: HttpClient,
  parameters: Readonly<Record<string, unknown>>,
  signal?: AbortSignal,
  options?: RequestOptions
) => Promise<T>;

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
