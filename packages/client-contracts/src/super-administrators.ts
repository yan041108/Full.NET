export interface SuperAdministrator {
  userId: string;
  username: string;
  displayName: string;
  isActive: boolean;
}

export interface SuperAdministratorAudit {
  id: string;
  targetUserId: string;
  actorUserId: string | null;
  eventType: string;
  resultCode: string;
  succeeded: boolean;
  occurredAtUtc: string;
}

export interface SuperAdministratorChangeResponse {
  targetUserId: string;
  changed: boolean;
}

/** 校验不可信 JSON 是否为超级管理员账号列表。 */
export function isSuperAdministratorArray(
  value: unknown
): value is SuperAdministrator[] {
  return Array.isArray(value) && value.every(item => isRecord(item)
    && isText(item.userId)
    && typeof item.username === 'string'
    && typeof item.displayName === 'string'
    && typeof item.isActive === 'boolean');
}

/** 校验不可信 JSON 是否为可追责的超级管理员审计列表。 */
export function isSuperAdministratorAuditArray(
  value: unknown
): value is SuperAdministratorAudit[] {
  return Array.isArray(value) && value.every(item => isRecord(item)
    && isText(item.id)
    && isText(item.targetUserId)
    && (isText(item.actorUserId) || item.actorUserId === null)
    && isText(item.eventType)
    && isText(item.resultCode)
    && typeof item.succeeded === 'boolean'
    && isText(item.occurredAtUtc));
}

/** 校验授予或撤销返回的幂等变更摘要。 */
export function isSuperAdministratorChangeResponse(
  value: unknown
): value is SuperAdministratorChangeResponse {
  return isRecord(value)
    && isText(value.targetUserId)
    && typeof value.changed === 'boolean';
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
