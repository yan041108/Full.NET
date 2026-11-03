<script setup lang="ts">
import { computed } from 'vue';
import type { AiKnowledgeCitation, AiKnowledgeSourceLocation } from '@fullnet/client-contracts';
import { translateKnowledgeDocx, translateKnowledgeFormats } from '@fullnet/admin-i18n';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeFaqSourceText } from '../i18n/ai-knowledge-faq-source';
const props = defineProps<{ citation: AiKnowledgeCitation | Omit<AiKnowledgeCitation, 'id'> }>();
const { t, locale } = useAdminI18n();
const position = computed(() => {
  if (props.citation.faqSource) return `${knowledgeFaqSourceText(locale.value, 'revision')} ${props.citation.faqSource.source.faqRevisionId} · ${knowledgeFaqSourceText(locale.value, 'position')} ${props.citation.sequence}`;
  if (props.citation.correction) return '';
  if (props.citation.offset === null || props.citation.length === null) return t('knowledge.sourceUnavailable');
  if (props.citation.sourceLocation) return sourcePosition(props.citation.sourceLocation, props.citation.offset, props.citation.length);
  const location = props.citation.docxLocation;
  if (!location) return t('knowledge.position', { page: props.citation.pageNumber ?? t('knowledge.noPage'),
    sequence: props.citation.sequence, offset: props.citation.offset, length: props.citation.length });
  return translateKnowledgeDocx(locale.value, location.tableNumber === null ? 'paragraph' : 'table', {
    paragraph: location.paragraphNumber, table: location.tableNumber ?? 0, row: location.rowNumber ?? 0, cell: location.cellNumber ?? 0,
    offset: props.citation.offset, length: props.citation.length
  });
});
function sourcePosition(location: AiKnowledgeSourceLocation, offset: number, length: number): string {
  if (location.image) {
    const i = location.image;
    const frame = translateKnowledgeFormats(locale.value, 'image', { frame: i.frameNumber, x: i.x, y: i.y, width: i.width, height: i.height, confidence: Math.round(i.confidence * 100) });
    const cell = i.tableNumber === null ? '' : ` · ${translateKnowledgeFormats(locale.value, 'grid', { sheet: i.tableNumber, row: i.rowNumber ?? 0, column: i.columnNumber ?? 0 })}`;
    return `${i.parent ? sourcePosition(i.parent, offset, length) + ' · ' : ''}${frame}${cell}`;
  }
  if (location.grid) return translateKnowledgeFormats(locale.value, 'grid', { sheet: location.grid.sheetName, row: location.grid.row, column: location.grid.column });
  if (location.slide) {
    const s = location.slide;
    const place = translateKnowledgeFormats(locale.value, location.kind === 'slide_notes' ? 'notes' : 'slide', { slide: s.slideNumber, shape: s.shapeNumber });
    return s.rowNumber === null ? place : `${place} · ${translateKnowledgeFormats(locale.value, 'grid', { sheet: '', row: s.rowNumber, column: s.columnNumber ?? 0 })}`;
  }
  if (location.htmlPath) return translateKnowledgeFormats(locale.value, 'html', { path: location.htmlPath });
  if (location.paragraphNumber !== null) return translateKnowledgeDocx(locale.value, location.tableNumber === null ? 'paragraph' : 'table', {
    paragraph: location.paragraphNumber, table: location.tableNumber ?? 0, row: location.rowNumber ?? 0, cell: location.cellNumber ?? 0, offset, length });
  return t('knowledge.position', { page: location.pageNumber ?? t('knowledge.noPage'), sequence: location.blockSequence, offset, length });
}
const correctionPositions = computed(() => props.citation.correction?.spans.map(span => {
  const kind = translateKnowledgeFormats(locale.value, span.contentKind);
  const range = translateKnowledgeFormats(locale.value, 'revisionRange', { offset: span.textOffset, length: span.textLength });
  return { kind: span.contentKind, text: `${kind} · ${range}${span.sourceLocation && span.sourceOffset !== null && span.sourceLength !== null
    ? ' · ' + sourcePosition(span.sourceLocation, span.sourceOffset, span.sourceLength) : ''}` };
}) ?? []);
</script>
<template>
  <span v-if="citation.correction" class="correction-positions" data-testid="knowledge-citation-position">
    <span v-for="(span, index) in correctionPositions" :key="index" :data-kind="span.kind" data-testid="knowledge-correction-span">{{ span.text }}</span>
  </span>
  <span v-else data-testid="knowledge-citation-position">{{ position }}</span>
</template>
<style scoped>.correction-positions { display: inline-flex; flex-direction: column; gap: 4px; max-width: 100%; overflow-wrap: anywhere; }</style>
