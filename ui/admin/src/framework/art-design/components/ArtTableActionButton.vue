<script setup lang="ts">
import { computed } from 'vue';
import {
  Delete,
  EditPen,
  Key,
  UserFilled,
  View
} from '@element-plus/icons-vue';
import { ElIcon } from 'element-plus';

defineOptions({ name: 'ArtTableActionButton' });

const props = defineProps<{
  type: 'edit' | 'delete' | 'view' | 'roles' | 'password';
  testId?: string;
  title?: string;
}>();

const emit = defineEmits<{
  click: [];
}>();

const icon = computed(() => {
  if (props.type === 'edit') return EditPen;
  if (props.type === 'delete') return Delete;
  if (props.type === 'roles') return UserFilled;
  if (props.type === 'password') return Key;
  return View;
});
</script>

<template>
  <button
    type="button"
    class="art-table-action-btn"
    :class="`art-table-action-btn--${type}`"
    :data-testid="testId"
    :title="title"
    @click="emit('click')"
  >
    <ElIcon :size="16"><component :is="icon" /></ElIcon>
  </button>
</template>

<style scoped>
.art-table-action-btn {
  display: inline-grid;
  width: 32px;
  height: 32px;
  margin-right: 8px;
  place-items: center;
  border: 0;
  border-radius: 8px;
  cursor: pointer;
}

.art-table-action-btn--edit {
  background: rgb(64 158 255 / 12%);
  color: var(--art-theme-color);
}

.art-table-action-btn--delete {
  background: rgb(245 108 108 / 12%);
  color: #f56c6c;
}

.art-table-action-btn--view {
  background: rgb(103 194 58 / 12%);
  color: #67c23a;
}

.art-table-action-btn--roles {
  background: rgb(144 147 153 / 12%);
  color: #606266;
}

.art-table-action-btn--password {
  background: rgb(230 162 60 / 12%);
  color: #e6a23c;
}

.art-table-action-btn .el-icon {
  display: inline-flex;
}
</style>
