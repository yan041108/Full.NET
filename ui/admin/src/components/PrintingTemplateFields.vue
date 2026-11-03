<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import type { PrintingFormSchemaDefinition } from '@fullnet/client-contracts';
import { getPrintingFormSchema } from '../api/printing-templates';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ formSchemaKey: string; disabled: boolean }>();
const emit = defineEmits<{ insert: [fieldKey: string] }>();
const { t } = useAdminI18n();
const schema = ref<PrintingFormSchemaDefinition>(), loading = ref(false), failed = ref(false);
let version = 0, request: AbortController | undefined, timer: ReturnType<typeof setTimeout> | undefined;
const fieldLabels = {
  tenantName: 'printingTemplates.fieldTenantName', tenantCode: 'printingTemplates.fieldTenantCode',
  tenantDomain: 'printingTemplates.fieldTenantDomain', printedByDisplayName: 'printingTemplates.fieldPrintedBy',
  printedAtUtc: 'printingTemplates.fieldPrintedAt'
} as const;
function label(field: PrintingFormSchemaDefinition['fields'][number]) {
  const key = fieldLabels[field.fieldKey as keyof typeof fieldLabels];
  return key ? t(key) : field.displayName;
}
function stop() { version++; request?.abort(); if (timer) clearTimeout(timer); loading.value = false; }
async function load() {
  stop(); schema.value = undefined; failed.value = false;
  if (!props.formSchemaKey) return;
  const current = version, key = props.formSchemaKey, controller = new AbortController(); request = controller; loading.value = true;
  // 目录请求有界，超时后只允许人工重试，迟到响应不能重新启用字段插入。
  timer = setTimeout(() => { if (current === version) { stop(); failed.value = true; } }, 10_000);
  try {
    const value = await getPrintingFormSchema(key, controller.signal);
    if (current === version && !controller.signal.aborted) schema.value = value;
  } catch { if (current === version && !controller.signal.aborted) failed.value = true; }
  finally { if (current === version) { if (timer) clearTimeout(timer); loading.value = false; } }
}
function insert(fieldKey: string) {
  if (!props.disabled && schema.value?.fields.some(field => field.fieldKey === fieldKey)) emit('insert', fieldKey);
}
watch(() => props.formSchemaKey, load, { immediate: true });
onBeforeUnmount(stop);
</script>

<template>
  <section class="printing-template-fields" :aria-label="t('printingTemplates.fields')" :aria-busy="loading" data-testid="printing-template-fields">
    <p v-if="schema">{{ formSchemaKey === 'printing.tenant_profile_card' ? t('printingTemplates.tenantProfileSchema') : schema.displayName }}</p>
    <p>{{ t('printingTemplates.fieldsHelp') }}</p>
    <span v-if="loading" role="status">{{ t('printingTemplates.fieldsLoading') }}</span>
    <ElAlert v-if="failed" type="warning" :closable="false" :title="t('printingTemplates.fieldsFailed')" />
    <ElButton v-if="failed" :disabled="disabled" data-testid="printing-template-fields-retry" @click="load">{{ t('printingTemplates.fieldsRetry') }}</ElButton>
    <p v-if="schema && !schema.fields.length">{{ t('printingTemplates.fieldsEmpty') }}</p>
    <div v-if="schema" class="field-buttons">
      <ElButton v-for="field in schema.fields" :key="field.fieldKey" :disabled="disabled" :data-field-key="field.fieldKey" @click="insert(field.fieldKey)">
        {{ label(field) }} <code v-text="`{{${field.fieldKey}}}`" />
      </ElButton>
    </div>
  </section>
</template>

<style scoped>
.printing-template-fields { width: 100%; overflow-wrap: anywhere; }
.printing-template-fields p { margin: 0 0 8px; }
.field-buttons { display: flex; flex-wrap: wrap; gap: 8px; }
.field-buttons .el-button { margin-left: 0; max-width: 100%; height: auto; min-height: 32px; white-space: normal; }
.field-buttons code { margin-left: 6px; overflow-wrap: anywhere; }
</style>
