<script setup lang="ts">
import { useSlots } from 'vue';
import { ElDropdown, ElIcon } from 'element-plus';
import { MoreFilled } from '@element-plus/icons-vue';
import { useAdminI18n } from '../../../i18n/adminI18n';
import { flattenSlotVNodes } from '../utils/flattenSlotVNodes';
import { ART_TABLE_ACTION_MAX_VISIBLE } from './artTableActions';

defineOptions({ name: 'ArtTableActionGroup' });

const props = withDefaults(defineProps<{
  /** 直接展示的操作数量上限；超出部分收入「更多」。 */
  maxVisible?: number;
}>(), {
  maxVisible: ART_TABLE_ACTION_MAX_VISIBLE
});

const { t } = useAdminI18n();
const slots = useSlots();

// 表格复用行时插槽闭包会替换；必须在渲染中读取，不能缓存旧行的身份和事件处理器。
const actionNodes = () => flattenSlotVNodes(slots.default?.() ?? []);
</script>

<template>
  <div class="art-table-action-group" data-testid="art-table-action-group">
    <!-- 每轮只展开一次；局部节点作用域让更多菜单插槽也随当前行更新。 -->
    <template v-for="nodes in [actionNodes()]" :key="'current-actions'">
      <template v-for="(node, index) in nodes.slice(0, props.maxVisible)" :key="`visible-${index}`">
        <component :is="node" />
      </template>

      <ElDropdown
        v-if="nodes.length > props.maxVisible"
        trigger="click"
        placement="bottom-end"
        :teleported="true"
        popper-class="art-table-action-group__dropdown"
      >
        <button
          type="button"
          class="art-table-action-btn art-table-action-btn--more"
          :title="t('table.moreActions')"
          data-testid="art-table-action-more"
          @click.stop
        >
          <ElIcon :size="16"><MoreFilled /></ElIcon>
        </button>
        <template #dropdown>
          <div class="art-table-action-group__overflow-panel" role="menu">
            <template v-for="(node, index) in nodes.slice(props.maxVisible)" :key="`overflow-${index}`">
              <component :is="node" />
            </template>
          </div>
        </template>
      </ElDropdown>
    </template>
  </div>
</template>

<style scoped>
.art-table-action-group {
  display: inline-flex;
  flex-wrap: nowrap;
  align-items: center;
  justify-content: center;
  gap: 4px;
  max-width: 100%;
}

.art-table-action-group :deep(.art-table-action-btn) {
  margin-right: 0;
  flex-shrink: 0;
}

.art-table-action-btn--more {
  display: inline-grid;
  width: 32px;
  height: 32px;
  place-items: center;
  border: 0;
  border-radius: 8px;
  background: rgb(144 147 153 / 12%);
  color: #606266;
  cursor: pointer;
}

.art-table-action-group__overflow-panel {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 8px;
}
</style>

<style>
.art-table-action-group__dropdown.el-popper {
  min-width: auto !important;
  padding: 0 !important;
}
</style>
