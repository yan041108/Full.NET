import { request } from './http';

export type WeChatMiniProgramSubscriptionResponse = {
  templateId: string;
  statusKey: string;
  authorizedAtUtc: string;
};

export type WeChatMiniProgramBindingResponse = {
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
};

export type PagedWeChatMiniProgramBindingResponse = {
  items: WeChatMiniProgramBindingResponse[];
  page: number;
  pageSize: number;
  total: number;
};

function isBindingRecord(value: unknown): value is WeChatMiniProgramBindingResponse {
  return typeof value === 'object'
    && value !== null
    && typeof (value as WeChatMiniProgramBindingResponse).id === 'string';
}

export function listWeChatMiniProgramBindings(
  page = 1,
  pageSize = 20
): Promise<PagedWeChatMiniProgramBindingResponse> {
  return request(`/api/v1/notifications/wechat-miniprogram/bindings?page=${page}&pageSize=${pageSize}`, { method: 'GET' });
}

export function listMyWeChatMiniProgramBindings(): Promise<WeChatMiniProgramBindingResponse[]> {
  return request('/api/v1/notifications/wechat-miniprogram/bindings/mine', { method: 'GET' });
}

export function exchangeWeChatMiniProgramBinding(body: {
  providerProfileVersionId: string;
  jsCode: string;
}): Promise<WeChatMiniProgramBindingResponse> {
  return request('/api/v1/notifications/wechat-miniprogram/bindings/exchange', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }).then(value => {
    if (!isBindingRecord(value)) {
      throw new TypeError('Invalid WeChat mini program binding response.');
    }
    return value;
  });
}

export function recordWeChatMiniProgramSubscription(
  appId: string,
  body: { templateId: string; statusKey: string }
): Promise<WeChatMiniProgramBindingResponse> {
  return request(`/api/v1/notifications/wechat-miniprogram/bindings/${encodeURIComponent(appId)}/subscriptions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }).then(value => {
    if (!isBindingRecord(value)) {
      throw new TypeError('Invalid WeChat mini program binding response.');
    }
    return value;
  });
}
