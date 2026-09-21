import { request } from './http';

/** 消费死信列表项（与 Host Messaging API 对齐）。 */
export interface MessagingDeadLetter {
  consumerName: string;
  messageId: string;
  messageType: string;
  schemaVersion: number;
  tenantId: string | null;
  attempts: number;
  receivedAtUtc: string;
  lastErrorCode: string | null;
  lastError: string | null;
}

export interface MessagingDeadLetterPage {
  items: MessagingDeadLetter[];
  total: number;
  page: number;
  pageSize: number;
}

function isDeadLetterPage(value: unknown): value is MessagingDeadLetterPage {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const page = value as MessagingDeadLetterPage;
  return Array.isArray(page.items)
    && typeof page.total === 'number'
    && typeof page.page === 'number'
    && typeof page.pageSize === 'number';
}

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
  const value = await request<MessagingDeadLetterPage>(
    `/api/v1/messaging/dead-letters?${query.toString()}`,
    { signal }
  );
  if (!isDeadLetterPage(value)) {
    throw new Error('client.invalid_messaging_dead_letter_page');
  }
  return value;
}
