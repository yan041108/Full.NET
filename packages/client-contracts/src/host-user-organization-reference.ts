import {
  isOrganizationUnit,
  type OrganizationUnit
} from './tenant-org-units.js';
import {
  isOrganizationPosition,
  type OrganizationPosition
} from './tenant-org-positions.js';
import {
  isOrganizationUserUnit,
  type OrganizationUserUnit
} from './tenant-user-units.js';
import {
  isOrganizationUserPosition,
  type OrganizationUserPosition
} from './tenant-user-positions.js';

/** Host 用户管理页读取的租户机构参考聚合。 */
export interface HostUserOrganizationReference {
  units: OrganizationUnit[];
  unitsTotal: number;
  positionsTotal: number;
  positions: OrganizationPosition[];
  userUnits: OrganizationUserUnit[];
  userPositions: OrganizationUserPosition[];
}

/** 校验不可信 JSON 是否为 Host 用户机构参考聚合。 */
export function isHostUserOrganizationReference(
  value: unknown
): value is HostUserOrganizationReference {
  return isRecord(value)
    && typeof value.unitsTotal === 'number' && typeof value.positionsTotal === 'number'
    && Array.isArray(value.units)
    && value.units.every(isOrganizationUnit)
    && Array.isArray(value.positions)
    && value.positions.every(isOrganizationPosition)
    && Array.isArray(value.userUnits)
    && value.userUnits.every(isOrganizationUserUnit)
    && Array.isArray(value.userPositions)
    && value.userPositions.every(isOrganizationUserPosition);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}