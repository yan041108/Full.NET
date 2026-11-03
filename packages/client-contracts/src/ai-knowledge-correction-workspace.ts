import type { AiKnowledgeCorrectionPreparationResponse, AiKnowledgeCorrectionWorkspaceResponse, AiKnowledgeCorrectionRevisionResponse,
  AiKnowledgeCorrectionRevisionPageResponse, AiKnowledgeCorrectionHeadRequest } from './generated/models.generated.js';
import { isAiKnowledgeSourceLocation, type AiKnowledgeSourceLocation } from './ai-knowledge-location.js';

export type AiKnowledgeCorrectionWorkspace = Omit<AiKnowledgeCorrectionWorkspaceResponse, 'block'> & {
  readonly block: Omit<AiKnowledgeCorrectionWorkspaceResponse['block'], 'parts'> & {
    readonly parts: readonly (Omit<AiKnowledgeCorrectionWorkspaceResponse['block']['parts'][number], 'sourceLocation'> & {
      readonly sourceLocation: AiKnowledgeSourceLocation | null;
    })[];
  };
};
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const hash = /^[0-9a-f]{64}$/;
const integer = (value: number, min: number, max: number) => Number.isSafeInteger(value) && value >= min && value <= max;
const fail = (): never => { throw new Error('client.invalid_knowledge_correction'); };
function text(value: string, maximum: number) {
  if (value.length > maximum) return false;
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) { const next = value.charCodeAt(++index); if (!(next >= 0xdc00 && next <= 0xdfff)) return false; }
    else if (code >= 0xdc00 && code <= 0xdfff || code === 0) return false;
  }
  return true;
}
const layout = (value: string) => value.length <= 64 && /^[\r\n\t ]*$/.test(value);

/** 生成守卫之后继续校验状态关系；来源完成不能被解释成索引或发布完成。 */
export function validateKnowledgeCorrectionPreparation(value: AiKnowledgeCorrectionPreparationResponse, versionId: string, preparationId?: string) {
  if (!guid.test(value.id) || !guid.test(value.versionId) || value.versionId !== versionId || preparationId !== undefined && value.id !== preparationId
    || !integer(value.documentVersion, 1, 2147483646) || !integer(value.attemptCount, 0, 3)
    || !['queued', 'running', 'completed', 'failed', 'cancelled'].includes(value.state)
    || value.state === 'queued' && value.attemptCount !== 0 || ['running', 'completed'].includes(value.state) && value.attemptCount < 1
    || (value.state === 'completed' ? value.sourceId === null || !guid.test(value.sourceId) : value.sourceId !== null)
    || (value.state === 'failed' ? value.failureCode !== 'ai.knowledge.correction_preparation_unavailable'
      : value.state === 'cancelled' ? value.failureCode !== 'ai.knowledge.correction_preparation_stale' : value.failureCode !== null)
    || !Number.isFinite(Date.parse(value.createdAtUtc)) || !Number.isFinite(Date.parse(value.updatedAtUtc))
    || Date.parse(value.updatedAtUtc) < Date.parse(value.createdAtUtc)) fail();
  return Object.freeze({ ...value });
}

/** 一页只接收一个有界块；原文和更正的真实上下文不可缺失，补写不能携带坐标。 */
export function validateKnowledgeCorrectionWorkspace(value: AiKnowledgeCorrectionWorkspaceResponse,
  expected: { versionId: string; block?: number; sourceId?: string; revisionId?: string }): AiKnowledgeCorrectionWorkspace {
  if (value.versionId !== expected.versionId || !guid.test(value.versionId) || !guid.test(value.sourceId)
    || value.isSourceView !== (expected.sourceId !== undefined) || expected.sourceId !== undefined && value.sourceId !== expected.sourceId
    || expected.sourceId !== undefined && expected.revisionId !== undefined || expected.revisionId !== undefined && value.revisionId !== expected.revisionId
    || value.blockIndex !== (expected.block ?? 0) || !integer(value.totalBlocks, 1, 8192) || !integer(value.blockIndex, 0, value.totalBlocks - 1)
    || !integer(value.documentVersion, 1, 2147483646) || !integer(value.headVersion, 0, 2147483646)
    || (value.headVersion === 0 ? value.revisionId !== null : value.revisionId === null || !guid.test(value.revisionId))
    || value.isSourceView && value.revisionId !== null || !hash.test(value.sourceHash) || !hash.test(value.payloadHash)
    || !value.parserRuntimeId.trim() || value.parserRuntimeId.length > 128 || !text(value.parserRuntimeId, 128)
    || !value.originalFileName.trim() || !text(value.originalFileName, 255) || !value.contentType.trim() || value.contentType.length > 128
    || !guid.test(value.block.id) || !layout(value.block.separator) || value.block.parts.length < 1 || value.block.parts.length > 32) fail();
  const ids = new Set([value.block.id]);
  const parts = value.block.parts.map(part => {
    if (!guid.test(part.id) || ids.has(part.id) || !text(part.text, 32768) || !layout(part.prefix)
      || !['original', 'corrected', 'supplement'].includes(part.contentKind)) fail();
    ids.add(part.id);
    if (part.contentKind === 'supplement') {
      if (part.anchorId !== null || part.originalText !== null || part.sourceOffset !== null || part.sourceLength !== null || part.sourceLocation !== null) fail();
    } else {
      // 维护预览允许显示未确认 OCR 的实际低置信度；性质保持 original，不能借此取得模型资格。
      if (part.originalText === null || !text(part.originalText, 2048) || !isAiKnowledgeSourceLocation(part.sourceLocation, false, true)
        || part.sourceOffset === null || !integer(part.sourceOffset, 0, 2097152)
        || part.sourceLength !== part.originalText.length
        || (part.anchorId === null ? part.contentKind !== 'original' || part.text !== '' || part.originalText !== '' || part.sourceOffset !== 0
          : !guid.test(part.anchorId) || part.originalText.length < 1)
        || part.contentKind === 'original' && part.text !== part.originalText) fail();
    }
    return Object.freeze({ ...part, sourceLocation: part.sourceLocation as AiKnowledgeSourceLocation | null });
  });
  return Object.freeze({ ...value, block: Object.freeze({ ...value.block, parts: Object.freeze(parts) }) });
}

/** 回执必须属于原文件版本；提交成功的两层版本、父修订和采用来源逐项绑定原请求。 */
export function validateKnowledgeCorrectionRevision(value: AiKnowledgeCorrectionRevisionResponse, versionId: string,
  write?: { expected: AiKnowledgeCorrectionHeadRequest; sourceId: string; reason: string }) {
  if (!guid.test(value.id) || value.versionId !== versionId || !guid.test(value.sourceId)
    || value.parentId !== null && !guid.test(value.parentId) || !integer(value.headVersion, 1, 2147483647)
    || !integer(value.documentVersion, 2, 2147483647) || !hash.test(value.payloadHash) || !value.reason.trim() || !text(value.reason, 1000)
    || !Number.isFinite(Date.parse(value.createdAtUtc))
    || write && (value.headVersion !== write.expected.headVersion + 1 || value.documentVersion !== write.expected.documentVersion + 1
      || value.parentId !== write.expected.revisionId || value.sourceId !== write.sourceId || value.reason !== write.reason)) fail();
  return Object.freeze({ ...value });
}
/** 历史只接受有界、严格递减且无重复身份的摘要，不把目录当作正文或当前头。 */
export function validateKnowledgeCorrectionHistory(value: AiKnowledgeCorrectionRevisionPageResponse, versionId: string, before?: number) {
  if (value.items.length > 20 || value.nextHeadVersion !== null && (value.items.length !== 20 || value.nextHeadVersion !== value.items.at(-1)?.headVersion)) fail();
  const ids = new Set<string>(); let cursor = before ?? 2147483648;
  const items = value.items.map(item => {
    const row = validateKnowledgeCorrectionRevision(item, versionId);
    if (row.headVersion >= cursor || ids.has(row.id)) fail();
    cursor = row.headVersion; ids.add(row.id); return row;
  });
  return Object.freeze({ items: Object.freeze(items), nextHeadVersion: value.nextHeadVersion });
}
