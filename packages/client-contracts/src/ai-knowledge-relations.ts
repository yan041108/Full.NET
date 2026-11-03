import type { AiKnowledgeSearchRelation, AiKnowledgeSearchRelationsResponse, ReadAiKnowledgeSearchRelationsRequest } from './generated/models.generated.js';
import { readAiKnowledgeSearchRelationsResponse } from './generated/guards.generated.js';
import { isAiKnowledgeSourceLocation, type AiKnowledgeSourceLocation } from './ai-knowledge-location.js';
import { isAiKnowledgeCorrectionCitation, isAiKnowledgeSourceReference } from './ai-chat.js';
import type { AiKnowledgeSearchContextResult } from './ai-knowledge-search.js';
import { sameKnowledgeSource, validKnowledgeSourcePosition } from './ai-knowledge-source-identity.js';

export type AiKnowledgeSearchRelationsResult = Omit<AiKnowledgeSearchRelationsResponse, 'blocks' | 'relations'> & {
  readonly blocks: AiKnowledgeSearchContextResult['blocks'];
  readonly relations: readonly (Omit<AiKnowledgeSearchRelation, 'parentLocation' | 'childLocation'> & {
    readonly parentLocation: AiKnowledgeSourceLocation; readonly childLocation: AiKnowledgeSourceLocation;
  })[];
};
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const positive = (value: number) => Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
const sequence = (value: number) => Number.isSafeInteger(value) && value >= 0 && value <= 255;
// 坐标只比较协议字段，属性排列顺序不构成关系差异，也不读取附加数据。
function locationKey(p: AiKnowledgeSourceLocation): string {
  const g = p.grid; const s = p.slide; const i = p.image;
  return JSON.stringify([p.blockSequence, p.kind, p.pageNumber, p.paragraphNumber, p.tableNumber, p.rowNumber, p.cellNumber, p.htmlPath,
    g && [g.sheetNumber, g.sheetName, g.row, g.column], s && [s.slideNumber, s.shapeNumber, s.rowNumber, s.columnNumber, s.rowSpan, s.columnSpan],
    i && [i.frameNumber, i.imageWidth, i.imageHeight, i.x, i.y, i.width, i.height, i.confidence, i.tableNumber, i.rowNumber, i.columnNumber,
      i.parent && locationKey(i.parent)]]);
}
function locations(block: AiKnowledgeSearchContextResult['blocks'][number]): readonly AiKnowledgeSourceLocation[] {
  return block.correction ? block.correction.spans.flatMap(span => span.sourceLocation ? [span.sourceLocation] : []) : block.sourceLocation ? [block.sourceLocation] : [];
}

/** 每条边绑定真实父位置和双方正文，禁止把邻接块、补写或混代响应作为结构关系。 */
export function validateKnowledgeSearchRelations(value: AiKnowledgeSearchRelationsResponse, input: ReadAiKnowledgeSearchRelationsRequest): AiKnowledgeSearchRelationsResult {
  const invalid = () => new Error('Invalid published knowledge relationship response.');
  value = readAiKnowledgeSearchRelationsResponse(value); const source = value.source;
  if (value.strategy !== 'embedded_image_parent_v1' || !sameKnowledgeSource(source, input.source)
    || !guid.test(source.knowledgeBaseId) || !guid.test(source.documentId) || !guid.test(source.versionId)
    || !guid.test(source.publicationId) || !guid.test(source.generationId) || !positive(source.knowledgeBaseVersion)
    || !positive(source.documentVersion) || !/^[0-9a-f]{64}$/i.test(source.manifestHash)
    || value.anchorSequence !== input.anchorSequence || !sequence(value.anchorSequence)
    || !value.documentTitle.trim() || value.documentTitle.length > 200 || !positive(value.documentMetadataVersion)
    || value.documentMetadataVersion < source.documentVersion || value.blocks.length < 1 || value.blocks.length > 5
    || value.relations.length > 128 || !['none', 'complete', 'parent_not_indexed'].includes(value.relationState)) throw invalid();
  let size = 0; let previous = -1;
  for (const block of value.blocks) {
    if (!validKnowledgeSourcePosition(source, block) || !sequence(block.sequence) || block.sequence <= previous || !block.text || block.text.length > 32768
      || !isAiKnowledgeSourceReference({ ...block, documentId: source.documentId, versionId: source.versionId })
      || (block.correction !== null ? !isAiKnowledgeCorrectionCitation(block.correction) || block.correction.textLength !== block.text.length
          || block.offset !== null || block.length !== null || block.sourceLocation !== null
        : block.offset === null || !Number.isSafeInteger(block.offset) || block.offset < 0 || block.length !== block.text.length
          || block.offset + block.length > 2147483647)) throw invalid();
    previous = block.sequence; size += block.text.length; if (size > 32768) throw invalid();
  }
  const checked = value as AiKnowledgeSearchRelationsResult;
  const anchor = checked.blocks.find(block => block.sequence === input.anchorSequence); if (!anchor) throw invalid();
  const anchorLocations = locations(anchor); const used = new Set([anchor.sequence]); const seen = new Set<string>();
  for (const edge of checked.relations) {
    if (!sequence(edge.childSequence) || edge.parentSequence !== null && !sequence(edge.parentSequence)
      || !isAiKnowledgeSourceLocation(edge.parentLocation, true) || !isAiKnowledgeSourceLocation(edge.childLocation, false, true)
      || !edge.childLocation.image?.parent || edge.parentLocation.blockSequence >= edge.childLocation.blockSequence
      || locationKey(edge.childLocation.image.parent) !== locationKey(edge.parentLocation)
      || !anchorLocations.some(location => locationKey(location.image?.parent ?? location) === locationKey(edge.parentLocation))) throw invalid();
    const child = checked.blocks.find(block => block.sequence === edge.childSequence);
    const parent = checked.blocks.find(block => block.sequence === edge.parentSequence);
    if (!child || !locations(child).some(location => locationKey(location) === locationKey(edge.childLocation))
      || (edge.parentSequence === null ? checked.blocks.some(block => locations(block).some(location => locationKey(location) === locationKey(edge.parentLocation)))
        : !parent || !locations(parent).some(location => locationKey(location) === locationKey(edge.parentLocation)))) throw invalid();
    const key = `${edge.parentSequence}:${edge.childSequence}:${locationKey(edge.childLocation)}:${locationKey(edge.parentLocation)}`;
    if (seen.has(key)) throw invalid(); seen.add(key); used.add(edge.childSequence); if (edge.parentSequence !== null) used.add(edge.parentSequence);
  }
  if (checked.blocks.some(block => !used.has(block.sequence))
    || anchorLocations.some(location => location.image?.parent && !checked.relations.some(edge => edge.childSequence === anchor.sequence && locationKey(edge.childLocation) === locationKey(location)))
    || (checked.relationState === 'none' ? checked.relations.length !== 0 || checked.blocks.length !== 1
      : checked.relations.length === 0 || (checked.relationState === 'parent_not_indexed') !== checked.relations.some(edge => edge.parentSequence === null))) throw invalid();
  return checked;
}
