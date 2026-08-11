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
  ElPopover,
  ElRadio,
  ElRadioGroup,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTreeSelect,
  type FormInstance,
  type TableInstance
} from 'element-plus';
import { Plus, Folder, Document, Operation } from '@element-plus/icons-vue';
import {
  HOST_MENU_COMPONENT_OPTIONS,
  HOST_MENU_ICON_OPTIONS,
  HOST_MENU_TYPES,
  type FullNetProblemDetails,
  type HostMenuIcon,
  type HostMenuType
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import type { MessageKey } from '@fullnet/admin-i18n';
import {
  createHostMenu,
  disableHostMenu,
  enableHostMenu,
  listHostMenuPermissionOptions,
  listHostMenusAll,
  syncHostMenuCatalog,
  updateHostMenu
} from '../api/menus';
import MenuIconPicker from '../identity/MenuIconPicker.vue';
import {
  buildHostMenuTree,
  buildMenuParentTreeOptions,
  filterMenusForTree,
  isPersistedMenuRow,
  isVirtualCatalogButtonRow,
  menuTypeLabelKey,
  menuTypeTagType,
  mergeCatalogButtonRows,
  type MenuTreeRow
} from '../identity/menu-tree';

defineOptions({ name: 'MenusView' });

type EditorMode = 'create' | 'edit';
type MenuTableColumnKey =
  | 'menuType'
  | 'path'
  | 'componentKey'
  | 'requiredPermission'
  | 'displayOrder'
  | 'status'
  | 'modRecord';

interface AppliedFilters {
  title: string;
  menuType: '' | HostMenuType;
  status: '' | 'active' | 'inactive';
}

interface MenuEditorForm {
  menuType: HostMenuType;
  routeName: string;
  path: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: HostMenuIcon | string;
  parentId: string | null;
  displayOrder: number;
  requiredPermission: string;
  redirect: string;
  linkUrl: string;
  isHidden: boolean;
  isKeepAlive: boolean;
  isAffix: boolean;
  isEmbedded: boolean;
  remark: string;
}

interface PermissionSelectOption {
  code: string;
  label: string;
  disabled?: boolean;
}

const ROUTE_NAME_PATTERN = /^[a-z][a-z0-9-]{2,63}$/;

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const allMenuRows = ref<MenuTreeRow[]>([]);
const permissionOptions = ref<PermissionSelectOption[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({
  title: '',
  menuType: '',
  status: ''
});
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingMenu = ref<MenuTreeRow | null>(null);
const editorFormRef = ref<FormInstance>();
const treeTableRef = ref<TableInstance>();
const editorForm = reactive<MenuEditorForm>({
  menuType: HOST_MENU_TYPES.menu,
  routeName: '',
  path: '',
  componentKey: HOST_MENU_COMPONENT_OPTIONS[0]?.componentKey ?? 'overview',
  title: '',
  caption: '',
  icon: HOST_MENU_ICON_OPTIONS[0] ?? 'grid',
  parentId: null,
  displayOrder: 50,
  requiredPermission: '',
  redirect: '',
  linkUrl: '',
  isHidden: false,
  isKeepAlive: false,
  isAffix: false,
  isEmbedded: false,
  remark: ''
});
const fieldErrors = reactive({
  routeName: '',
  title: '',
  path: '',
  componentKey: '',
  requiredPermission: ''
});
const columnVisibility = ref<Record<MenuTableColumnKey, boolean>>({
  menuType: true,
  path: true,
  componentKey: true,
  requiredPermission: true,
  displayOrder: true,
  status: true,
  modRecord: true
});

const componentOptions = HOST_MENU_COMPONENT_OPTIONS;

const realMenus = computed(() => allMenuRows.value.filter(row => isPersistedMenuRow(row)));

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
  buildMenuParentTreeOptions(
    realMenus.value,
    editorMode.value === 'edit' ? editingMenu.value?.id : undefined
  )
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

const filteredMenuRows = computed(() => {
  const filters = appliedFilters.value;
  return filterMenusForTree(allMenuRows.value, row => {
    if (filters.title.trim()) {
      const keyword = filters.title.trim().toLowerCase();
      if (!row.title.toLowerCase().includes(keyword)) {
        return false;
      }
    }

    if (filters.menuType && row.menuType !== filters.menuType) {
      return false;
    }

    if (filters.status === 'active' && !row.isActive) {
      return false;
    }
    if (filters.status === 'inactive' && row.isActive) {
      return false;
    }

    return true;
  });
});

const menuTree = computed(() => buildHostMenuTree(filteredMenuRows.value));

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'menuType', label: t('menus.menuType'), visible: columnVisibility.value.menuType },
    { key: 'path', label: t('menus.path'), visible: columnVisibility.value.path },
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
    {
      key: 'displayOrder',
      label: t('users.columnSortOrder'),
      visible: columnVisibility.value.displayOrder
    },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    { key: 'modRecord', label: t('menus.modRecord'), visible: columnVisibility.value.modRecord }
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
    key: 'title',
    label: t('menus.titleField'),
    placeholder: t('menus.searchTitleOnlyPlaceholder')
  },
  {
    key: 'menuType',
    label: t('menus.menuType'),
    type: 'select',
    placeholder: t('menus.searchTypePlaceholder'),
    options: [
      { label: t('menus.typeAll'), value: '' },
      { label: t('menus.typeDirectory'), value: HOST_MENU_TYPES.directory },
      { label: t('menus.typeMenu'), value: HOST_MENU_TYPES.menu },
      { label: t('menus.typeButton'), value: HOST_MENU_TYPES.button }
    ]
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
const canDisable = computed(() => session.can('identity.menus.disable'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: MenuTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return index + 1;
}

function menuRowIcon(menuType: HostMenuType) {
  switch (menuType) {
    case HOST_MENU_TYPES.directory:
      return Folder;
    case HOST_MENU_TYPES.button:
      return Operation;
    default:
      return Document;
  }
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

function normalizeOptionalText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function resolvePath(componentKey: string): string {
  const entry = componentOptions.find(option => option.componentKey === componentKey);
  return entry?.path ?? '/';
}

function resolveEditorPath(): string {
  if (editorForm.menuType === HOST_MENU_TYPES.directory) {
    return editorForm.path.trim();
  }
  return resolvePath(editorForm.componentKey);
}

function resolveEditorComponentKey(): string {
  if (editorForm.menuType === HOST_MENU_TYPES.directory) {
    return 'layout';
  }
  return editorForm.componentKey.trim();
}

function buildCopyRouteName(routeName: string): string {
  const base = normalizeRouteName(routeName.replace(/-copy(?:-\d+)?$/, ''));
  let candidate = `${base}-copy`;
  if (!ROUTE_NAME_PATTERN.test(candidate)) {
    candidate = `${base.slice(0, Math.max(3, 64 - 5))}-copy`;
  }

  let counter = 2;
  let unique = candidate;
  while (realMenus.value.some(menu => menu.routeName === unique)) {
    unique = `${base}-copy-${counter}`;
    counter += 1;
  }
  return unique;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return t('users.fieldEmpty');
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function walkMenuTreeRows(rows: MenuTreeRow[], visitor: (row: MenuTreeRow) => void): void {
  for (const row of rows) {
    visitor(row);
    if (row.children?.length) {
      walkMenuTreeRows(row.children, visitor);
    }
  }
}

function expandAllRows(): void {
  walkMenuTreeRows(menuTree.value, row => {
    treeTableRef.value?.toggleRowExpansion(row, true);
  });
}

function collapseAllRows(): void {
  walkMenuTreeRows(menuTree.value, row => {
    treeTableRef.value?.toggleRowExpansion(row, false);
  });
}

function resetEditorForm(): void {
  editorForm.menuType = HOST_MENU_TYPES.menu;
  editorForm.routeName = '';
  editorForm.path = '';
  editorForm.componentKey = componentOptions[0]?.componentKey ?? 'overview';
  editorForm.title = '';
  editorForm.caption = '';
  editorForm.icon = HOST_MENU_ICON_OPTIONS[0] ?? 'grid';
  editorForm.parentId = null;
  editorForm.displayOrder = 50;
  editorForm.requiredPermission = permissionOptions.value[0]?.code ?? '';
  editorForm.redirect = '';
  editorForm.linkUrl = '';
  editorForm.isHidden = false;
  editorForm.isKeepAlive = false;
  editorForm.isAffix = false;
  editorForm.isEmbedded = false;
  editorForm.remark = '';
}

function fillEditorFromMenu(menu: MenuTreeRow): void {
  editorForm.menuType = menu.menuType;
  editorForm.routeName = menu.routeName;
  editorForm.path = menu.path;
  editorForm.componentKey = menu.componentKey;
  editorForm.title = menu.title;
  editorForm.caption = menu.caption;
  editorForm.icon = menu.icon;
  editorForm.parentId = menu.parentId;
  editorForm.displayOrder = menu.displayOrder;
  editorForm.requiredPermission = menu.requiredPermission;
  editorForm.redirect = menu.redirect ?? '';
  editorForm.linkUrl = menu.linkUrl ?? '';
  editorForm.isHidden = menu.isHidden;
  editorForm.isKeepAlive = menu.isKeepAlive;
  editorForm.isAffix = menu.isAffix;
  editorForm.isEmbedded = menu.isEmbedded;
  editorForm.remark = menu.remark ?? '';
}

function clearFieldErrors(): void {
  fieldErrors.routeName = '';
  fieldErrors.title = '';
  fieldErrors.path = '';
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

function validatePath(): string {
  if (editorForm.menuType !== HOST_MENU_TYPES.directory) {
    return '';
  }
  const path = editorForm.path.trim();
  if (!path || !path.startsWith('/') || path.length > 256) {
    return t('menus.pathPlaceholder');
  }
  return '';
}

function validateComponentKey(): string {
  if (editorForm.menuType !== HOST_MENU_TYPES.menu) {
    return '';
  }
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
  if (editorMode.value === 'edit' && editingMenu.value?.isSystem) {
    return '';
  }
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
  fieldErrors.path = validatePath();
  fieldErrors.componentKey = validateComponentKey();
  fieldErrors.requiredPermission = validateRequiredPermission();
  return !fieldErrors.routeName
    && !fieldErrors.title
    && !fieldErrors.path
    && !fieldErrors.componentKey
    && !fieldErrors.requiredPermission;
}

function onEditorMenuTypeChange(menuType: HostMenuType): void {
  editorForm.menuType = menuType;
  if (menuType === HOST_MENU_TYPES.directory) {
    editorForm.componentKey = 'layout';
    return;
  }
  if (editorForm.componentKey === 'layout') {
    editorForm.componentKey = componentOptions[0]?.componentKey ?? 'overview';
  }
  fieldErrors.path = validatePath();
  fieldErrors.componentKey = validateComponentKey();
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [menus, permissions] = await Promise.all([
      listHostMenusAll(),
      listHostMenuPermissionOptions()
    ]);
    allMenuRows.value = mergeCatalogButtonRows(menus, permissions);
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
    title: params.title ?? '',
    menuType: (params.menuType as AppliedFilters['menuType']) ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
}

function resetSearch(): void {
  appliedFilters.value = { title: '', menuType: '', status: '' };
}

function openCreate(): void {
  editorMode.value = 'create';
  editingMenu.value = null;
  resetEditorForm();
  clearFieldErrors();
  editorOpen.value = true;
}

function resolvePersistedMenu(row: MenuTreeRow): MenuTreeRow {
  return realMenus.value.find(menu => menu.id === row.id) ?? row;
}

function openEdit(menu: MenuTreeRow): void {
  if (changing.value || !canUpdate.value || !isPersistedMenuRow(menu)) {
    return;
  }
  editorMode.value = 'edit';
  editingMenu.value = resolvePersistedMenu(menu);
  fillEditorFromMenu(editingMenu.value);
  clearFieldErrors();
  editorOpen.value = true;
}

function openCopy(menu: MenuTreeRow): void {
  if (changing.value || !canCreate.value || !isPersistedMenuRow(menu)) {
    return;
  }
  editorMode.value = 'create';
  editingMenu.value = null;
  fillEditorFromMenu(resolvePersistedMenu(menu));
  editorForm.routeName = buildCopyRouteName(menu.routeName);
  clearFieldErrors();
  editorOpen.value = true;
  ElMessage.success(t('menus.copySuccess'));
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
      path: resolveEditorPath(),
      componentKey: resolveEditorComponentKey(),
      title,
      caption,
      icon: editorForm.icon,
      displayOrder: editorForm.displayOrder,
      requiredPermission: editorForm.requiredPermission,
      menuType: editorForm.menuType,
      redirect: normalizeOptionalText(editorForm.redirect),
      linkUrl: normalizeOptionalText(editorForm.linkUrl),
      isHidden: editorForm.isHidden,
      isKeepAlive: editorForm.isKeepAlive,
      isAffix: editorForm.isAffix,
      isEmbedded: editorForm.isEmbedded,
      remark: normalizeOptionalText(editorForm.remark)
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
    const menuType = menu.menuType === HOST_MENU_TYPES.button
      ? HOST_MENU_TYPES.button
      : menu.isSystem
        ? editorForm.menuType
        : editorForm.menuType;
    const path = menu.menuType === HOST_MENU_TYPES.button || menu.isSystem
      ? menu.path
      : resolveEditorPath();
    const componentKey = menu.menuType === HOST_MENU_TYPES.button || menu.isSystem
      ? menu.componentKey
      : resolveEditorComponentKey();
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
      version: menu.version,
      menuType,
      redirect: normalizeOptionalText(editorForm.redirect),
      linkUrl: normalizeOptionalText(editorForm.linkUrl),
      isHidden: editorForm.isHidden,
      isKeepAlive: editorForm.isKeepAlive,
      isAffix: editorForm.isAffix,
      isEmbedded: editorForm.isEmbedded,
      remark: normalizeOptionalText(editorForm.remark)
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

async function syncCatalog(): Promise<void> {
  if (changing.value || !canUpdate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const result = await syncHostMenuCatalog();
    ElMessage.success(
      t('menus.syncCatalogSuccess', {
        count: result.created,
        reparented: result.reparented
      })
    );
    await load();
    await session.reloadContext();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function onStatusChange(menu: MenuTreeRow, active: boolean): Promise<void> {
  if (changing.value || !isPersistedMenuRow(menu) || active === menu.isActive) {
    return;
  }
  if (active && !canUpdate.value) {
    return;
  }
  if (!active && !canDisable.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    if (active) {
      await enableHostMenu(menu.id);
      ElMessage.success(t('menus.enableSuccess'));
    } else {
      await ElMessageBox.confirm(
        t('menus.confirmDisable', { name: menu.routeName }),
        t('menus.disable'),
        {
          type: 'warning',
          confirmButtonText: t('menus.disable'),
          cancelButtonText: t('users.cancel')
        }
      );
      await disableHostMenu(menu.id);
      ElMessage.success(t('menus.disableSuccess'));
    }
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

async function disable(menu: MenuTreeRow): Promise<void> {
  if (changing.value || !menu.isActive || !isPersistedMenuRow(menu) || !canDisable.value) {
    return;
  }
  await onStatusChange(menu, false);
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
      :default-visible-count="3"
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
                {{ t('menus.add') }}
              </el-button>
            </PermissionGate>
            <PermissionGate code="identity.menus.update">
              <el-button
                plain
                :disabled="changing"
                data-testid="menus-action-sync-catalog"
                @click="syncCatalog"
              >
                {{ t('menus.syncCatalog') }}
              </el-button>
            </PermissionGate>
            <el-button plain @click="expandAllRows">
              {{ t('menus.expandAll') }}
            </el-button>
            <el-button plain @click="collapseAllRows">
              {{ t('menus.collapseAll') }}
            </el-button>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': menuTree.length === 0 }">
          <el-table
            ref="treeTableRef"
            v-loading="loading"
            :data="menuTree"
            row-key="id"
            :tree-props="{ children: 'children' }"
            default-expand-all
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table menus-tree-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
            data-testid="menus-tree-table"
          >
            <el-table-column :label="t('menus.titleField')" min-width="280" class-name="menus-tree-title-column">
              <template #default="{ row }">
                <div class="menus-name-cell">
                  <el-icon
                    class="menus-name-cell__icon"
                    :class="`menus-name-cell__icon--${row.menuType}`"
                  >
                    <component :is="menuRowIcon(row.menuType)" />
                  </el-icon>
                  <span class="menus-name-cell__title" translate="no">{{ row.title }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('menuType')"
              :label="t('menus.menuType')"
              width="96"
              align="center"
            >
              <template #default="{ row }">
                <el-tag size="small" :type="menuTypeTagType(row.menuType)">
                  {{ t(menuTypeLabelKey(row.menuType) as MessageKey) }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('path')"
              :label="t('menus.path')"
              min-width="180"
              prop="path"
            />

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
              v-if="isColumnVisible('displayOrder')"
              :label="t('users.columnSortOrder')"
              width="88"
              align="center"
              prop="displayOrder"
            />

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="88"
              align="center"
            >
              <template #default="{ row }">
                <template v-if="isPersistedMenuRow(row as MenuTreeRow)">
                  <PermissionGate
                    v-if="row.isActive"
                    code="identity.menus.disable"
                  >
                    <el-switch
                      :model-value="row.isActive"
                      :disabled="changing"
                      @change="(value: string | number | boolean) => onStatusChange(row as MenuTreeRow, Boolean(value))"
                    />
                  </PermissionGate>
                  <PermissionGate
                    v-else
                    code="identity.menus.update"
                  >
                    <el-switch
                      :model-value="row.isActive"
                      :disabled="changing"
                      @change="(value: string | number | boolean) => onStatusChange(row as MenuTreeRow, Boolean(value))"
                    />
                  </PermissionGate>
                </template>
                <el-tag v-else size="small" type="success">
                  {{ t('menus.active') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('modRecord')"
              :label="t('menus.modRecord')"
              width="108"
              align="center"
            >
              <template #default="{ row }">
                <el-popover trigger="hover" :width="280">
                  <template #reference>
                    <el-button link type="primary">{{ t('menus.modRecord') }}</el-button>
                  </template>
                  <div class="menus-mod-record">
                    <strong>{{ t('menus.modRecordTitle') }}</strong>
                    <dl>
                      <dt>{{ t('users.createdAt') }}</dt>
                      <dd translate="no">{{ formatDateTime(row.createdAtUtc) }}</dd>
                      <dt>{{ t('menus.modRecord') }}</dt>
                      <dd translate="no">{{ formatDateTime(row.updatedAtUtc) }}</dd>
                      <dt>{{ t('moduleCatalog.version') }}</dt>
                      <dd translate="no">{{ row.version }}</dd>
                    </dl>
                  </div>
                </el-popover>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="196"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div
                  v-if="isVirtualCatalogButtonRow(row as MenuTreeRow)"
                  class="menus-virtual-actions"
                  :title="t('menus.virtualButtonHint')"
                >
                  {{ t('menus.virtualButtonHint') }}
                </div>
                <div v-else class="menus-row-actions">
                  <PermissionGate code="identity.menus.update">
                    <el-button
                      link
                      type="primary"
                      :disabled="changing"
                      @click="openEdit(row as MenuTreeRow)"
                    >
                      {{ t('menus.edit') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="identity.menus.disable">
                    <el-button
                      link
                      type="danger"
                      :disabled="changing"
                      @click="disable(row as MenuTreeRow)"
                    >
                      {{ t('menus.delete') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="identity.menus.create">
                    <el-button
                      link
                      type="primary"
                      :disabled="changing"
                      @click="openCopy(row as MenuTreeRow)"
                    >
                      {{ t('menus.copy') }}
                    </el-button>
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('menus.emptyDirectory') }}</template>
          </el-table>
        </div>
      </div>
    </el-card>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('menus.createDialogTitle') : t('menus.editDialogTitle')"
      width="880px"
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
        label-width="96px"
        class="menus-editor-form menus-editor-form--cols-2"
      >
        <el-form-item :label="t('menus.menuType')">
          <el-tag
            v-if="editorForm.menuType === HOST_MENU_TYPES.button"
            size="small"
            type="info"
          >
            {{ t('menus.typeButton') }}
          </el-tag>
          <el-radio-group
            v-else
            :model-value="editorForm.menuType"
            @update:model-value="onEditorMenuTypeChange($event as HostMenuType)"
          >
            <el-radio :value="HOST_MENU_TYPES.directory">{{ t('menus.typeDirectory') }}</el-radio>
            <el-radio :value="HOST_MENU_TYPES.menu">{{ t('menus.typeMenu') }}</el-radio>
          </el-radio-group>
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

        <el-form-item
          v-if="editorForm.menuType === HOST_MENU_TYPES.directory"
          :label="t('menus.path')"
          prop="path"
          required
          :error="fieldErrors.path || undefined"
        >
          <el-input
            v-model="editorForm.path"
            :disabled="editorMode === 'edit' && editingMenu?.isSystem === true"
            :placeholder="t('menus.pathPlaceholder')"
            @update:model-value="fieldErrors.path = validatePath()"
          />
        </el-form-item>

        <el-form-item
          v-else-if="editorForm.menuType === HOST_MENU_TYPES.menu"
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

        <el-form-item :label="t('menus.captionField')">
          <el-input
            v-model="editorForm.caption"
            :placeholder="t('menus.captionPlaceholder')"
          />
        </el-form-item>

        <el-form-item :label="t('menus.redirect')">
          <el-input v-model="editorForm.redirect" />
        </el-form-item>

        <el-form-item :label="t('menus.linkUrl')">
          <el-input v-model="editorForm.linkUrl" />
        </el-form-item>

        <el-form-item :label="t('menus.icon')" required>
          <MenuIconPicker v-model="editorForm.icon" />
        </el-form-item>

        <el-form-item :label="t('menus.displayOrder')">
          <el-input
            v-model.number="editorForm.displayOrder"
            type="number"
            :min="0"
            :max="9999"
          />
        </el-form-item>

        <el-form-item :label="t('menus.isHidden')">
          <el-radio-group v-model="editorForm.isHidden">
            <el-radio :value="false">{{ t('menus.visible') }}</el-radio>
            <el-radio :value="true">{{ t('menus.hidden') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="t('menus.isKeepAlive')">
          <el-radio-group v-model="editorForm.isKeepAlive">
            <el-radio :value="true">{{ t('menus.keepAlive') }}</el-radio>
            <el-radio :value="false">{{ t('menus.noKeepAlive') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="t('menus.isAffix')">
          <el-radio-group v-model="editorForm.isAffix">
            <el-radio :value="true">{{ t('menus.affix') }}</el-radio>
            <el-radio :value="false">{{ t('menus.noAffix') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="t('menus.isEmbedded')">
          <el-radio-group v-model="editorForm.isEmbedded">
            <el-radio :value="true">{{ t('menus.embedded') }}</el-radio>
            <el-radio :value="false">{{ t('menus.noEmbedded') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item
          v-if="editorForm.menuType !== HOST_MENU_TYPES.button
            && (editorMode !== 'edit' || !editingMenu?.isSystem)"
          :label="t('menus.requiredPermission')"
          prop="requiredPermission"
          required
          :error="fieldErrors.requiredPermission || undefined"
          class="menus-editor-form__span-2"
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

        <el-form-item :label="t('menus.remark')" class="menus-editor-form__span-2">
          <el-input v-model="editorForm.remark" type="textarea" :rows="3" />
        </el-form-item>

        <p
          v-if="editorMode === 'edit' && editingMenu?.isSystem"
          class="menus-editor-form__hint menus-editor-form__span-2"
        >
          {{
            editingMenu.menuType === HOST_MENU_TYPES.button
              ? t('menus.systemButtonEditableHint')
              : t('menus.systemEditableHint')
          }}
        </p>
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

.menus-editor-form--cols-2 {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 20px;
}

.menus-editor-form--cols-2 :deep(.el-form-item) {
  margin-bottom: 16px;
}

.menus-editor-form--cols-2 :deep(.menus-editor-form__span-2) {
  grid-column: 1 / -1;
}

.menus-editor-form__hint {
  margin: 0 0 12px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
  line-height: 1.5;
}

.menus-tree-table :deep(.menus-tree-title-column .cell) {
  display: flex;
  align-items: center;
}

.menus-name-cell {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 24px;
}

.menus-name-cell__icon {
  flex-shrink: 0;
  font-size: 16px;
}

.menus-name-cell__icon--directory {
  color: var(--el-color-warning);
}

.menus-name-cell__icon--menu {
  color: var(--el-color-primary);
}

.menus-name-cell__icon--button {
  color: var(--el-text-color-secondary);
}

.menus-name-cell__title {
  color: var(--el-text-color-primary);
  line-height: 1.4;
}

.menus-row-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 4px 8px;
}

.menus-mod-record strong {
  display: block;
  margin-bottom: 8px;
}

.menus-mod-record dl {
  margin: 0;
}

.menus-mod-record dt {
  margin-top: 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.menus-mod-record dd {
  margin: 4px 0 0;
}

.menus-virtual-actions {
  max-width: 168px;
  margin: 0 auto;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.4;
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
