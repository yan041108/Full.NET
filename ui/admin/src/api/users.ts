import {
  identityBatchDisableHostUsers,
  identityBatchEnableHostUsers,
  identityCreateHostUser,
  identityDisableHostUser,
  identityEnableHostUser,
  identityExportHostUsers,
  identityGetHostUserRoles,
  identityImportHostUsers,
  identityListHostUsers,
  identityReplaceHostUserRoles,
  identityResetHostUserPassword,
  identityUpdateHostUser,
  type HostUser,
  type HostUserPage,
  type HostUserProfileWrite,
  type HostUserProfileWriteRequest,
  type HostUserRoles
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listHostUsers(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostUserPage> {
  return identityListHostUsers(http, { page, pageSize }, signal);
}

export async function exportHostUsers(signal?: AbortSignal): Promise<HostUser[]> {
  return identityExportHostUsers(http, {}, signal);
}

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

export async function disableHostUser(
  id: string,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityDisableHostUser(http, { userId: id }, signal);
}

export async function enableHostUser(
  id: string,
  signal?: AbortSignal
): Promise<HostUser> {
  return identityEnableHostUser(http, { userId: id }, signal);
}

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

export async function getHostUserRoles(
  id: string,
  signal?: AbortSignal
): Promise<HostUserRoles> {
  return identityGetHostUserRoles(http, { userId: id }, signal);
}

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
