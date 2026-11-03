import {
  tenancyGetPackageOffer, tenancySavePackageOffer, tenancyCreatePaidSubscription, tenancyListEntitlementCatalog,
  paymentsCheckoutSubscription, paymentsGetSubscriptionFulfillment, paymentsRetrySubscriptionFulfillment,
  type UpsertTenantPackageOfferRequest, type CreatePaidTenantSubscriptionRequest
} from '@fullnet/client-contracts';
import { http } from './http';

// 统一使用生成 Operation 的响应守卫；付款入口只提交冻结账单身份。
export const getPackageOffer = (packageId: string, signal?: AbortSignal) => tenancyGetPackageOffer(http, { packageId }, signal);
export const savePackageOffer = (packageId: string, body: UpsertTenantPackageOfferRequest) => tenancySavePackageOffer(http, { packageId, body });
export const listBillingFeatures = () => tenancyListEntitlementCatalog(http, {});
export const createPaidSubscription = (tenantId: string, body: CreatePaidTenantSubscriptionRequest) => tenancyCreatePaidSubscription(http, { tenantId, body });
export const checkoutSubscription = (tenantId: string, subscriptionId: string) => paymentsCheckoutSubscription(http, { tenantId, subscriptionId });
export const getSubscriptionFulfillment = (orderId: string) => paymentsGetSubscriptionFulfillment(http, { orderId });
export const retrySubscriptionFulfillment = (orderId: string) => paymentsRetrySubscriptionFulfillment(http, { orderId });
