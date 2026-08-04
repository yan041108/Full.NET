<script setup lang="ts">
import { ElButton, ElDialog } from 'element-plus';

defineOptions({ name: 'ArtFormDialog' });

const props = withDefaults(defineProps<{
  open: boolean;
  title: string;
  width?: string;
  saving?: boolean;
  confirmLabel?: string;
  cancelLabel?: string;
  confirmTestId?: string;
  showConfirm?: boolean;
}>(), {
  width: '520px',
  saving: false,
  confirmLabel: '确定',
  cancelLabel: '取消',
  showConfirm: true
});

const emit = defineEmits<{
  'update:open': [value: boolean];
  confirm: [];
  cancel: [];
}>();

function close(): void {
  emit('update:open', false);
  emit('cancel');
}
</script>

<template>
  <el-dialog
    :model-value="open"
    :width="width"
    class="art-form-dialog"
    modal-class="art-form-dialog-modal"
    destroy-on-close
    :show-close="false"
    append-to-body
    align-center
    @update:model-value="emit('update:open', $event)"
  >
    <template #header>
      <div class="art-form-dialog__header">
        <span>{{ title }}</span>
        <button type="button" class="art-form-dialog__close" @click="close">×</button>
      </div>
    </template>

    <slot />

    <template #footer>
      <div class="art-form-dialog__footer">
        <el-button @click="close">{{ cancelLabel }}</el-button>
        <el-button
          v-if="showConfirm"
          type="primary"
          :loading="saving"
          :data-testid="confirmTestId"
          @click="emit('confirm')"
        >
          {{ confirmLabel }}
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.art-form-dialog__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: -8px -8px 0;
  padding: 12px 16px;
  border-radius: 8px 8px 0 0;
  background: var(--art-theme-color);
  color: #fff;
  font-size: 15px;
  font-weight: 600;
}

.art-form-dialog__close {
  border: 0;
  background: transparent;
  color: inherit;
  font-size: 22px;
  line-height: 1;
  cursor: pointer;
}

.art-form-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>