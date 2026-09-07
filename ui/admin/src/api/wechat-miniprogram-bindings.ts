import {
  isPagedWeChatMiniProgramBindingResponse,
  isWeChatMiniProgramBindingResponse,
  type PagedWeChatMiniProgramBindingResponse,
  type WeChatMiniProgramBindingResponse
} from '@fullnet/client-contracts';
import { request } from './http';

/** 分页查询微信小程序 OpenId 绑定。 */
export async function listWeChatMiniProgramBindings(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedWeChatMiniProgramBindingResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/wechat-miniprogram/bindings?page=${page}&pageSize=${pageSize}`,
    { method: 'GET' },
    signal
  );
  if (!isPagedWeChatMiniProgramBindingResponse(value)) {
    throw new Error('client.invalid_wechat_miniprogram_binding_page');
  }
  return value;
}

/** 查询当前用户的微信小程序绑定。 */
export async function listMyWeChatMiniProgramBindings(
  signal?: AbortSignal
): Promise<WeChatMiniProgramBindingResponse[]> {
  const value = await request<unknown>(
    '/api/v1/notifications/wechat-miniprogram/bindings/mine',
    { method: 'GET' },
    signal
  );
  if (!Array.isArray(value) || !value.every(isWeChatMiniProgramBindingResponse)) {
    throw new Error('client.invalid_wechat_miniprogram_binding_list');
  }
  return value;
}

/** 通过 js_code 交换并完成当前用户绑定。 */
export async function exchangeWeChatMiniProgramBinding(
  body: {
    providerProfileVersionId: string;
    jsCode: string;
  },
  signal?: AbortSignal
): Promise<WeChatMiniProgramBindingResponse> {
  const value = await request<unknown>(
    '/api/v1/notifications/wechat-miniprogram/bindings/exchange',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isWeChatMiniProgramBindingResponse(value)) {
    throw new Error('client.invalid_wechat_miniprogram_binding');
  }
  return value;
}

/** 登记或更新订阅消息授权结果。 */
export async function recordWeChatMiniProgramSubscription(
  appId: string,
  body: { templateId: string; statusKey: string },
  signal?: AbortSignal
): Promise<WeChatMiniProgramBindingResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/wechat-miniprogram/bindings/${encodeURIComponent(appId)}/subscriptions`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isWeChatMiniProgramBindingResponse(value)) {
    throw new Error('client.invalid_wechat_miniprogram_binding');
  }
  return value;
}

export type {
  PagedWeChatMiniProgramBindingResponse,
  WeChatMiniProgramBindingResponse
};
