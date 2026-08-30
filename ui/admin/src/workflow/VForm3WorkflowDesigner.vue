<script setup lang="ts">
import { nextTick, ref, watch } from 'vue';
import {
  VForm3DesignerHost,
  type VForm3DesignerHostInstance
} from '@fullnet/admin-form-designer';
import type {
  WorkflowFormComponentCatalogResponse,
  WorkflowFormSchema
} from '@fullnet/client-contracts';
import {
  fromVFormDesignerJson,
  toVFormDesignerJson
} from './vform3-adapter';

const props = defineProps<{
  schema: WorkflowFormSchema;
  catalog: WorkflowFormComponentCatalogResponse;
  disabled: boolean;
}>();
const emit = defineEmits<{
  'update:schema': [schema: WorkflowFormSchema];
  'validation-error': [code: string];
}>();
const designer = ref<VForm3DesignerHostInstance>();
watch(() => props.schema, loadSchema, { deep: true });

async function loadSchema(): Promise<void> {
  await nextTick();
  designer.value?.setFormJson(toVFormDesignerJson(props.schema));
}

function readSchema(): WorkflowFormSchema {
  try {
    const raw = designer.value?.getFormJson();
    if (raw === undefined) throw new Error('client.invalid_workflow_form_draft');
    const schema = fromVFormDesignerJson(raw, props.catalog);
    emit('update:schema', schema);
    return schema;
  } catch (error: unknown) {
    const code = error instanceof Error ? error.message : 'client.invalid_workflow_form_draft';
    emit('validation-error', code);
    throw error;
  }
}

defineExpose({ readSchema, loadSchema });
</script>

<template>
  <VForm3DesignerHost
    ref="designer"
    :disabled="disabled"
    data-testid="vform3-workflow-designer"
    @ready="loadSchema"
    @error="emit('validation-error', $event)"
  />
</template>
