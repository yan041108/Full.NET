<script setup lang="ts">
import { computed } from 'vue';
import type { AiKnowledgeCitation, AiKnowledgeSourceLocation } from '@fullnet/client-contracts';
import { translateKnowledgeDocx, translateKnowledgeFormats } from '@fullnet/admin-i18n';
import { useAdminI18n } from '../i18n/adminI18n';
const props = defineProps<{ citation: AiKnowledgeCitation }>();
const { t, locale } = useAdminI18n();
const position = computed(() => {
  if (props.citation.sourceLocation) return sourcePosition(props.citation.sourceLocation);
  const location = props.citation.docxLocation;
  if (!location) return t('knowledge.position', { page: props.citation.pageNumber ?? t('knowledge.noPage'),
    sequence: props.citation.sequence, offset: props.citation.offset, length: props.citation.length });
  return translateKnowledgeDocx(locale.value, location.tableNumber === null ? 'paragraph' : 'table', {
    paragraph: location.paragraphNumber, table: location.tableNumber ?? 0, row: location.rowNumber ?? 0, cell: location.cellNumber ?? 0,
    offset: props.citation.offset, length: props.citation.length
  });
});
function sourcePosition(location: AiKnowledgeSourceLocation): string {
  const offset = props.citation.offset; const length = props.citation.length;
  if (location.image) {
    const i = location.image;
    const frame = translateKnowledgeFormats(locale.value, 'image', { frame: i.frameNumber, x: i.x, y: i.y, width: i.width, height: i.height, confidence: Math.round(i.confidence * 100) });
    const cell = i.tableNumber === null ? '' : ` · ${translateKnowledgeFormats(locale.value, 'grid', { sheet: i.tableNumber, row: i.rowNumber ?? 0, column: i.columnNumber ?? 0 })}`;
    return `${i.parent ? sourcePosition(i.parent) + ' · ' : ''}${frame}${cell}`;
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
</script>
<template><span data-testid="knowledge-citation-position">{{ position }}</span></template>
