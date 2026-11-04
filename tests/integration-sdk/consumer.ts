import { createIntegrationClient, webhooksListSubscriptions, webhooksReplayDelivery, type ReplayWebhookDeliveryRequest, type WebhookDeliveryReplayReceipt } from '@fullnet/integration-sdk';
const client = createIntegrationClient({ baseUrl:'https://example.com',tenantId:'019bc2b1-2a40-7cc3-8992-a80de51bf299',getCredentials:()=> ({accessToken:'typechecking-fixture',tenantId:'019bc2b1-2a40-7cc3-8992-a80de51bf299'}) });
export const list = () => webhooksListSubscriptions(client,{});
export function replay(subscriptionId:string,deliveryId:string,body:ReplayWebhookDeliveryRequest):Promise<WebhookDeliveryReplayReceipt> {
  return webhooksReplayDelivery(client,{subscriptionId,deliveryId,body});
}
