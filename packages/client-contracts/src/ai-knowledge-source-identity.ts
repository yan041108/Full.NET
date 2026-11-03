import type { AiKnowledgeSearchSource } from './generated/models.generated.js';
import type { AiKnowledgeCitation } from './ai-chat.js';

type Identity = AiKnowledgeSearchSource & { sourceKind?: string; faqRevisionId?: string | null; faqId?: string | null };
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const positive = (value: unknown) => typeof value === 'number' && Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
/** 来源协议闭合：FAQ 不能省略根或修订身份，也不能作为普通文件读取。 */
export function isKnowledgeSourceIdentity(value: unknown): value is Identity {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
  const source = value as Identity; const kind = source.sourceKind ?? 'document-v1';
  return [source.knowledgeBaseId, source.documentId, source.versionId, source.publicationId, source.generationId].every(id => typeof id === 'string' && guid.test(id))
    && positive(source.knowledgeBaseVersion) && positive(source.documentVersion) && /^[a-f0-9]{64}$/i.test(source.manifestHash)
    && (kind === 'document-v1' && source.faqRevisionId == null && source.faqId == null
      || kind === 'faq-revision-v1' && typeof source.faqRevisionId === 'string' && guid.test(source.faqRevisionId) && typeof source.faqId === 'string' && guid.test(source.faqId));
}
/** 冻结输入与返回的完整节点必须一致；相同文件身份不能掩盖来源类型漂移。 */
export function sameKnowledgeSource(left: AiKnowledgeSearchSource, right: AiKnowledgeSearchSource): boolean {
  if (!isKnowledgeSourceIdentity(left) || !isKnowledgeSourceIdentity(right)) return false;
  const keys = ['knowledgeBaseId', 'knowledgeBaseVersion', 'documentId', 'documentVersion', 'versionId', 'publicationId', 'generationId', 'manifestHash'] as const;
  return keys.every(key => left[key] === right[key]) && (left.sourceKind ?? 'document-v1') === (right.sourceKind ?? 'document-v1')
    && (left.faqRevisionId ?? null) === (right.faqRevisionId ?? null) && (left.faqId ?? null) === (right.faqId ?? null);
}
/** 将已验证 FAQ 命中交给专属正文入口；普通文件不生成此字段。 */
export function knowledgeFaqCitation(source: AiKnowledgeSearchSource, model: string | null | undefined, version: number | null | undefined): AiKnowledgeCitation['faqSource'] {
  if (!isKnowledgeSourceIdentity(source)) throw new Error('Invalid published source identity.');
  if (source.sourceKind !== 'faq-revision-v1') return undefined;
  if (!model || !guid.test(model) || !positive(version)) throw new Error('Invalid FAQ index model identity.');
  return { source: { ...source }, embeddingModelConfigId: model, embeddingModelVersion: version! };
}
/** FAQ 的坐标属于数据库修订，不能混入文件页码、OCR 或人工原文位置。 */
export function validKnowledgeSourcePosition(source: unknown, position: unknown): boolean {
  if (!isKnowledgeSourceIdentity(source) || position === null || typeof position !== 'object') return false;
  if (source.sourceKind !== 'faq-revision-v1') return true;
  const value = position as Record<string, unknown>;
  return value.pageNumber === null && value.correction == null && value.sourceLocation == null && value.docxLocation == null;
}
