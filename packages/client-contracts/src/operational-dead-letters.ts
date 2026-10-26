import { isGuid, isRecord } from './response-shape.js';

/** Host 死信运维只读契约；Offset 用十进制字符串避免丢失 bigint 精度。 */
export interface OperationalDeadLetter {
  id: string;
  source: 'legacy' | 'kafka';
  consumerName: string | null;
  messageId: string | null;
  messageType: string | null;
  schemaVersion: number | null;
  tenantId: string | null;
  attempts: number;
  errorCode: string | null;
  firstFailedAtUtc: string;
  lastFailedAtUtc: string;
  sourceTopic: string | null;
  sourcePartition: number | null;
  sourceOffset: string | null;
  dlqTopic: string | null;
  dlqPartition: number | null;
  dlqOffset: string | null;
  canReplay: boolean;
  replayedAtUtc: string | null;
}

export interface OperationalDeadLetterPage {
  items: OperationalDeadLetter[];
  total: number;
  page: number;
  pageSize: number;
}

const text = (value: unknown): boolean => value === null || typeof value === 'string';
const guid = (value: unknown): boolean => value === null || isGuid(value);
const offset = (value: unknown): boolean => value === null || (typeof value === 'string' && /^\d+$/.test(value));
const integer = (value: unknown): boolean => value === null || (Number.isSafeInteger(value) && Number(value) >= 0);
const time = (value: unknown): boolean => typeof value === 'string' && Number.isFinite(Date.parse(value));

/** 校验列表和详情的全部边界字段，损坏响应不得伪装为空列表。 */
export function isOperationalDeadLetter(value: unknown): value is OperationalDeadLetter {
  return isRecord(value) && isGuid(value.id) && (value.source === 'legacy' || value.source === 'kafka')
    && text(value.consumerName) && guid(value.messageId) && text(value.messageType)
    && integer(value.schemaVersion) && guid(value.tenantId) && Number.isSafeInteger(value.attempts)
    && Number(value.attempts) >= 0 && text(value.errorCode) && time(value.firstFailedAtUtc)
    && time(value.lastFailedAtUtc) && text(value.sourceTopic) && integer(value.sourcePartition)
    && offset(value.sourceOffset) && text(value.dlqTopic) && integer(value.dlqPartition)
    && offset(value.dlqOffset) && typeof value.canReplay === 'boolean'
    && (value.replayedAtUtc === null || time(value.replayedAtUtc));
}

/** 分页结果必须包含合法记录与有界页参数。 */
export function isOperationalDeadLetterPage(value: unknown): value is OperationalDeadLetterPage {
  return isRecord(value) && Array.isArray(value.items) && value.items.every(isOperationalDeadLetter)
    && Number.isSafeInteger(value.total) && Number(value.total) >= 0
    && Number.isSafeInteger(value.page) && Number(value.page) >= 1
    && Number.isSafeInteger(value.pageSize) && Number(value.pageSize) >= 1 && Number(value.pageSize) <= 100;
}
