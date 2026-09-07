export interface WeChatMiniProgramSubscriptionResponse {
  templateId: string;
  statusKey: string;
  authorizedAtUtc: string;
}

export interface WeChatMiniProgramBindingResponse {
  id: string;
  userId: string;
  appId: string;
  providerProfileVersionId: string;
  openIdMask: string;
  verificationStatusKey: string;
  recipientEndpointId: string | null;
  subscriptions: WeChatMiniProgramSubscriptionResponse[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface PagedWeChatMiniProgramBindingResponse {
  items: WeChatMiniProgramBindingResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isWeChatMiniProgramSubscriptionResponse(
  value: unknown
): value is WeChatMiniProgramSubscriptionResponse {
  return isRecord(value)
    && typeof value.templateId === 'string'
    && typeof value.statusKey === 'string'
    && typeof value.authorizedAtUtc === 'string';
}

export function isWeChatMiniProgramBindingResponse(
  value: unknown
): value is WeChatMiniProgramBindingResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.userId)
    && typeof value.appId === 'string'
    && isGuid(value.providerProfileVersionId)
    && typeof value.openIdMask === 'string'
    && typeof value.verificationStatusKey === 'string'
    && (value.recipientEndpointId === null || isGuid(value.recipientEndpointId))
    && Array.isArray(value.subscriptions)
    && value.subscriptions.every(isWeChatMiniProgramSubscriptionResponse)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}

export function isPagedWeChatMiniProgramBindingResponse(
  value: unknown
): value is PagedWeChatMiniProgramBindingResponse {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isWeChatMiniProgramBindingResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
