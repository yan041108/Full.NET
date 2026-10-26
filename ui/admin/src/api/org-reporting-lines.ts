import { http } from './http';
import { organizationGetReportingLine, organizationSetReportingLine, organizationListReportingCandidates } from '@fullnet/client-contracts';

export interface OrganizationReportingLine { employeeUserId: string; managerUserId: string | null; revision: number;
  updatedById: string | null; updatedAtUtc: string | null; managerDisplayName: string | null }
export interface OrganizationReportingCandidates { items: { id: string; username: string; displayName: string }[]; page: number; pageSize: number; total: number }
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
const invalid = (): never => { throw new TypeError('client.invalid_organization_reporting_line'); };
const integer = (value: unknown, min: number): number => {
  const result = typeof value === 'string' && /^(0|[1-9][0-9]*)$/.test(value) ? Number(value) : value;
  return typeof result === 'number' && Number.isSafeInteger(result) && result >= min ? result : invalid();
};
const path = (employee: string) => uuid(employee) ? `/api/v1/organization/users/${employee.toLowerCase()}/reporting-line` : invalid();
function read(value: unknown, employee: string): OrganizationReportingLine {
  if (!object(value) || !uuid(value.employeeUserId) || value.employeeUserId.toLowerCase() !== employee.toLowerCase() ||
    value.managerUserId !== null && (!uuid(value.managerUserId) || value.managerUserId.toLowerCase() === employee.toLowerCase()) ||
    value.updatedById !== null && !uuid(value.updatedById) || value.updatedAtUtc !== null && (typeof value.updatedAtUtc !== 'string' || !Number.isFinite(Date.parse(value.updatedAtUtc))) ||
    value.managerDisplayName !== null && typeof value.managerDisplayName !== 'string') return invalid();
  const revision = integer(value.revision, 0);
  if (revision === 0 && (value.managerUserId !== null || value.updatedById !== null || value.updatedAtUtc !== null) ||
    revision > 0 && (value.updatedById === null || value.updatedAtUtc === null) || value.managerUserId === null && value.managerDisplayName !== null) return invalid();
  return { employeeUserId: value.employeeUserId.toLowerCase(), managerUserId: typeof value.managerUserId === 'string' ? value.managerUserId.toLowerCase() : null,
    revision, updatedById: typeof value.updatedById === 'string' ? value.updatedById.toLowerCase() : null, updatedAtUtc: value.updatedAtUtc as string | null, managerDisplayName: value.managerDisplayName as string | null };
}
/** 直属上级来自明确汇报关系，列表的部门或职级不构成该授权事实。 */
export async function getOrganizationReportingLine(employee: string, signal?: AbortSignal): Promise<OrganizationReportingLine> {
  path(employee); return read(await organizationGetReportingLine(http, { employeeUserId: employee.toLowerCase() }, signal), employee);
}
/** 只写入选定直属上级和读取修订；未知结果需重新读取，不能自动重试配置。 */
export async function setOrganizationReportingLine(employee: string, manager: string | null, revision: number, signal?: AbortSignal): Promise<OrganizationReportingLine> {
  path(employee); if (manager !== null && (!uuid(manager) || manager.toLowerCase() === employee.toLowerCase()) || typeof revision !== 'number') return invalid();
  integer(revision, 0); const managerUserId = manager?.toLowerCase() ?? null;
  const result = read(await organizationSetReportingLine(http, { employeeUserId: employee.toLowerCase(), body: { managerUserId, expectedRevision: revision } }, signal), employee);
  return result.managerUserId === managerUserId ? result : invalid();
}
/** 候选由服务端当前租户分页返回，自身保留但界面禁选，最终仍由写边界验证。 */
export async function listOrganizationReportingCandidates(employee: string, page = 1, pageSize = 20, signal?: AbortSignal): Promise<OrganizationReportingCandidates> {
  integer(page, 1); integer(pageSize, 1); if (pageSize > 100 || page > Math.floor(2_147_483_647 / pageSize)) return invalid();
  path(employee); const value: unknown = await organizationListReportingCandidates(http, { employeeUserId: employee.toLowerCase(), page, pageSize }, signal);
  if (!object(value) || !Array.isArray(value.items) || value.items.length > pageSize || integer(value.page, 1) !== page || integer(value.pageSize, 1) !== pageSize ||
    value.items.some(item => !object(item) || !uuid(item.id) || typeof item.username !== 'string' || typeof item.displayName !== 'string') ||
    new Set(value.items.map(item => String(item.id).toLowerCase())).size !== value.items.length) return invalid();
  const total = integer(value.total, 0); if (value.items.length > total || value.items.length > 0 && (page - 1) * pageSize + value.items.length > total) return invalid();
  return { items: value.items.map(item => ({ id: String(item.id).toLowerCase(), username: item.username as string, displayName: item.displayName as string })), page, pageSize, total };
}
