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
  /** 多层动态弹窗显式维护本层 Tab 边界，避免焦点落到浏览器或下层表单。 */
  trapFocus?: boolean;
}>(), {
  width: '520px',
  saving: false,
  confirmLabel: '确定',
  cancelLabel: '取消',
  showConfirm: true,
  trapFocus: false
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

function containTab(event: KeyboardEvent): void {
  if (!props.trapFocus || event.key !== 'Tab' || event.altKey || event.ctrlKey || event.metaKey) return;
  const container = event.currentTarget;
  if (!(container instanceof HTMLElement)) return;
  const targets = [...container.querySelectorAll<HTMLElement>('a[href],button,input,select,textarea,[tabindex]')]
    .filter(element => element.tabIndex >= 0 && !element.matches(':disabled,[aria-hidden="true"]')
      && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden');
  const first = targets[0];
  const last = targets.at(-1);
  if (!first || !last) return;
  if ((!event.shiftKey && document.activeElement === last) || (event.shiftKey && document.activeElement === first)) {
    event.preventDefault();
    event.stopPropagation();
    (event.shiftKey ? last : first).focus();
  }
}
</script>

<template>
  <el-dialog
    :model-value="open"
    :title="title"
    :width="width"
    class="art-form-dialog"
    modal-class="art-form-dialog-modal"
    destroy-on-close
    :show-close="false"
    append-to-body
    align-center
    @keydown.capture="containTab"
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
