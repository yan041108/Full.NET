// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 数据模型。

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

export interface CreateConfigEntryRequest {
  readonly configKey: string;
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly groupName: null | string;
  readonly value: string;
  readonly valueKind: "string" | "boolean" | "integer" | "decimal" | "json" | "secret";
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

export interface CreateHostUserRequest {
  readonly accountType?: null | string;
  readonly displayName: string;
  readonly password: string;
  readonly profile?: null | HostUserProfileWriteRequest;
  readonly username: string;
}

export interface DeleteConfigEntryRequest {
  readonly version: number;
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

export interface HostFileResponse {
  readonly contentHash: null | string;
  readonly contentType: string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly id: string;
  readonly originalFileName: string;
  readonly sizeBytes: number;
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

export interface PagedResultOfConfigEntryResponse {
  readonly items: Array<ConfigEntryResponse>;
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

export interface PagedResultOfHostMenuResponse {
  readonly items: Array<HostMenuResponse>;
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

export interface ProblemDetails {
  readonly detail?: null | string;
  readonly instance?: null | string;
  readonly status?: null | number;
  readonly title?: null | string;
  readonly type?: null | string;
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

export type Stream = Blob;

export interface UpdateConfigEntryRequest {
  readonly description: null | string;
  readonly displayName: string;
  readonly displayOrder: number;
  readonly groupName: null | string;
  readonly value: string;
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

export interface UpdateHostUserRequest {
  readonly accountType?: null | string;
  readonly displayName: string;
  readonly profile?: null | HostUserProfileWriteRequest;
  readonly version: number;
}
