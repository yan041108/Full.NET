import { IntegrationProtocolError } from './http.js';
import { readWorkflowCompletedWebhookEvent } from './generated/guards.generated.js';
import type { WorkflowCompletedWebhookEvent } from './generated/models.generated.js';
import { withAbort } from './async.js';

export interface WorkflowWebhookVerification {
  body: Uint8Array;
  signature: string;
  version: string;
  keyId: string;
  timestamp: string;
  eventId: string;
  deliveryId: string;
  eventType: string;
  expectedTenantId: string;
  /** 只解析受保护配置中明确允许的公开 KeyId；未知身份返回 null。 */
  resolveSigningSecret: (keyId: string, signal: AbortSignal) => string | null | Promise<string | null>;
  maxAgeSeconds?: number;
  /** 受信时钟的 Unix 毫秒；默认使用 Date.now，不能来自请求头。 */
  clock?: () => number;
  timeoutMilliseconds?: number;
  signal?: AbortSignal;
}
/** 验证现行 PascalCase 事件原文与已签名身份；结果仍须由接收方事务 Inbox 去重。 */
export async function verifyWorkflowCompletedWebhook(input: WorkflowWebhookVerification): Promise<WorkflowCompletedWebhookEvent> {
  const invalid = (): never => { throw new IntegrationProtocolError('sdk.webhook_invalid'); };
  if (!input || typeof input !== 'object') invalid();
  const { signature: encoded, eventId, deliveryId, eventType, expectedTenantId, keyId, version, timestamp, resolveSigningSecret, clock = Date.now, maxAgeSeconds = 300, timeoutMilliseconds = 5000, signal: caller } = input;
  const empty = '00000000-0000-0000-0000-000000000000', guid = (value: unknown): value is string => typeof value === 'string' && /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/iu.test(value) && value !== empty;
  if (!(input.body instanceof Uint8Array) || input.body.byteLength > 262_144 || version !== '1' || ![keyId,eventId,deliveryId,expectedTenantId].every(guid)
    || typeof encoded !== 'string' || !/^[a-f\d]{64}$/iu.test(encoded) || typeof timestamp !== 'string' || !/^(?:0|[1-9]\d{0,11})$/u.test(timestamp)
    || typeof eventType !== 'string' || !/^[\x21-\x7e]{1,128}$/u.test(eventType) || typeof resolveSigningSecret !== 'function' || typeof clock !== 'function'
    || !Number.isInteger(maxAgeSeconds) || maxAgeSeconds < 1 || maxAgeSeconds > 3600 || !Number.isInteger(timeoutMilliseconds) || timeoutMilliseconds < 1 || timeoutMilliseconds > 30_000) invalid();
  const fresh = () => { const now = clock(); return Number.isFinite(now) && Math.abs(Math.floor(now/1000)-Number(timestamp)) <= maxAgeSeconds; };
  if (!fresh()) invalid();
  const bytes = new Uint8Array(input.body), signature = Uint8Array.from(encoded.match(/../gu)!,pair => Number.parseInt(pair,16));
  const deadline = new AbortController(), timer = setTimeout(() => deadline.abort(new DOMException('Webhook verification timed out.','TimeoutError')),timeoutMilliseconds);
  const signal = AbortSignal.any([...(caller ? [caller] : []),deadline.signal]);
  try {
  let secret: string | null;
  try { secret = await withAbort(Promise.resolve(resolveSigningSecret(keyId.toLowerCase(),signal)),signal); }
  catch { signal.throwIfAborted(); throw new IntegrationProtocolError('sdk.webhook_key_unavailable'); }
  if (typeof secret !== 'string' || secret.length < 16 || secret.length > 128) return invalid();
  const digest = await withAbort(crypto.subtle.digest('SHA-256',bytes),signal), hex = Array.from(new Uint8Array(digest),value=>value.toString(16).padStart(2,'0')).join('').toUpperCase();
  const canonical = ['fullnet.webhook/1',timestamp,keyId.toLowerCase(),eventId.toLowerCase(),deliveryId.toLowerCase(),eventType,hex].join('\n');
  const key = await withAbort(crypto.subtle.importKey('raw',new TextEncoder().encode(secret),{name:'HMAC',hash:'SHA-256'},false,['verify']),signal);
  if (!await withAbort(crypto.subtle.verify('HMAC',key,signature,new TextEncoder().encode(canonical)),signal) || !fresh()) invalid();
  let value: unknown; try { value = JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes)); } catch { return invalid(); }
  let event: WorkflowCompletedWebhookEvent;
  try { event = readWorkflowCompletedWebhookEvent(value); } catch { return invalid(); }
  if (event.EventId === empty || event.TenantId === empty || event.Data.InstanceId === empty || event.Data.RecipientUserId === empty
    || event.EventId.toLowerCase() !== eventId.toLowerCase() || event.TenantId.toLowerCase() !== expectedTenantId.toLowerCase() || event.EventType !== eventType
    || !/(?:Z|\+00:00)$/u.test(event.OccurredAtUtc) || !Number.isFinite(Date.parse(event.OccurredAtUtc)) || !event.Data.BusinessType || !event.Data.BusinessId) invalid();
  return event;
  } finally { clearTimeout(timer); }
}
