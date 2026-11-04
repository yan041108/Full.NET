import { IntegrationProtocolError } from './http.js';
import { readWorkflowCompletedWebhookEvent } from './generated/guards.generated.js';
import type { WorkflowCompletedWebhookEvent } from './generated/models.generated.js';

export interface WorkflowWebhookVerification {
  body: Uint8Array;
  signature: string;
  eventId: string;
  eventType: string;
  expectedTenantId: string;
  secret: string;
}
/** 验证现行 PascalCase 事件原文与已签名身份；结果仍须由接收方事务 Inbox 去重。 */
export async function verifyWorkflowCompletedWebhook(input: WorkflowWebhookVerification): Promise<WorkflowCompletedWebhookEvent> {
  const invalid = (): never => { throw new IntegrationProtocolError('sdk.webhook_invalid'); };
  const { signature: encoded, eventId, eventType, expectedTenantId, secret } = input;
  if (!(input.body instanceof Uint8Array) || input.body.byteLength > 262_144 || ![encoded,eventId,eventType,expectedTenantId,secret].every(value => typeof value === 'string') || !/^[a-f\d]{64}$/iu.test(encoded) || secret.length < 16 || secret.length > 128) invalid();
  const bytes = new Uint8Array(input.body), signature = Uint8Array.from(encoded.match(/../gu)!,pair => Number.parseInt(pair,16));
  const key = await crypto.subtle.importKey('raw',new TextEncoder().encode(secret),{name:'HMAC',hash:'SHA-256'},false,['verify']);
  if (!await crypto.subtle.verify('HMAC',key,signature,bytes)) invalid();
  let value: unknown; try { value = JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes)); } catch { return invalid(); }
  const event = readWorkflowCompletedWebhookEvent(value), empty = '00000000-0000-0000-0000-000000000000';
  if (event.EventId === empty || event.TenantId === empty || event.Data.InstanceId === empty || event.Data.RecipientUserId === empty
    || event.EventId.toLowerCase() !== eventId.toLowerCase() || event.TenantId.toLowerCase() !== expectedTenantId.toLowerCase() || event.EventType !== eventType
    || !/(?:Z|\+00:00)$/u.test(event.OccurredAtUtc) || !Number.isFinite(Date.parse(event.OccurredAtUtc)) || !event.Data.BusinessType || !event.Data.BusinessId) invalid();
  return event;
}
