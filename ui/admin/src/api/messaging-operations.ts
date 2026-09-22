import {
  isMessagingDeadLetterPage,
  type MessagingDeadLetter,
  type MessagingDeadLetterPage
} from '@fullnet/client-contracts';
import { request } from './http';

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
