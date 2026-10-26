import {
  isMessagingDeadLetterPage,
  type MessagingDeadLetter,
  type MessagingDeadLetterPage
} from '@fullnet/client-contracts';
import { request } from './http';
import { isOperationalDeadLetter, isOperationalDeadLetterPage,
  type OperationalDeadLetter, type OperationalDeadLetterPage } from '@fullnet/client-contracts';

export type { OperationalDeadLetter, OperationalDeadLetterPage };
export type DeadLetterSource = 'legacy' | 'kafka';

export interface OperationalDeadLetterFilter {
  page: number;
  pageSize: number;
  messageType?: string;
  schemaVersion?: string;
  errorCode?: string;
  consumerName?: string;
  fromUtc?: string;
  toUtc?: string;
}

function deadLetterPath(source: DeadLetterSource): string {
  return `/api/v1/messaging/${source === 'legacy' ? 'outbox' : 'kafka'}/dead-letters`;
}

/** 分来源查询真实死信；筛选值由 URLSearchParams 编码。 */
export async function listOperationalDeadLetters(source: DeadLetterSource, filter: OperationalDeadLetterFilter,
  signal?: AbortSignal): Promise<OperationalDeadLetterPage> {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(filter)) {
    if (String(value ?? '').trim()) query.set(key, String(value).trim());
  }
  const value = await request<unknown>(`${deadLetterPath(source)}?${query}`, { signal });
  if (!isOperationalDeadLetterPage(value)) throw new Error('client.invalid_operational_dead_letter_page');
  return value;
}

/** 详情仅返回元数据，服务端不会返回原始消息载荷。 */
export async function getOperationalDeadLetter(source: DeadLetterSource, id: string): Promise<OperationalDeadLetter> {
  const value = await request<unknown>(`${deadLetterPath(source)}/${encodeURIComponent(id)}`);
  if (!isOperationalDeadLetter(value)) throw new Error('client.invalid_operational_dead_letter');
  return value;
}

/** 精确单条恢复；客户端只提交理由，不提交操作者或修改后的载荷。 */
export async function recoverOperationalDeadLetter(source: DeadLetterSource, id: string, reason: string): Promise<string> {
  const value = await request<unknown>(`${deadLetterPath(source)}/${encodeURIComponent(id)}/${source === 'legacy' ? 'requeue' : 'replay'}`,
    { method: 'POST', body: JSON.stringify({ reason: reason.trim() }) });
  if (typeof value !== 'object' || value === null || !('id' in value) || value.id !== id
    || !('outcome' in value) || !['requeued', 'processed', 'already_processed'].includes(String(value.outcome)))
    throw new Error('client.invalid_dead_letter_recovery');
  return String(value.outcome);
}

export type { MessagingDeadLetter, MessagingDeadLetterPage };

/** 分页查询消费死信，供消息运维页展示。 */
export async function listMessagingDeadLetters(
  page = 1,
  pageSize = 20,
  consumerName?: string,
  signal?: AbortSignal
): Promise<MessagingDeadLetterPage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  if (consumerName?.trim()) {
    query.set('consumerName', consumerName.trim());
  }
  const value = await request<unknown>(
    `/api/v1/messaging/dead-letters?${query.toString()}`,
    { signal }
  );
  if (!isMessagingDeadLetterPage(value)) {
    throw new Error('client.invalid_messaging_dead_letter_page');
  }
  return value;
}
