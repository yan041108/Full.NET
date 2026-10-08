<script setup lang="ts">
import { computed } from 'vue';
import type { AiKnowledgeCitation } from '@fullnet/client-contracts';
import { translateKnowledgeDocx } from '@fullnet/admin-i18n';
import { useAdminI18n } from '../i18n/adminI18n';
const props = defineProps<{ citation: AiKnowledgeCitation }>();
const { t, locale } = useAdminI18n();
const position = computed(() => {
  const location = props.citation.docxLocation;
  if (!location) return t('knowledge.position', { page: props.citation.pageNumber ?? t('knowledge.noPage'),
    sequence: props.citation.sequence, offset: props.citation.offset, length: props.citation.length });
  return translateKnowledgeDocx(locale.value, location.tableNumber === null ? 'paragraph' : 'table', {
    paragraph: location.paragraphNumber, table: location.tableNumber ?? 0, row: location.rowNumber ?? 0, cell: location.cellNumber ?? 0,
    offset: props.citation.offset, length: props.citation.length
  });
});
</script>
<template><span data-testid="knowledge-citation-position">{{ position }}</span></template>
