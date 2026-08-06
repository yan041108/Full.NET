<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTreeSelect
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import {
  HOST_MENU_COMPONENT_OPTIONS,
  HOST_MENU_ICON_OPTIONS,
  type FullNetProblemDetails,
  type HostMenu,
  type HostMenuIcon
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostMenu,
  disableHostMenu,
  listHostMenuPermissionOptions,
  listHostMenus,
  updateHostMenu
} from '../api/menus';

defineOptions({ name: 'MenusView' });

type EditorMode = 'create' | 'edit';
type MenuTableColumnKey = 'componentKey' | 'requiredPermission' | 'status' | 'displayOrder';

interface MenuTreeOption {
  value: string;
  label: string;
  children?: MenuTreeOption[];
}

interface AppliedFilters {
  routeName: string;
  title: string;
  componentKey: string;
  status: '' | 'active' | 'inactive';
}

interface MenuEditorForm {
  routeName: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: HostMenuIcon | string;
  parentId: string | null;
  displayOrder: number;
  requiredPermission: string;
}

const ROUTE_NAME_PATTERN = /^[a-z][a-z0-9-]{2,63}$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allMenus = ref<HostMenu[]>([]);
const permissionOptions = ref<Array<{
  code: string;
  label: string;
  disabled?: boolean;
}>>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({
  routeName: '',
  title: '',
  componentKey: '',
  status: ''
});
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingMenu = ref<HostMenu | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive<MenuEditorForm>({
  routeName: '',
  componentKey: HOST_MENU_COMPONENT_OPTIONS[0]?.componentKey ?? 'overview',
  title: '',
  caption: '',
  icon: HOST_MENU_ICON_OPTIONS[0] ?? 'grid',
  parentId: null as string | null,
  displayOrder: 50,
  requiredPermission: ''
});
const fieldErrors = reactive({
  routeName: '',
  title: '',
  componentKey: '',
  requiredPermission: ''
});
const columnVisibility = ref<Record<MenuTableColumnKey, boolean>>({
  componentKey: true,
  requiredPermission: true,
  status: true,
  displayOrder: true
});

const componentOptions = HOST_MENU_COMPONENT_OPTIONS;
const iconOptions = HOST_MENU_ICON_OPTIONS;
const menuPermissionOptions = computed(() => {
  const options = [...permissionOptions.value];
  const currentCode = editingMenu.value?.requiredPermission ?? editorForm.requiredPermission;
  if (currentCode && !options.some(option => option.code === currentCode)) {
    options.push({
      code: currentCode,
      label: `[${t('menus.inactive')}] ${currentCode}`,
      disabled: true
    });
  }

  return options;
});

const parentMenuTreeOptions = computed(() =>
  buildMenuParentTreeOptions(allMenus.value, editingMenu.value?.id)
);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

const filteredMenus = computed(() => {
  let rows = allMenus.value;
  const filters = appliedFilters.value;

  if (filters.routeName.trim()) {
    const keyword = filters.routeName.trim().toLowerCase();
    rows = rows.filter(menu => menu.routeName.toLowerCase().includes(keyword));
  }

  if (filters.title.trim()) {
    const keyword = filters.title.trim().toLowerCase();
    rows = rows.filter(menu => menu.title.toLowerCase().includes(keyword));
  }

  if (filters.componentKey) {
    rows = rows.filter(menu => menu.componentKey === filters.componentKey);
  }

  if (filters.status === 'active') {
    rows = rows.filter(menu => menu.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(menu => !menu.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedMenus, resetPage } = useArtClientPagination(filteredMenus);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    {
      key: 'componentKey',
      label: t('menus.componentKey'),
      visible: columnVisibility.value.componentKey
    },
    {
      key: 'requiredPermission',
      label: t('menus.requiredPermission'),
      visible: columnVisibility.value.requiredPermission
    },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    {
      key: 'displayOrder',
      label: t('users.columnSortOrder'),
      visible: columnVisibility.value.displayOrder
    }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as MenuTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'routeName',
    label: t('menus.routeName'),
    placeholder: t('menus.searchRouteNamePlaceholder')
  },
  {
    key: 'title',
    label: t('menus.titleField'),
    placeholder: t('menus.searchTitlePlaceholder')
  },
  {
    key: 'componentKey',
    label: t('menus.componentKey'),
    type: 'select',
    placeholder: t('menus.searchComponentPlaceholder'),
    options: componentOptions.map(option => ({
      label: option.componentKey,
      value: option.componentKey
    }))
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('menus.searchStatusPlaceholder'),
    options: [
      { label: t('menus.active'), value: 'active' },
      { label: t('menus.inactive'), value: 'inactive' }
    ]
  }
]);

const canCreate = computed(() => session.can('identity.menus.create'));
const canUpdate = computed(() => session.can('identity.menus.update'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: MenuTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function buildMenuParentTreeOptions(
  menus: HostMenu[],
  excludeMenuId?: string
): MenuTreeOption[] {
  const excludedIds = new Set<string>();
  if (excludeMenuId) {
    excludedIds.add(excludeMenuId);
    const collectDescendants = (parentId: string): void => {
      for (const menu of menus) {
        if (menu.parentId === parentId && !excludedIds.has(menu.id)) {
          excludedIds.add(menu.id);
          collectDescendants(menu.id);
        }
      }
    };
    collectDescendants(excludeMenuId);
  }

  const nodes = menus
    .filter(menu => menu.isActive && !excludedIds.has(menu.id))
    .map(menu => ({
      id: menu.id,
      parentId: menu.parentId,
      value: menu.id,
      label: `${menu.title} (${menu.routeName})`,
      displayOrder: menu.displayOrder
    }));

  const childrenByParent = new Map<string | null, typeof nodes>();
  for (const node of nodes) {
    const bucket = childrenByParent.get(node.parentId) ?? [];
    bucket.push(node);
    childrenByParent.set(node.parentId, bucket);
  }

  const walk = (parentId: string | null): MenuTreeOption[] =>
    (childrenByParent.get(parentId) ?? [])
      .sort((left, right) =>
        left.displayOrder - right.displayOrder
        || left.label.localeCompare(right.label, 'zh-CN'))
      .map(node => {
        const children = walk(node.id);
        return children.length > 0
          ? { value: node.value, label: node.label, children }
          : { value: node.value, label: node.label };
      });

  return walk(null);
}

function normalizeRouteName(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-+/, '');
}

function resolvePath(componentKey: string): string {
  const entry = componentOptions.find(option => option.componentKey === componentKey);
  return entry?.path ?? '/';
}

function clearFieldErrors(): void {
  fieldErrors.routeName = '';
  fieldErrors.title = '';
  fieldErrors.componentKey = '';
  fieldErrors.requiredPermission = '';
}

function validateRouteName(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const routeName = normalizeRouteName(editorForm.routeName);
  if (!routeName) {
    return t('menus.routeNameRequired');
  }
  if (!ROUTE_NAME_PATTERN.test(routeName)) {
    return t('menus.routeNameInvalid');
  }
  return '';
}

function validateTitle(): string {
  const title = editorForm.title.trim();
  if (!title) {
    return t('menus.titleRequired');
  }
  return '';
}

function validateComponentKey(): string {
  const componentKey = editorForm.componentKey.trim();
  if (!componentKey) {
    return t('menus.componentKeyRequired');
  }
  if (!componentOptions.some(option => option.componentKey === componentKey)) {
    return t('menus.componentKeyRequired');
  }
  return '';
}

function validateRequiredPermission(): string {
  const permission = editorForm.requiredPermission.trim();
  if (!permission) {
    return t('menus.permissionRequired');
  }
  if (!menuPermissionOptions.value.some(option => option.code === permission)) {
    return t('menus.permissionRequired');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.routeName = validateRouteName();
  fieldErrors.title = validateTitle();
  fieldErrors.componentKey = validateComponentKey();
  fieldErrors.requiredPermission = validateRequiredPermission();
  return !fieldErrors.routeName
    && !fieldErrors.title
    && !fieldErrors.componentKey
    && !fieldErrors.requiredPermission;
}

async function fetchAllMenus(): Promise<HostMenu[]> {
  const pageLimit = 100;
  const firstPage = await listHostMenus(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostMenus(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [menus, permissions] = await Promise.all([
      fetchAllMenus(),
      listHostMenuPermissionOptions()
    ]);
    allMenus.value = menus;
    permissionOptions.value = permissions.map(option => ({
      code: option.code,
      label: `[${option.moduleTitle} / ${option.pageTitle}] ${option.displayName}`,
      disabled: false
    }));
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'menus.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    routeName: params.routeName ?? '',
    title: params.title ?? '',
    componentKey: params.componentKey ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { routeName: '', title: '', componentKey: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingMenu.value = null;
  editorForm.routeName = '';
  editorForm.componentKey = componentOptions[0]?.componentKey ?? 'overview';
  editorForm.title = '';
  editorForm.caption = '';
  editorForm.icon = iconOptions[0] ?? 'grid';
  editorForm.parentId = null;
  editorForm.displayOrder = 50;
  editorForm.requiredPermission = permissionOptions.value[0]?.code ?? '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(menu: HostMenu): void {
  if (changing.value || !canUpdate.value) {
    return;
  }
  editorMode.value = 'edit';
  editingMenu.value = menu;
  editorForm.routeName = menu.routeName;
  editorForm.componentKey = menu.componentKey;
  editorForm.title = menu.title;
  editorForm.caption = menu.caption;
  editorForm.icon = menu.icon;
  editorForm.parentId = menu.parentId;
  editorForm.displayOrder = menu.displayOrder;
  editorForm.requiredPermission = menu.requiredPermission;
  clearFieldErrors();
  editorOpen.value = true;
}

function onEditorRouteNameBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.routeName = normalizeRouteName(editorForm.routeName);
  fieldErrors.routeName = validateRouteName();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.routeName = normalizeRouteName(editorForm.routeName);
  }
  editorForm.title = editorForm.title.trim();
  if (!applyFieldErrors()) {
    return;
  }
  if (editorMode.value === 'create') {
    await create();
    return;
  }
  await saveEdit();
}

async function create(): Promise<void> {
  if (!canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const title = editorForm.title.trim();
    const caption = editorForm.caption.trim() || title;
    await createHostMenu({
      parentId: editorForm.parentId,
      routeName: editorForm.routeName,
      path: resolvePath(editorForm.componentKey),
      componentKey: editorForm.componentKey,
      title,
      caption,
      icon: editorForm.icon,
      displayOrder: editorForm.displayOrder,
      requiredPermission: editorForm.requiredPermission
    });
    editorOpen.value = false;
    ElMessage.success(t('menus.createSuccess'));
    await load();
    await session.reloadContext();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const menu = editingMenu.value;
  if (!canUpdate.value || !menu) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const title = editorForm.title.trim();
    const caption = editorForm.caption.trim() || title;
    const path = menu.isSystem ? menu.path : resolvePath(editorForm.componentKey);
    const componentKey = menu.isSystem ? menu.componentKey : editorForm.componentKey;
    const requiredPermission = menu.isSystem
      ? menu.requiredPermission
      : editorForm.requiredPermission;
    await updateHostMenu(menu.id, {
      parentId: editorForm.parentId,
      path,
      componentKey,
      title,
      caption,
      icon: editorForm.icon,
      displayOrder: editorForm.displayOrder,
      requiredPermission,
      version: menu.version
    });
    editorOpen.value = false;
    ElMessage.success(t('menus.updateSuccess'));
    await load();
    await session.reloadContext();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(menu: HostMenu): Promise<void> {
  if (changing.value || !menu.isActive || !session.can('identity.menus.disable')) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('menus.confirmDisable', { name: menu.routeName }),
      t('menus.disable'),
      {
        type: 'warning',
        confirmButtonText: t('menus.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableHostMenu(menu.id);
    ElMessage.success(t('menus.disableSuccess'));
    await load();
    await session.reloadContext();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'menus.loadFailed' | 'menus.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_menu_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="menus-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('menus.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="4"
      :search-label="t('menus.query')"
      :reset-label="t('menus.reset')"
      :expand-label="t('menus.expand')"
      :collapse-label="t('menus.collapse')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:columns="tableColumns"
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,columns,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="identity.menus.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="menus-action-create"
                @click="openCreate"
              >
                {{ t('menus.addMenu') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedMenus.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedMenus"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('menus.routeName')" min-width="220">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.routeName.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.title }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.routeName }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('componentKey')"
              :label="t('menus.componentKey')"
              min-width="140"
              prop="componentKey"
            />

            <el-table-column
              v-if="isColumnVisible('requiredPermission')"
              :label="t('menus.requiredPermission')"
              min-width="200"
              prop="requiredPermission"
            />

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="120"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-tag-group">
                  <el-tag v-if="row.isSystem" type="warning">{{ t('menus.system') }}</el-tag>
                  <el-tag :type="row.isActive ? 'success' : 'info'">
                    {{ t(row.isActive ? 'menus.active' : 'menus.inactive') }}
                  </el-tag>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('displayOrder')"
              :label="t('users.columnSortOrder')"
              width="88"
              align="center"
              prop="displayOrder"
            />

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate code="identity.menus.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="menus-action-edit"
                      :title="t('menus.edit')"
                      :disabled="changing"
                  @click="openEdit(row as HostMenu)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="identity.menus.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="menus-action-disable"
                      :title="t('menus.disable')"
                      :disabled="changing"
                  @click="disable(row as HostMenu)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('menus.emptyDirectory') }}</template>
          </el-table>

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :total="total"
              background
              layout="total, sizes, prev, pager, next, jumper"
              :page-sizes="[10, 20, 50, 100]"
            />
          </div>
        </div>
      </div>
    </el-card>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('menus.createDialogTitle') : t('menus.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="menus-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="menus-editor-form"
        :model="editorForm"
        label-width="108px"
        class="menus-editor-form"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('menus.routeName')"
          prop="routeName"
          required
          :error="fieldErrors.routeName || undefined"
        >
          <el-input
            v-model="editorForm.routeName"
            :placeholder="t('menus.routeNamePlaceholder')"
            @blur="onEditorRouteNameBlur"
            @update:model-value="fieldErrors.routeName = validateRouteName()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('menus.routeName')">
          <el-input v-model="editorForm.routeName" disabled />
        </el-form-item>

        <el-form-item
          :label="t('menus.componentKey')"
          prop="componentKey"
          required
          :error="fieldErrors.componentKey || undefined"
        >
          <el-select
            v-model="editorForm.componentKey"
            :disabled="editorMode === 'edit' && editingMenu?.isSystem === true"
            @update:model-value="fieldErrors.componentKey = validateComponentKey()"
          >
            <el-option
              v-for="option in componentOptions"
              :key="option.componentKey"
              :label="option.componentKey"
              :value="option.componentKey"
            />
          </el-select>
        </el-form-item>

        <el-form-item :label="t('menus.parentMenu')">
          <el-tree-select
            v-model="editorForm.parentId"
            :data="parentMenuTreeOptions"
            clearable
            check-strictly
            :render-after-expand="false"
            :placeholder="t('menus.parentMenuPlaceholder')"
          />
        </el-form-item>

        <el-form-item :label="t('menus.icon')" required>
          <el-select v-model="editorForm.icon">
            <el-option
              v-for="icon in iconOptions"
              :key="icon"
              :label="icon"
              :value="icon"
            />
          </el-select>
        </el-form-item>

        <el-form-item
          :label="t('menus.titleField')"
          prop="title"
          required
          :error="fieldErrors.title || undefined"
        >
          <el-input
            v-model="editorForm.title"
            :placeholder="t('menus.titlePlaceholder')"
            @update:model-value="fieldErrors.title = validateTitle()"
          />
        </el-form-item>

        <el-form-item :label="t('menus.captionField')">
          <el-input
            v-model="editorForm.caption"
            :placeholder="t('menus.captionPlaceholder')"
          />
        </el-form-item>

        <el-form-item :label="t('menus.displayOrder')">
          <el-input
            v-model.number="editorForm.displayOrder"
            type="number"
            :min="0"
            :max="9999"
          />
        </el-form-item>

        <p
          v-if="editorMode === 'edit' && editingMenu?.isSystem"
          class="menus-editor-form__hint"
        >
          {{ t('menus.systemLockedHint') }}
        </p>

        <el-form-item
          :label="t('menus.requiredPermission')"
          prop="requiredPermission"
          required
          :error="fieldErrors.requiredPermission || undefined"
        >
          <el-select
            v-model="editorForm.requiredPermission"
            :disabled="editorMode === 'edit' && editingMenu?.isSystem === true"
            @update:model-value="fieldErrors.requiredPermission = validateRequiredPermission()"
          >
            <el-option
              v-for="permission in menuPermissionOptions"
              :key="permission.code"
              :label="permission.label"
              :value="permission.code"
              :disabled="permission.disabled"
            />
          </el-select>
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.menus-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.menus-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.menus-editor-form {
  padding-top: 8px;
}

.menus-editor-form__hint {
  margin: 0 0 12px 108px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
  line-height: 1.5;
}

.art-sr-heading {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
