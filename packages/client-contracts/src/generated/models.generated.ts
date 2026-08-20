// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 数据模型。

export interface BatchDeleteConfigEntriesRequest {
  readonly ids: ReadonlyArray<string>;
}

export interface BatchHostUserIdsRequest {
  readonly userIds: ReadonlyArray<string>;
}

export interface BatchHostUserStatusItem {
  readonly errorCode: null | string;
  readonly message: null | string;
  readonly succeeded: boolean;
  readonly userId: string;
}

export interface BatchHostUserStatusResponse {
  readonly results: ReadonlyArray<BatchHostUserStatusItem>;
  readonly succeededCount: number;
}

export interface BatchUpdateConfigValuesRequest {
  readonly updates: ReadonlyArray<ConfigValueUpdate>;
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
  readonly valueKind: string;
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
  readonly valueKind: string;
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

export interface HostFileResponse {
  readonly contentHash: null | string;
  readonly contentType: string;
  readonly createdAtUtc: string;
  readonly createdByUserId: string;
  readonly id: string;
  readonly originalFileName: string;
  readonly sizeBytes: number;
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
  readonly fieldKeys: null | ReadonlyArray<string>;
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
  readonly effectiveFieldKeys: ReadonlyArray<string>;
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
  readonly roleIds: ReadonlyArray<string>;
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
  readonly rows: ReadonlyArray<CreateHostUserRequest>;
}

export interface ImportHostUsersResponse {
  readonly results: ReadonlyArray<ImportHostUserRowResult>;
  readonly succeededCount: number;
}

export interface PagedResultOfConfigEntryResponse {
  readonly items: ReadonlyArray<ConfigEntryResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostFileResponse {
  readonly items: ReadonlyArray<HostFileResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfHostUserResponse {
  readonly items: ReadonlyArray<HostUserResponse>;
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

export interface ReplaceHostUserRolesRequest {
  readonly roleIds: ReadonlyArray<string>;
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

export interface UpdateHostUserRequest {
  readonly accountType?: null | string;
  readonly displayName: string;
  readonly profile?: null | HostUserProfileWriteRequest;
  readonly version: number;
}
