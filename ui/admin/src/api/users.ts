import {
  identityBatchDisableHostUsers,
  identityBatchEnableHostUsers,
  identityCreateHostUser,
  identityDisableHostUser,
  identityDownloadHostUserImportTemplate,
  identityEnableHostUser,
  identityExportHostUsers,
  identityExportHostUsersWorkbook,
  identityGetHostUserRoles,
  identityImportHostUsers,
  identityImportHostUsersWorkbook,
  identityListHostUsers,
  identityReplaceHostUserRoles,
  identityResetHostUserPassword,
  identityUpdateHostUser,
  type HostUser,
  type HostUserPage,
  type HostUserProfileWrite,
  type HostUserProfileWriteRequest,
  type HostUserRoles,
  type ImportHostUsersResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询 Host 用户列表。 */
export async function listHostUsers(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostUserPage> {
  return identityListHostUsers(http, { page, pageSize }, signal);
}

/** 导出 Host 用户列表的结构化数据。 */
export async function exportHostUsers(signal?: AbortSignal): Promise<HostUser[]> {
  return identityExportHostUsers(http, {}, signal);
}

/** 下载 Host 用户导入模板文件。 */
export async function downloadHostUserImportTemplate(
  signal?: AbortSignal
): Promise<Blob> {
  return identityDownloadHostUserImportTemplate(http, {}, signal);
}

/** 导出 Host 用户 Excel 工作簿。 */
export async function exportHostUsersWorkbook(
  signal?: AbortSignal
): Promise<Blob> {
  return identityExportHostUsersWorkbook(http, {}, signal);
}

/** 导入 Host 用户 Excel 工作簿。 */
export async function importHostUsersWorkbook(
  file: File,
  signal?: AbortSignal
): Promise<ImportHostUsersResponse> {
  return identityImportHostUsersWorkbook(http, { file }, signal);
}

/** 按简化行模型批量导入 Host 用户，并只返回页面真正关心的成功数量。 */
export async function importHostUsers(
  rows: Array<{
    username: string;
    displayName: string;
    password: string;
    accountType?: string | null;
  }>,
  signal?: AbortSignal
): Promise<{ succeededCount: number }> {
  const result = await identityImportHostUsers(http, { body: { rows } }, signal);
  return { succeededCount: result.succeededCount };
}

/** 批量禁用 Host 用户。 */
export async function batchDisableHostUsers(
  userIds: string[],
  signal?: AbortSignal
): Promise<{ succeededCount: number }> {
  const result = await identityBatchDisableHostUsers(
    http,
    { body: { userIds } },
    signal
  );
  return { succeededCount: result.succeededCount };
}

/** 批量启用 Host 用户。 */
export async function batchEnableHostUsers(
  userIds: string[],
  signal?: AbortSignal
): Promise<{ succeededCount: number }> {
  const result = await identityBatchEnableHostUsers(
    http,
    { body: { userIds } },
    signal
  );
  return { succeededCount: result.succeededCount };
}

/** 创建 Host 用户，并把前端局部档案投影为服务端写模型。 */
export async function createHostUser(
  username: string,
  displayName: string,
  password: string,
  profile?: HostUserProfileWrite | null,
  accountType?: string | null,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityCreateHostUser(http, {
    body: {
      username,
      displayName,
      password,
      accountType: accountType ?? undefined,
      profile: normalizeHostUserProfile(profile)
    }
  }, signal);
}

/** 禁用指定 Host 用户。 */
export async function disableHostUser(
  id: string,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityDisableHostUser(http, { userId: id }, signal);
}

/** 启用指定 Host 用户。 */
export async function enableHostUser(
  id: string,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityEnableHostUser(http, { userId: id }, signal);
}

/** 更新 Host 用户基础信息与档案，并携带版本号维持乐观并发。 */
export async function updateHostUser(
  id: string,
  displayName: string,
  version: number,
  profile?: HostUserProfileWrite | null,
  accountType?: string | null,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityUpdateHostUser(http, {
    userId: id,
    body: {
      displayName,
      version,
      accountType: accountType ?? undefined,
      profile: normalizeHostUserProfile(profile)
    }
  }, signal);
}

/** 重置 Host 用户密码。 */
export async function resetHostUserPassword(
  id: string,
  password: string,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityResetHostUserPassword(
    http,
    { userId: id, body: { password } },
    signal
  );
}

/** 查询 Host 用户当前角色集合。 */
export async function getHostUserRoles(
  id: string,
  signal?: AbortSignal
): Promise<HostUserRoles> {
  return identityGetHostUserRoles(http, { userId: id }, signal);
}

/** 用新角色集整体替换 Host 用户角色分配。 */
export async function replaceHostUserRoles(
  id: string,
  roleIds: string[],
  version: number,
  signal?: AbortSignal
): Promise<HostUserRoles> {
  return identityReplaceHostUserRoles(
    http,
    { userId: id, body: { roleIds, version } },
    signal
  );
}

/** 将兼容层的局部档案输入补齐为 OpenAPI 请求模型；未声明字段保持 null 语义。 */
function normalizeHostUserProfile(
  profile?: HostUserProfileWrite | null
): HostUserProfileWriteRequest | null | undefined {
  if (profile === undefined || profile === null) {
    return profile;
  }

  return {
    address: profile.address ?? null,
    birthDate: profile.birthDate ?? null,
    educationLevel: profile.educationLevel ?? null,
    email: profile.email ?? null,
    emergencyContact: profile.emergencyContact ?? null,
    emergencyContactAddress: profile.emergencyContactAddress ?? null,
    emergencyContactPhone: profile.emergencyContactPhone ?? null,
    emergencyContactRelation: profile.emergencyContactRelation ?? null,
    employeeNumber: profile.employeeNumber ?? null,
    ethnicity: profile.ethnicity ?? null,
    fieldKeys: profile.fieldKeys ?? null,
    gender: profile.gender ?? null,
    graduatedSchool: profile.graduatedSchool ?? null,
    idCardNumber: profile.idCardNumber ?? null,
    idCardType: profile.idCardType ?? null,
    joinDateUtc: profile.joinDateUtc ?? null,
    nickname: profile.nickname ?? null,
    officePhone: profile.officePhone ?? null,
    phoneNumber: profile.phoneNumber ?? null,
    politicalStatus: profile.politicalStatus ?? null,
    remark: profile.remark ?? null,
    sortOrder: profile.sortOrder ?? null,
    version: profile.version ?? null
  };
}

/** 导出用户、档案写模型、角色分配与导入结果模型，供列表页、编辑页、角色分配弹窗与导入流程共享同一契约。 */
export type {
  HostUser,
  HostUserPage,
  HostUserProfileWrite,
  HostUserProfileWriteRequest,
  HostUserRoles,
  ImportHostUsersResponse
};
