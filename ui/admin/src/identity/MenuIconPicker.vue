<script setup lang="ts">

import { computed, ref, watch } from 'vue';

import { ElButton, ElInput, ElPopover, ElTabPane, ElTabs } from 'element-plus';

import { ArrowDown } from '@element-plus/icons-vue';

import { useAdminI18n } from '../i18n/adminI18n';

import {

  filterMenuIconGroups,

  findMenuIconGroup,

  isIconInMenuIconCatalog,

  MENU_ICON_GROUPS

} from './menu-icon-catalog';

import {

  isIconifyMenuIcon,

  normalizeMenuIconInput

} from './host-menu-icons';

import MenuIconGlyph from './MenuIconGlyph.vue';



const props = defineProps<{

  modelValue: string;

  disabled?: boolean;

}>();



const emit = defineEmits<{

  'update:modelValue': [value: string];

}>();



const { t } = useAdminI18n();

const open = ref(false);

const search = ref('');

const customIconInput = ref('');

const customIconError = ref(false);

const activeGroupId = ref(MENU_ICON_GROUPS[0]!.id);



const tabGroups = computed(() => {

  const normalized = search.value.trim();

  if (!normalized) {

    return MENU_ICON_GROUPS.map(group => ({

      ...group,

      icons: [...group.icons]

    }));

  }



  return filterMenuIconGroups(normalized);

});



const currentGroupLabel = computed(() => {

  const group = findMenuIconGroup(props.modelValue);

  return group ? t(group.titleKey) : t('menus.iconGroupUnknown');

});



const searchCustomCandidate = computed(() => {

  const normalized = normalizeMenuIconInput(search.value);

  if (!isIconifyMenuIcon(normalized)) {

    return null;

  }



  if (isIconInMenuIconCatalog(normalized)) {

    return null;

  }



  return normalized;

});



const customInputCandidate = computed(() => {

  const normalized = normalizeMenuIconInput(customIconInput.value);

  return isIconifyMenuIcon(normalized) ? normalized : null;

});



watch(tabGroups, groups => {

  if (!groups.some(group => group.id === activeGroupId.value)) {

    activeGroupId.value = groups[0]?.id ?? MENU_ICON_GROUPS[0]!.id;

  }

});



watch(customIconInput, () => {

  customIconError.value = false;

});



function selectIcon(icon: string): void {

  emit('update:modelValue', icon);

  open.value = false;

  search.value = '';

  customIconInput.value = '';

  customIconError.value = false;

}



function applyCustomIcon(icon?: string): void {

  const candidate = normalizeMenuIconInput(icon ?? customIconInput.value);

  if (!isIconifyMenuIcon(candidate)) {

    customIconError.value = true;

    return;

  }



  selectIcon(candidate);

}



function onPopoverShow(): void {

  search.value = '';

  customIconError.value = false;

  activeGroupId.value = findMenuIconGroup(props.modelValue)?.id

    ?? MENU_ICON_GROUPS[0]!.id;



  if (

    isIconifyMenuIcon(props.modelValue)

    && !isIconInMenuIconCatalog(props.modelValue)

  ) {

    customIconInput.value = props.modelValue;

    return;

  }



  customIconInput.value = '';

}

</script>



<template>

  <el-popover

    v-model:visible="open"

    trigger="click"

    placement="bottom-start"

    :width="720"

    :disabled="disabled"

    popper-class="menu-icon-picker__popover"

    @show="onPopoverShow"

  >

    <template #reference>

      <button

        type="button"

        class="menu-icon-picker__trigger"

        :class="{ 'menu-icon-picker__trigger--disabled': disabled }"

        :disabled="disabled"

        data-testid="menu-icon-picker-trigger"

      >

        <span class="menu-icon-picker__trigger-preview">

          <MenuIconGlyph :icon="modelValue" :size="18" />

        </span>

        <span class="menu-icon-picker__trigger-text">

          <span class="menu-icon-picker__trigger-label">{{ modelValue }}</span>

          <span class="menu-icon-picker__trigger-group">{{ currentGroupLabel }}</span>

        </span>

        <ArrowDown class="menu-icon-picker__trigger-arrow" />

      </button>

    </template>



    <div class="menu-icon-picker__panel" data-testid="menu-icon-picker-panel">

      <el-input

        v-model="search"

        clearable

        :placeholder="t('menus.iconSearchPlaceholder')"

        data-testid="menu-icon-picker-search"

      />



      <button

        v-if="searchCustomCandidate"

        type="button"

        class="menu-icon-picker__custom-banner"

        data-testid="menu-icon-picker-search-custom"

        @click="applyCustomIcon(searchCustomCandidate)"

      >

        <MenuIconGlyph :icon="searchCustomCandidate" :size="22" />

        <span>{{ t('menus.iconSearchUseCustom', { icon: searchCustomCandidate }) }}</span>

      </button>



      <el-tabs

        v-if="tabGroups.length > 0"

        v-model="activeGroupId"

        class="menu-icon-picker__tabs"

      >

        <el-tab-pane

          v-for="group in tabGroups"

          :key="group.id"

          :name="group.id"

          :label="t(group.tabTitleKey)"

          :data-testid="`menu-icon-group-${group.id}`"

        >

          <div

            class="menu-icon-picker__grid"

            role="listbox"

            :aria-label="t(group.titleKey)"

          >

            <button

              v-for="icon in group.icons"

              :key="`${group.id}:${icon}`"

              type="button"

              class="menu-icon-picker__item"

              :class="{ 'menu-icon-picker__item--active': icon === modelValue }"

              role="option"

              :aria-selected="icon === modelValue"

              :data-testid="`menu-icon-option-${icon}`"

              @click="selectIcon(icon)"

            >

              <MenuIconGlyph :icon="icon" :size="22" />

              <span>{{ icon }}</span>

            </button>

          </div>

        </el-tab-pane>

      </el-tabs>



      <p v-else-if="!searchCustomCandidate" class="menu-icon-picker__empty">

        {{ t('menus.iconSearchEmpty') }}

      </p>



      <section class="menu-icon-picker__custom" data-testid="menu-icon-picker-custom">

        <h4 class="menu-icon-picker__custom-title">

          {{ t('menus.iconCustomTitle') }}

        </h4>

        <p class="menu-icon-picker__custom-hint">

          {{ t('menus.iconCustomHint') }}

        </p>

        <div class="menu-icon-picker__custom-row">

          <el-input

            v-model="customIconInput"

            clearable

            :placeholder="t('menus.iconCustomPlaceholder')"

            data-testid="menu-icon-picker-custom-input"

            @keyup.enter="applyCustomIcon()"

          />

          <el-button

            type="primary"

            :disabled="!customInputCandidate"

            data-testid="menu-icon-picker-custom-apply"

            @click="applyCustomIcon()"

          >

            {{ t('menus.iconCustomApply') }}

          </el-button>

        </div>

        <div

          v-if="customInputCandidate"

          class="menu-icon-picker__custom-preview"

        >

          <MenuIconGlyph :icon="customInputCandidate" :size="28" />

          <span>{{ customInputCandidate }}</span>

        </div>

        <p

          v-if="customIconError"

          class="menu-icon-picker__custom-error"

        >

          {{ t('menus.iconCustomInvalid') }}

        </p>

      </section>

    </div>

  </el-popover>

</template>



<style scoped>

.menu-icon-picker__trigger {

  display: flex;

  align-items: center;

  gap: 10px;

  width: 100%;

  min-height: 32px;

  padding: 0 12px;

  border: 1px solid var(--el-border-color);

  border-radius: var(--el-border-radius-base);

  background: var(--el-fill-color-blank);

  color: var(--el-text-color-regular);

  cursor: pointer;

  transition: border-color 0.2s ease;

}



.menu-icon-picker__trigger:hover:not(:disabled) {

  border-color: var(--el-color-primary);

}



.menu-icon-picker__trigger--disabled {

  cursor: not-allowed;

  background: var(--el-fill-color-light);

  color: var(--el-text-color-placeholder);

}



.menu-icon-picker__trigger-preview {

  display: inline-flex;

  align-items: center;

  justify-content: center;

  width: 28px;

  height: 28px;

  border-radius: 6px;

  background: var(--el-fill-color-light);

  color: var(--el-color-primary);

}



.menu-icon-picker__trigger-text {

  display: grid;

  flex: 1;

  gap: 2px;

  text-align: left;

}



.menu-icon-picker__trigger-label {

  font-size: 14px;

  line-height: 1.2;

}



.menu-icon-picker__trigger-group {

  color: var(--el-text-color-secondary);

  font-size: 12px;

  line-height: 1.2;

}



.menu-icon-picker__trigger-arrow {

  width: 14px;

  height: 14px;

  color: var(--el-text-color-secondary);

}



.menu-icon-picker__panel {

  display: grid;

  gap: 12px;

}



.menu-icon-picker__custom-banner {

  display: flex;

  align-items: center;

  gap: 10px;

  width: 100%;

  padding: 10px 12px;

  border: 1px dashed var(--el-color-primary-light-5);

  border-radius: 8px;

  background: var(--el-color-primary-light-9);

  color: var(--el-color-primary);

  cursor: pointer;

  text-align: left;

}



.menu-icon-picker__custom-banner:hover {

  border-color: var(--el-color-primary);

  background: var(--el-color-primary-light-8);

}



.menu-icon-picker__tabs :deep(.el-tabs__header) {

  margin: 0 0 12px;

}



.menu-icon-picker__tabs :deep(.el-tabs__nav-wrap) {

  overflow: auto hidden;

}



.menu-icon-picker__tabs :deep(.el-tabs__nav-wrap::after) {

  height: 1px;

}



.menu-icon-picker__tabs :deep(.el-tabs__nav) {

  flex-wrap: nowrap;

}



.menu-icon-picker__tabs :deep(.el-tabs__item) {

  height: 32px;

  padding: 0 16px;

  font-size: 13px;

  white-space: nowrap;

}



.menu-icon-picker__tabs :deep(.el-tabs__content) {

  max-height: 280px;

  overflow: auto;

}



.menu-icon-picker__grid {

  display: grid;

  grid-template-columns: repeat(6, minmax(0, 1fr));

  gap: 10px;

}



.menu-icon-picker__item {

  display: grid;

  gap: 6px;

  justify-items: center;

  padding: 10px 6px;

  border: 1px solid transparent;

  border-radius: 8px;

  background: transparent;

  color: var(--el-text-color-regular);

  cursor: pointer;

  transition:

    border-color 0.2s ease,

    background-color 0.2s ease,

    color 0.2s ease;

}



.menu-icon-picker__item:hover {

  border-color: var(--el-border-color);

  background: var(--el-fill-color-light);

}



.menu-icon-picker__item--active {

  border-color: var(--el-color-primary-light-5);

  background: var(--el-color-primary-light-9);

  color: var(--el-color-primary);

}



.menu-icon-picker__item span {

  font-size: 11px;

  line-height: 1.2;

  word-break: break-all;

  text-align: center;

}



.menu-icon-picker__empty {

  margin: 0;

  padding: 24px 0;

  color: var(--el-text-color-secondary);

  font-size: 13px;

  text-align: center;

}



.menu-icon-picker__custom {

  display: grid;

  gap: 8px;

  padding-top: 12px;

  border-top: 1px solid var(--el-border-color-lighter);

}



.menu-icon-picker__custom-title {

  margin: 0;

  font-size: 14px;

  font-weight: 600;

  color: var(--el-text-color-primary);

}



.menu-icon-picker__custom-hint {

  margin: 0;

  color: var(--el-text-color-secondary);

  font-size: 12px;

  line-height: 1.5;

}



.menu-icon-picker__custom-row {

  display: grid;

  grid-template-columns: minmax(0, 1fr) auto;

  gap: 8px;

}



.menu-icon-picker__custom-preview {

  display: flex;

  align-items: center;

  gap: 10px;

  padding: 8px 10px;

  border-radius: 8px;

  background: var(--el-fill-color-light);

  color: var(--el-text-color-regular);

  font-size: 13px;

}



.menu-icon-picker__custom-error {

  margin: 0;

  color: var(--el-color-danger);

  font-size: 12px;

}

</style>



<style>

.menu-icon-picker__popover {

  box-sizing: border-box;

  width: 720px !important;

  max-width: min(720px, calc(100vw - 32px)) !important;

  padding: 16px !important;

}



.menu-icon-picker__popover .menu-icon-picker__panel {

  width: 100%;

}

</style>

