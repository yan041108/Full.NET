import {
  tenancyGetPackageOffer, tenancySavePackageOffer, tenancyCreatePaidSubscription, tenancyListEntitlementCatalog, tenancyGetSubscriptionRenewalPreview, tenancyGetSubscriptionInvoice,
  paymentsCheckoutSubscription, paymentsGetSubscriptionFulfillment, paymentsRetrySubscriptionFulfillment,
  type UpsertTenantPackageOfferRequest, type CreatePaidTenantSubscriptionRequest
} from '@fullnet/client-contracts';
import { http } from './http';

// 统一使用生成 Operation 的响应守卫；付款入口只提交冻结账单身份。
export const getPackageOffer = (packageId: string, signal?: AbortSignal) => tenancyGetPackageOffer(http, { packageId }, signal);
export const getSubscriptionRenewalPreview = (tenantId: string, subscriptionId: string, signal?: AbortSignal, targetPackageId?: string) => tenancyGetSubscriptionRenewalPreview(http, { tenantId, subscriptionId, ...(targetPackageId ? { targetPackageId } : {}) }, signal);
export const getSubscriptionInvoice = (tenantId: string, subscriptionId: string, signal?: AbortSignal) => tenancyGetSubscriptionInvoice(http, { tenantId, subscriptionId }, signal);
export const savePackageOffer = (packageId: string, body: UpsertTenantPackageOfferRequest, signal?: AbortSignal) => tenancySavePackageOffer(http, { packageId, body }, signal);
export const listBillingFeatures = (signal?: AbortSignal) => tenancyListEntitlementCatalog(http, {}, signal);
export const createPaidSubscription = (tenantId: string, body: CreatePaidTenantSubscriptionRequest, signal?: AbortSignal) => tenancyCreatePaidSubscription(http, { tenantId, body }, signal);
export const checkoutSubscription = (tenantId: string, subscriptionId: string, signal?: AbortSignal) => paymentsCheckoutSubscription(http, { tenantId, subscriptionId }, signal);
export const getSubscriptionFulfillment = (orderId: string, signal?: AbortSignal) => paymentsGetSubscriptionFulfillment(http, { orderId }, signal);
export const retrySubscriptionFulfillment = (orderId: string, signal?: AbortSignal) => paymentsRetrySubscriptionFulfillment(http, { orderId }, signal);
