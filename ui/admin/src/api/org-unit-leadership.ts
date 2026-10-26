import { http } from './http';
import { organizationGetTenantUnitLeadership, organizationSetTenantUnitLeadership, organizationListTenantUnitLeaderCandidates } from '@fullnet/client-contracts';

export interface OrganizationUnitLeadership {
  unitId: string; unitVersion: number; isUnitActive: boolean; leaderUserId: string | null; revision: number;
  updatedById: string | null; updatedAtUtc: string | null; leaderDisplayName: string | null;
}
export interface OrganizationUnitLeaderCandidate { id: string; username: string; displayName: string }
export interface OrganizationUnitLeaderCandidates { items: OrganizationUnitLeaderCandidate[]; page: number; pageSize: number; total: number }
export interface SetOrganizationUnitLeadership { leaderUserId: string | null; expectedUnitVersion: number; expectedRevision: number }
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
const invalid = (): never => { throw new TypeError('client.invalid_organization_unit_leadership'); };
const integer = (value: unknown, minimum: number): number => {
  const number = typeof value === 'string' && /^(0|[1-9][0-9]*)$/.test(value) ? Number(value) : value;
  return typeof number === 'number' && Number.isSafeInteger(number) && number >= minimum ? number : invalid();
};
const path = (unitId: string) => uuid(unitId) ? `/api/v1/organization/units/${encodeURIComponent(unitId.toLowerCase())}/leadership` : invalid();
function read(value: unknown, unitId: string): OrganizationUnitLeadership {
  if (!object(value) || !uuid(value.unitId) || value.unitId.toLowerCase() !== unitId.toLowerCase() || typeof value.isUnitActive !== 'boolean' ||
    value.leaderUserId !== null && !uuid(value.leaderUserId) || value.updatedById !== null && !uuid(value.updatedById) ||
    value.updatedAtUtc !== null && (typeof value.updatedAtUtc !== 'string' || !Number.isFinite(Date.parse(value.updatedAtUtc))) ||
    value.leaderDisplayName !== null && typeof value.leaderDisplayName !== 'string') return invalid();
  const revision = integer(value.revision, 0);
  if (revision === 0 && (value.leaderUserId !== null || value.updatedById !== null || value.updatedAtUtc !== null) ||
    revision > 0 && (value.updatedById === null || value.updatedAtUtc === null) || value.leaderUserId === null && value.leaderDisplayName !== null) return invalid();
  return { unitId: value.unitId.toLowerCase(), unitVersion: integer(value.unitVersion, 1), isUnitActive: value.isUnitActive,
    leaderUserId: typeof value.leaderUserId === 'string' ? value.leaderUserId.toLowerCase() : null, revision, updatedById: typeof value.updatedById === 'string' ? value.updatedById.toLowerCase() : null,
    updatedAtUtc: value.updatedAtUtc as string | null, leaderDisplayName: value.leaderDisplayName as string | null };
}

/** 只读取已授权机构的权威配置；版本和负责人不从列表行或候选排序推定。 */
export async function getOrganizationUnitLeadership(unitId: string, signal?: AbortSignal): Promise<OrganizationUnitLeadership> {
  path(unitId);
  return read(await organizationGetTenantUnitLeadership(http, { unitId: unitId.toLowerCase() }, signal), unitId);
}
/** 更新只提交显式负责人和两个版本，不接受租户或操作人字段。 */
export async function setOrganizationUnitLeadership(unitId: string, request: SetOrganizationUnitLeadership, signal?: AbortSignal): Promise<OrganizationUnitLeadership> {
  if (request.leaderUserId !== null && !uuid(request.leaderUserId)) return invalid();
  if (typeof request.expectedRevision !== 'number' || typeof request.expectedUnitVersion !== 'number') return invalid();
  integer(request.expectedRevision, 0); integer(request.expectedUnitVersion, 1);
  const leaderUserId = request.leaderUserId?.toLowerCase() ?? null;
  path(unitId);
  const value = read(await organizationSetTenantUnitLeadership(http, { unitId: unitId.toLowerCase(), body: {
    leaderUserId, expectedRevision: request.expectedRevision, expectedUnitVersion: request.expectedUnitVersion } }, signal), unitId);
  return value.leaderUserId === leaderUserId ? value : invalid();
}
/** 候选分页来自 Identity 的当前租户成员权威目录，不截取首屏作为全部允许范围。 */
export async function listOrganizationUnitLeaderCandidates(unitId: string, page = 1, pageSize = 20, signal?: AbortSignal): Promise<OrganizationUnitLeaderCandidates> {
  integer(page, 1); integer(pageSize, 1); if (pageSize > 100 || page > Math.floor(2_147_483_647 / pageSize)) return invalid();
  path(unitId);
  const value: unknown = await organizationListTenantUnitLeaderCandidates(http, { unitId: unitId.toLowerCase(), page, pageSize }, signal);
  if (!object(value) || !Array.isArray(value.items) || value.items.length > pageSize || integer(value.page, 1) !== page || integer(value.pageSize, 1) !== pageSize ||
    value.items.some(item => !object(item) || !uuid(item.id) || typeof item.username !== 'string' || typeof item.displayName !== 'string') ||
    new Set(value.items.map(item => String(item.id).toLowerCase())).size !== value.items.length) return invalid();
  const total = integer(value.total, 0); if (value.items.length > total || value.items.length > 0 && (page - 1) * pageSize + value.items.length > total) return invalid();
  return { items: value.items.map(item => ({ id: String(item.id).toLowerCase(), username: item.username as string, displayName: item.displayName as string })), page, pageSize, total };
}
