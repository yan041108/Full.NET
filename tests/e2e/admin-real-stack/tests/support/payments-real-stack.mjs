import { expect } from '@playwright/test';
import {
  adminOrigin,
  loginHostAdminAccessToken,
  loginTenantAdminAccessToken
} from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const merchantConfigsPath = `${apiBaseUrl}/api/v1/payments/merchant-configs`;
const ordersPath = `${apiBaseUrl}/api/v1/payments/orders`;
const refundsPath = `${apiBaseUrl}/api/v1/payments/refunds`;

function weChatNotifyPath(merchantConfigId) {
  return `${apiBaseUrl}/api/v1/payments/wechat-native/notify/${merchantConfigId}`;
}

export const paymentWeChatNativeChannelKey = 'wechat_native';
export const paymentAlipayPageChannelKey = 'alipay_page';

const samplePrivateKeyPem =
  '-----BEGIN PRIVATE KEY-----\nMIIB\n-----END PRIVATE KEY-----';

/** 清单 77：支付宝网页支付商户元数据（禁用、不触网扣款）。 */
export function buildAlipayPageMerchantConfigBody(suffix, { returnUrl } = {}) {
  return {
    tenantId: null,
    name: `e2e-alipay-${suffix}`,
    channelKey: paymentAlipayPageChannelKey,
    appId: '2021000123456789',
    merchantId: '',
    certificateSerialNo: '',
    notifyUrl: `https://example.com/api/v1/payments/callbacks/alipay-${suffix}`,
    returnUrl: returnUrl ?? 'https://example.com/payments/return',
    apiV3Key: '',
    privateKeyPem: samplePrivateKeyPem,
    isDefault: false,
    isEnabled: false
  };
}

function authHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}

/** Host 分页列出支付商户配置（列表脱敏）。 */
export async function listPaymentMerchantConfigsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${merchantConfigsPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建商户配置。 */
export async function createPaymentMerchantConfigViaApi(
  request,
  clientKind,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(merchantConfigsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 租户上下文分页列出支付订单。 */
export async function listPaymentOrdersViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${ordersPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取支付订单详情。 */
export async function getPaymentOrderViaApi(request, clientKind, orderId, accessToken = null) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${ordersPath}/${orderId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建支付订单（微信 Native 首切片；不触发真实扣款）。 */
export async function createPaymentOrderViaApi(request, clientKind, body, accessToken = null) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(ordersPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 匿名微信 Native 回调（无 Bearer）；缺签名头时应 FAIL 且不触发真实扣款。 */
export async function postWeChatNativeNotifyViaApi(
  request,
  merchantConfigId,
  { rawBody = '{}', headers = {} } = {}
) {
  const response = await request.post(weChatNotifyPath(merchantConfigId), {
    headers: {
      'Content-Type': 'application/json',
      ...headers
    },
    data: rawBody
  });
  return { response };
}

/** 与渠道对账并同步订单状态。 */
export async function reconcilePaymentOrderViaApi(
  request,
  clientKind,
  orderId,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(`${ordersPath}/${orderId}/reconcile`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 对指定订单创建退款（不调用真实微信退款 API 的 E2E 断言以 404/409/422 为主）。 */
export async function createPaymentRefundViaApi(
  request,
  clientKind,
  orderId,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(`${ordersPath}/${orderId}/refunds`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 分页列出退款记录。 */
export async function listPaymentRefundsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${refundsPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取退款详情。 */
export async function getPaymentRefundViaApi(request, clientKind, refundId, accessToken = null) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${refundsPath}/${refundId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 列表项不得回显 API 密钥或私钥 PEM。 */
export function expectPaymentMerchantConfigListItemMasked(item) {
  expect(item).toBeTruthy();
  expect(typeof item.maskedAppId).toBe('string');
  expect(typeof item.maskedMerchantId).toBe('string');
  expect(typeof item.hasApiV3Key).toBe('boolean');
  expect(typeof item.hasPrivateKey).toBe('boolean');
  expect(item).not.toHaveProperty('apiV3Key');
  expect(item).not.toHaveProperty('privateKeyPem');
}
