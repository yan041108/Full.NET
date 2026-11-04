import { isDate, isGuid, isRecord } from './response-shape.js';
import { isTenantMember, type TenantMember } from './tenant-members.js';

/** 企业 Owner 职责与操作资格，以服务端当前状态为准。 */
export interface TenantOwnershipState { tenantId: string; owner?: TenantMember | null; canInitialize: boolean; canTransfer: boolean; requiresTotp: boolean }
/** 固定原操作及源/目标成员版本；验证凭据仅用于当前 HTTP 请求。 */
export interface TenantOwnerTransferRequest { operationId: string; sourceMemberId?: string | null; sourceVersion?: number | null;
  targetMemberId: string; targetVersion: number; currentPassword: string; totpCode?: string | null }
/** 不可变的职责交接回执，不保存验证凭据或业务 RBAC 授予。 */
export interface TenantOwnerTransferReceipt { tenantId: string; operationId: string; actorUserId: string;
  sourceMemberId?: string | null; sourceUserId?: string | null; sourcePreviousVersion?: number | null; sourceVersion?: number | null;
  targetMemberId: string; targetUserId: string; targetPreviousVersion: number; targetVersion: number; createdAtUtc: string }
const version = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) > 0 && (value as number) <= 2_147_483_647;
export function isTenantOwnershipState(value: unknown): value is TenantOwnershipState {
  return isRecord(value) && isGuid(value.tenantId) && typeof value.canInitialize === 'boolean'
    && typeof value.canTransfer === 'boolean' && typeof value.requiresTotp === 'boolean'
    && (value.owner == null ? !value.canTransfer : isTenantMember(value.owner) && value.owner.memberRole === 'Owner'
      && version(value.owner.version) && value.owner.tenantId.toLowerCase() === value.tenantId.toLowerCase()
      && !value.canInitialize && (!value.canTransfer || value.owner.status === 'Active'));
}
export function isTenantOwnerTransferReceipt(value: unknown): value is TenantOwnerTransferReceipt {
  return isRecord(value) && ['tenantId', 'operationId', 'actorUserId', 'targetMemberId', 'targetUserId'].every(key => isGuid(value[key]))
    && version(value.targetPreviousVersion) && version(value.targetVersion) && value.targetVersion === value.targetPreviousVersion + 1 && isDate(value.createdAtUtc)
    && (value.sourceMemberId == null ? value.sourceUserId == null && value.sourcePreviousVersion == null && value.sourceVersion == null
      : isGuid(value.sourceMemberId) && isGuid(value.sourceUserId) && version(value.sourcePreviousVersion) && version(value.sourceVersion)
        && value.sourceVersion === value.sourcePreviousVersion + 1
        && value.sourceUserId.toLowerCase() === (value.actorUserId as string).toLowerCase()
        && value.sourceMemberId.toLowerCase() !== (value.targetMemberId as string).toLowerCase()
        && value.sourceUserId.toLowerCase() !== (value.targetUserId as string).toLowerCase());
}
