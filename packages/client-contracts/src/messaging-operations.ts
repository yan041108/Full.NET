import { isRecord } from './response-shape.js';

export const messagingReplayStates = ['prepared', 'running', 'partial', 'completed', 'cancel_requested', 'cancelled', 'result_unknown', 'blocked'] as const;
export type MessagingReplayState = typeof messagingReplayStates[number];
export interface MessagingReplayOperation {
  operationId: string;
  state: MessagingReplayState;
  topicCode: string;
  replayConsumerName: string;
  eventType: string;
  schemaVersion: number;
  maxMessages: number;
  confirmedMessages: number;
  processedMessages: number;
  alreadyProcessedMessages: number;
  rejectedAttempts: number;
  attemptCount: number;
  hasUnconfirmedMessage: boolean;
  lastReasonCode: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  partitions: { partition: number; startOffset: string; endOffsetExclusive: string; nextOffset: string }[];
}
export interface MessagingReplayOperationPage { items: MessagingReplayOperation[]; page: number; pageSize: number; total: number }
export interface MessagingReplayCreateRequest {
  requestId: string;
  topicCode: string;
  fromTimestampUtc: string | null;
  toTimestampUtc: string | null;
  fromOffset: string | null;
  toOffset: string | null;
  partitions: number[];
  replayConsumerName: string;
  maxMessages: number;
  reason: string;
}
export interface MessagingLegacyBacklog {
  pendingCount: number; dueRetryCount: number; activeLeaseCount: number; deadLetterCount: number;
  oldestOccurredAtUtc: string | null; oldestDeadLetteredAtUtc: string | null;
}
export interface MessagingDeliveryStream {
  eventType: string; schemaVersion: number; topicCode: string | null; deliveryOwner: 0 | 1 | 2 | null;
  isRegistered: boolean; consumerNames: string[]; observationState: 'available' | 'partial'; legacyBacklog: MessagingLegacyBacklog | null;
  retainedEventCount: number | null; kafkaProjectedDeadLetterCount: number | null; oldestKafkaDeadLetterAtUtc: string | null;
  rollbackGeneration: string | null; cutoverReadiness: 'blocked' | 'unknown'; blockingReasons: string[];
}
export interface MessagingShadowCapturePolicy {
  enabled: boolean; eventType: string | null; schemaVersion: number | null; maximumEvents: number | null;
  maximumPayloadBytes: number | null; maximumWindowSeconds: number | null;
}
export interface MessagingShadowCapture {
  generation: string; eventType: string; schemaVersion: number; state: 'active' | 'closed' | 'blocked' | 'expired';
  maxEvents: number; maxPayloadBytes: number; capturedCount: number; capturedBytes: number; missingMetadataCount: number;
  startedAtUtc: string; expiresAtUtc: string; closedAtUtc: string | null; endReason: string | null;
}
export interface MessagingShadowCaptureStart {
  requestId: string; expectedGeneration: string | null; eventType: string; schemaVersion: number;
  maxEvents: number; windowSeconds: number; reason: string;
}
export interface MessagingDeliveryOverview {
  backlog: MessagingLegacyBacklog | null; streams: MessagingDeliveryStream[]; observationState: 'available' | 'partial' | 'failed';
  hasMoreStreams: boolean; unattributedKafkaDeadLetters: number | null; cutoverEnabled: boolean;
  replayEnabled: boolean; replayMaximumSynchronousMessages: number; replayExecutionTimeoutSeconds: number; controlAdapterConfigured: boolean; targetRuntimeState: 'not_observed'; shadowCapturePolicy: MessagingShadowCapturePolicy;
}
export interface MessagingRollbackOperation {
  generation: string; eventType: string; schemaVersion: number; phase: 0 | 1 | 2 | 3 | 4 | 5; createdAtUtc: string; updatedAtUtc: string;
}
export interface MessagingCutoverEvidence { id: string; eventType: string; schemaVersion: number; sampleCount: number; verifiedAtUtc: string; expiresAtUtc: string }

const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const text = (value: unknown): value is string => typeof value === 'string';
const nullableText = (value: unknown): value is string | null => value === null || text(value);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(text);
const timestamp = (value: unknown): value is string => text(value) && Number.isFinite(Date.parse(value));
const nullableTimestamp = (value: unknown): value is string | null => value === null || timestamp(value);
/** 十进制位点保持字符串，不经过 Number 转换。 */
export const isMessagingOffset = (value: unknown): value is string => text(value) && /^\d{1,19}$/.test(value) && BigInt(value) <= 9223372036854775807n;

/** 核对持久进度及窗口；无效状态、精度损失和倒退位点一律拒绝。 */
export function isMessagingReplayOperation(value: unknown): value is MessagingReplayOperation {
  if (!isRecord(value) || !uuid(value.operationId) || !messagingReplayStates.includes(value.state as MessagingReplayState)
    || !text(value.topicCode) || !text(value.replayConsumerName) || !text(value.eventType)
    || !count(value.schemaVersion) || value.schemaVersion < 1 || !count(value.maxMessages) || value.maxMessages < 1
    || !['confirmedMessages', 'processedMessages', 'alreadyProcessedMessages', 'rejectedAttempts', 'attemptCount'].every(key => count(value[key]))
    || value.confirmedMessages !== Number(value.processedMessages) + Number(value.alreadyProcessedMessages)
    || typeof value.hasUnconfirmedMessage !== 'boolean' || !nullableText(value.lastReasonCode)
    || !timestamp(value.createdAtUtc) || !timestamp(value.updatedAtUtc) || !Array.isArray(value.partitions) || value.partitions.length > 32) return false;
  const partitions = new Set<number>();
  return value.partitions.every(item => {
    if (!isRecord(item) || !count(item.partition) || partitions.has(item.partition)
      || !isMessagingOffset(item.startOffset) || !isMessagingOffset(item.nextOffset) || !isMessagingOffset(item.endOffsetExclusive)
      || BigInt(item.nextOffset) < BigInt(item.startOffset) || BigInt(item.nextOffset) > BigInt(item.endOffsetExclusive)) return false;
    partitions.add(item.partition); return true;
  });
}
export function isMessagingReplayOperationPage(value: unknown): value is MessagingReplayOperationPage {
  return isRecord(value) && Array.isArray(value.items) && value.items.every(isMessagingReplayOperation)
    && count(value.page) && value.page > 0 && count(value.pageSize) && value.pageSize > 0 && count(value.total);
}
function isBacklog(value: unknown): value is MessagingLegacyBacklog {
  return isRecord(value) && ['pendingCount', 'dueRetryCount', 'activeLeaseCount', 'deadLetterCount'].every(key => count(value[key]))
    && nullableTimestamp(value.oldestOccurredAtUtc) && nullableTimestamp(value.oldestDeadLetteredAtUtc);
}
export function isMessagingDeliveryOverview(value: unknown): value is MessagingDeliveryOverview {
  return isRecord(value) && (value.backlog === null || isBacklog(value.backlog))
    && ['available', 'partial', 'failed'].includes(String(value.observationState)) && typeof value.hasMoreStreams === 'boolean'
    && (value.unattributedKafkaDeadLetters === null || count(value.unattributedKafkaDeadLetters))
    && ['cutoverEnabled', 'replayEnabled', 'controlAdapterConfigured'].every(key => typeof value[key] === 'boolean')
    && count(value.replayMaximumSynchronousMessages) && count(value.replayExecutionTimeoutSeconds)
    && isMessagingShadowCapturePolicy(value.shadowCapturePolicy) && value.targetRuntimeState === 'not_observed' && Array.isArray(value.streams) && value.streams.length <= 512
    && value.streams.every(item => isRecord(item) && text(item.eventType) && count(item.schemaVersion) && item.schemaVersion > 0
      && nullableText(item.topicCode) && [null, 0, 1, 2].includes(item.deliveryOwner as 0 | 1 | 2 | null)
      && typeof item.isRegistered === 'boolean' && strings(item.consumerNames) && ['available', 'partial'].includes(String(item.observationState))
      && (item.legacyBacklog === null || isBacklog(item.legacyBacklog))
      && ['retainedEventCount', 'kafkaProjectedDeadLetterCount'].every(key => item[key] === null || count(item[key]))
      && nullableTimestamp(item.oldestKafkaDeadLetterAtUtc) && (item.rollbackGeneration === null || uuid(item.rollbackGeneration))
      && ['blocked', 'unknown'].includes(String(item.cutoverReadiness)) && strings(item.blockingReasons));
}
export function isMessagingRollbackOperation(value: unknown): value is MessagingRollbackOperation {
  return isRecord(value) && uuid(value.generation) && text(value.eventType) && count(value.schemaVersion) && value.schemaVersion > 0
    && [0, 1, 2, 3, 4, 5].includes(Number(value.phase)) && typeof value.phase === 'number'
    && timestamp(value.createdAtUtc) && timestamp(value.updatedAtUtc);
}
export function isMessagingCutoverEvidence(value: unknown): value is MessagingCutoverEvidence {
  return isRecord(value) && uuid(value.id) && text(value.eventType) && count(value.schemaVersion) && value.schemaVersion > 0
    && count(value.sampleCount) && value.sampleCount > 0 && timestamp(value.verifiedAtUtc) && timestamp(value.expiresAtUtc);
}

/** 禁用策略不得携带陈旧流或预算；启用策略必须有完整的服务端上限。 */
export function isMessagingShadowCapturePolicy(value: unknown): value is MessagingShadowCapturePolicy {
  if (!isRecord(value) || typeof value.enabled !== 'boolean') return false;
  const keys = ['eventType', 'schemaVersion', 'maximumEvents', 'maximumPayloadBytes', 'maximumWindowSeconds'];
  return !value.enabled ? keys.every(key => value[key] === null) : text(value.eventType) && value.eventType.length > 0
    && count(value.schemaVersion) && value.schemaVersion > 0 && count(value.maximumEvents) && value.maximumEvents > 0 && value.maximumEvents <= 10000
    && count(value.maximumPayloadBytes) && value.maximumPayloadBytes > 0 && value.maximumPayloadBytes <= 33554432
    && count(value.maximumWindowSeconds) && value.maximumWindowSeconds > 0 && value.maximumWindowSeconds <= 300;
}
/** 只展示当前代次元数据，拒绝超预算、非法终态或错误时间线。 */
export function isMessagingShadowCapture(value: unknown): value is MessagingShadowCapture {
  return isRecord(value) && uuid(value.generation) && text(value.eventType) && count(value.schemaVersion) && value.schemaVersion > 0
    && ['active', 'closed', 'blocked', 'expired'].includes(String(value.state))
    && count(value.maxEvents) && value.maxEvents > 0 && value.maxEvents <= 10000
    && count(value.maxPayloadBytes) && value.maxPayloadBytes > 0 && value.maxPayloadBytes <= 33554432
    && count(value.capturedCount) && value.capturedCount <= value.maxEvents
    && count(value.capturedBytes) && value.capturedBytes <= value.maxPayloadBytes && count(value.missingMetadataCount)
    && timestamp(value.startedAtUtc) && timestamp(value.expiresAtUtc) && Date.parse(value.expiresAtUtc) > Date.parse(value.startedAtUtc)
    && nullableTimestamp(value.closedAtUtc) && nullableText(value.endReason)
    && (value.state === 'closed' || value.state === 'blocked' ? value.closedAtUtc !== null && value.endReason !== null : value.closedAtUtc === null)
    && (value.state !== 'blocked' || value.missingMetadataCount > 0);
}
