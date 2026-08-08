<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsDictItem,
  SettingsDictType
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
  createSettingsDictItem,
  createSettingsDictType,
  deleteSettingsDictItem,
  deleteSettingsDictType,
  disableSettingsDictItem,
  disableSettingsDictType,
  listSettingsDictItems,
  listSettingsDictTypes,
  updateSettingsDictItem,
  updateSettingsDictType
} from '../api/dict-types';

defineOptions({ name: 'DictTypesView' });

type TypeEditorMode = 'create' | 'edit';
type ItemEditorMode = 'create' | 'edit';
// 对齐 Admin.NET：固定展示名称、编码、排序、状态、创建时间；描述与颜色放到可选列
type DictTypeTableColumnKey = 'description' | 'createdAt';
type DictItemTableColumnKey = 'color' | 'createdAt';

interface TypeAppliedFilters {
  code: string;
  name: string;
  status: '' | 'active' | 'inactive';
}

interface ItemAppliedFilters {
  label: string;
  value: string;
  status: '' | 'active' | 'inactive';
}

const DICT_TYPE_CODE_PATTERN = /^[a-z][a-z0-9_-]{1,62}[a-z0-9]$/;

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const allDictTypes = ref<SettingsDictType[]>([]);
const allDictItems = ref<SettingsDictItem[]>([]);
const loading = ref(false);
const itemsLoading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const typeSearchForm = ref<Record<string, string | undefined>>({});
const itemSearchForm = ref<Record<string, string | undefined>>({});
const typeAppliedFilters = ref<TypeAppliedFilters>({ code: '', name: '', status: '' });
const itemAppliedFilters = ref<ItemAppliedFilters>({ label: '', value: '', status: '' });
const selectedType = ref<SettingsDictType>();
const typeEditorOpen = ref(false);
const itemEditorOpen = ref(false);
const typeEditorMode = ref<TypeEditorMode>('create');
const itemEditorMode = ref<ItemEditorMode>('create');
const editingDictType = ref<SettingsDictType | null>(null);
const editingDictItem = ref<SettingsDictItem | null>(null);
const typeEditorFormRef = ref<FormInstance>();
const itemEditorFormRef = ref<FormInstance>();
const typeEditorForm = reactive({
  code: '',
  name: '',
  description: '',
  displayOrder: '0'
});
const itemEditorForm = reactive({
  label: '',
  value: '',
  color: '',
  displayOrder: '0'
});
const typeFieldErrors = reactive({
  code: '',
  name: '',
  displayOrder: ''
});
const itemFieldErrors = reactive({
  label: '',
  value: '',
  displayOrder: ''
});
// 默认对齐 Admin.NET：只保留描述为可选列，其余固定列对齐 Admin.NET 展示
const typeColumnVisibility = ref<Record<DictTypeTableColumnKey, boolean>>({
  description: true,
  createdAt: true
});
const itemColumnVisibility = ref<Record<DictItemTableColumnKey, boolean>>({
  color: false,
  createdAt: true
});

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

const {
  tableMainRef: itemsTableMainRef,
  tableHeight: itemsTableHeight,
  tableSize: itemsTableSize,
  tableZebra: itemsTableZebra,
  tableBorder: itemsTableBorder,
  tableHeaderBackground: itemsTableHeaderBackground,
  tableHeaderCellStyle: itemsTableHeaderCellStyle,
  updateTableHeight: updateItemsTableHeight,
  watchLoading: watchItemsLoading
} = useArtCrudTableLayout({ bottomOffset: 68 });

const canCreate = computed(() => session.can('settings.dict_types.create'));
const canUpdate = computed(() => session.can('settings.dict_types.update'));
const canDisable = computed(() => session.can('settings.dict_types.disable'));
// 硬删除仅对已禁用的字典类型/字典项开放，对应 Admin.NET DeleteDict。
const canDelete = computed(() => session.can('settings.dict_types.delete'));

const filteredDictTypes = computed(() => {
  let rows = allDictTypes.value;
  const filters = typeAppliedFilters.value;

  if (filters.code.trim()) {
    const keyword = filters.code.trim().toLowerCase();
    rows = rows.filter(dictType => dictType.code.toLowerCase().includes(keyword));
  }

  if (filters.name.trim()) {
    const keyword = filters.name.trim().toLowerCase();
    rows = rows.filter(dictType => dictType.name.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(dictType => dictType.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(dictType => !dictType.isActive);
  }

  return rows;
});

const filteredDictItems = computed(() => {
  let rows = allDictItems.value;
  const filters = itemAppliedFilters.value;

  if (filters.label.trim()) {
    const keyword = filters.label.trim().toLowerCase();
    rows = rows.filter(item => item.label.toLowerCase().includes(keyword));
  }

  if (filters.value.trim()) {
    const keyword = filters.value.trim().toLowerCase();
    rows = rows.filter(item => item.value.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(item => item.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(item => !item.isActive);
  }

  return rows;
});

const {
  page: typePage,
  pageSize: typePageSize,
  total: typeTotal,
  pagedItems: pagedDictTypes,
  resetPage: resetTypePage
} = useArtClientPagination(filteredDictTypes);

const {
  page: itemPage,
  pageSize: itemPageSize,
  total: itemTotal,
  pagedItems: pagedDictItems,
  resetPage: resetItemPage
} = useArtClientPagination(filteredDictItems);

// 固定列对齐 Admin.NET：序号、名称、编码、排序、状态
const typeTableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'description', label: t('dictTypes.descriptionLabel'), visible: typeColumnVisibility.value.description },
    { key: 'createdAt', label: t('dictTypes.createdAt'), visible: typeColumnVisibility.value.createdAt }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in typeColumnVisibility.value) {
        typeColumnVisibility.value[column.key as DictTypeTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const itemTableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'color', label: t('dictItems.color'), visible: itemColumnVisibility.value.color },
    { key: 'createdAt', label: t('dictItems.createdAt'), visible: itemColumnVisibility.value.createdAt }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in itemColumnVisibility.value) {
        itemColumnVisibility.value[column.key as DictItemTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const typeSearchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'code',
    label: t('dictTypes.code'),
    placeholder: t('dictTypes.searchCodePlaceholder')
  },
  {
    key: 'name',
    label: t('dictTypes.name'),
    placeholder: t('dictTypes.searchNamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('dictTypes.searchStatusPlaceholder'),
    options: [
      { label: t('dictTypes.active'), value: 'active' },
      { label: t('dictTypes.inactive'), value: 'inactive' }
    ]
  }
]);

const itemSearchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'label',
    label: t('dictItems.label'),
    placeholder: t('dictItems.searchLabelPlaceholder')
  },
  {
    key: 'value',
    label: t('dictItems.value'),
    placeholder: t('dictItems.searchValuePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('dictItems.searchStatusPlaceholder'),
    options: [
      { label: t('dictItems.active'), value: 'active' },
      { label: t('dictItems.inactive'), value: 'inactive' }
    ]
  }
]);

watchLoading(loading);
watchItemsLoading(itemsLoading);

watch(selectedType, () => {
  void nextTick(updateItemsTableHeight);
});

onMounted(() => {
  void load();
});

function isTypeColumnVisible(key: DictTypeTableColumnKey): boolean {
  return typeColumnVisibility.value[key];
}

function isItemColumnVisible(key: DictItemTableColumnKey): boolean {
  return itemColumnVisibility.value[key];
}

function typeRowIndex(index: number): number {
  return (typePage.value - 1) * typePageSize.value + index + 1;
}

function itemRowIndex(index: number): number {
  return (itemPage.value - 1) * itemPageSize.value + index + 1;
}

function normalizeDictTypeCode(value: string): string {
  return value.trim().toLowerCase().replace(/\s+/g, '_');
}

function normalizeDictItemValue(value: string): string {
  return value.trim().toLowerCase();
}

function clearTypeFieldErrors(): void {
  typeFieldErrors.code = '';
  typeFieldErrors.name = '';
  typeFieldErrors.displayOrder = '';
}

function clearItemFieldErrors(): void {
  itemFieldErrors.label = '';
  itemFieldErrors.value = '';
  itemFieldErrors.displayOrder = '';
}

function validateTypeCode(): string {
  if (typeEditorMode.value !== 'create') {
    return '';
  }
  const code = normalizeDictTypeCode(typeEditorForm.code);
  if (!code) {
    return t('dictTypes.codeRequired');
  }
  if (!DICT_TYPE_CODE_PATTERN.test(code)) {
    return t('dictTypes.codeInvalid');
  }
  return '';
}

function validateTypeName(): string {
  const name = typeEditorForm.name.trim();
  if (!name) {
    return t('dictTypes.nameRequired');
  }
  if (name.length > 128) {
    return t('dictTypes.nameInvalid');
  }
  return '';
}

function validateTypeDisplayOrder(): string {
  const order = Number.parseInt(typeEditorForm.displayOrder, 10);
  if (Number.isNaN(order)) {
    return t('dictTypes.displayOrderInvalid');
  }
  return '';
}

function validateItemLabel(): string {
  const label = itemEditorForm.label.trim();
  if (!label) {
    return t('dictItems.labelRequired');
  }
  if (label.length > 128) {
    return t('dictItems.labelInvalid');
  }
  return '';
}

function validateItemValue(): string {
  if (itemEditorMode.value !== 'create') {
    return '';
  }
  const value = normalizeDictItemValue(itemEditorForm.value);
  if (!value) {
    return t('dictItems.valueRequired');
  }
  return '';
}

function validateItemDisplayOrder(): string {
  const order = Number.parseInt(itemEditorForm.displayOrder, 10);
  if (Number.isNaN(order)) {
    return t('dictItems.displayOrderInvalid');
  }
  return '';
}

function applyTypeFieldErrors(): boolean {
  typeFieldErrors.code = validateTypeCode();
  typeFieldErrors.name = validateTypeName();
  typeFieldErrors.displayOrder = validateTypeDisplayOrder();
  return !typeFieldErrors.code && !typeFieldErrors.name && !typeFieldErrors.displayOrder;
}

function applyItemFieldErrors(): boolean {
  itemFieldErrors.label = validateItemLabel();
  itemFieldErrors.value = validateItemValue();
  itemFieldErrors.displayOrder = validateItemDisplayOrder();
  return !itemFieldErrors.label && !itemFieldErrors.value && !itemFieldErrors.displayOrder;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

// 把 Color 字段（如 #409eff）映射到 Element Plus Tag type，用于 Admin.NET 风格的彩色标签
const COLOR_TO_TAG_TYPE: Record<string, 'primary' | 'success' | 'warning' | 'danger' | 'info'> = {
  '#409eff': 'primary',
  '#67c23a': 'success',
  '#e6a23c': 'warning',
  '#f56c6c': 'danger',
  '#909399': 'info',
  primary: 'primary',
  success: 'success',
  warning: 'warning',
  danger: 'danger',
  info: 'info'
};

function tagTypeFromColor(color: string | null | undefined): 'primary' | 'success' | 'warning' | 'danger' | 'info' {
  if (!color) {
    return 'info';
  }
  const normalized = color.trim().toLowerCase();
  return COLOR_TO_TAG_TYPE[normalized] ?? 'info';
}

function tagStyleFromColor(color: string | null | undefined): Record<string, string> | undefined {
  if (!color) {
    return undefined;
  }
  const normalized = color.trim().toLowerCase();
  if (normalized.startsWith('#')) {
    // 自定义颜色：通过内联样式把 Tag 背景/文字变成用户指定颜色，实现 Admin.NET 风格的彩色标签
    return {
      backgroundColor: normalized,
      borderColor: normalized,
      color: '#ffffff'
    };
  }
  return undefined;
}

async function fetchAllDictTypes(): Promise<SettingsDictType[]> {
  const pageLimit = 100;
  const firstPage = await listSettingsDictTypes(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listSettingsDictTypes(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function fetchAllDictItems(dictTypeId: string): Promise<SettingsDictItem[]> {
  const pageLimit = 100;
  const firstPage = await listSettingsDictItems(dictTypeId, 1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listSettingsDictItems(dictTypeId, current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allDictTypes.value = await fetchAllDictTypes();
    if (selectedType.value) {
      const refreshed = allDictTypes.value.find(item => item.id === selectedType.value!.id);
      selectedType.value = refreshed;
      if (refreshed) {
        await loadItems(refreshed.id);
      } else {
        closeItems();
      }
    }
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictTypes.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadItems(dictTypeId: string): Promise<void> {
  itemsLoading.value = true;
  try {
    allDictItems.value = await fetchAllDictItems(dictTypeId);
    await nextTick(updateItemsTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictItems.loadFailed');
  } finally {
    itemsLoading.value = false;
  }
}

function handleTypeSearch(params: Record<string, string | undefined>): void {
  typeAppliedFilters.value = {
    code: params.code ?? '',
    name: params.name ?? '',
    status: (params.status as TypeAppliedFilters['status']) ?? ''
  };
  resetTypePage();
}

function resetTypeSearch(): void {
  typeAppliedFilters.value = { code: '', name: '', status: '' };
  resetTypePage();
}

function handleItemSearch(params: Record<string, string | undefined>): void {
  itemAppliedFilters.value = {
    label: params.label ?? '',
    value: params.value ?? '',
    status: (params.status as ItemAppliedFilters['status']) ?? ''
  };
  resetItemPage();
}

function resetItemSearch(): void {
  itemAppliedFilters.value = { label: '', value: '', status: '' };
  resetItemPage();
}

async function openItems(dictType: SettingsDictType): Promise<void> {
  selectedType.value = dictType;
  problem.value = undefined;
  itemAppliedFilters.value = { label: '', value: '', status: '' };
  resetItemPage();
  await loadItems(dictType.id);
}

function closeItems(): void {
  selectedType.value = undefined;
  allDictItems.value = [];
}

function openTypeCreate(): void {
  typeEditorMode.value = 'create';
  editingDictType.value = null;
  typeEditorForm.code = '';
  typeEditorForm.name = '';
  typeEditorForm.description = '';
  typeEditorForm.displayOrder = '0';
  clearTypeFieldErrors();
  typeEditorOpen.value = true;
}

function openTypeEdit(dictType: SettingsDictType): void {
  if (changing.value || !dictType.isActive) {
    return;
  }
  typeEditorMode.value = 'edit';
  editingDictType.value = dictType;
  typeEditorForm.code = dictType.code;
  typeEditorForm.name = dictType.name;
  typeEditorForm.description = dictType.description ?? '';
  typeEditorForm.displayOrder = String(dictType.displayOrder);
  clearTypeFieldErrors();
  typeEditorOpen.value = true;
}

function onTypeCodeBlur(): void {
  if (typeEditorMode.value !== 'create') {
    return;
  }
  typeEditorForm.code = normalizeDictTypeCode(typeEditorForm.code);
  typeFieldErrors.code = validateTypeCode();
}

async function submitTypeEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (typeEditorMode.value === 'create') {
    typeEditorForm.code = normalizeDictTypeCode(typeEditorForm.code);
  }
  typeEditorForm.name = typeEditorForm.name.trim();
  if (!applyTypeFieldErrors()) {
    return;
  }
  if (typeEditorMode.value === 'create') {
    await createDictType();
    return;
  }
  await saveTypeEdit();
}

async function createDictType(): Promise<void> {
  if (!canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createSettingsDictType(
      typeEditorForm.code,
      typeEditorForm.name,
      typeEditorForm.description.trim() || null,
      Number.parseInt(typeEditorForm.displayOrder, 10) || 0
    );
    typeEditorOpen.value = false;
    ElMessage.success(t('dictTypes.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveTypeEdit(): Promise<void> {
  const dictType = editingDictType.value;
  if (!canUpdate.value || !dictType) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateSettingsDictType(
      dictType.id,
      typeEditorForm.name,
      typeEditorForm.description.trim() || null,
      Number.parseInt(typeEditorForm.displayOrder, 10) || 0,
      dictType.version
    );
    typeEditorOpen.value = false;
    ElMessage.success(t('dictTypes.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disableDictType(dictType: SettingsDictType): Promise<void> {
  if (changing.value || !dictType.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('dictTypes.confirmDisable', { name: dictType.code }),
      t('dictTypes.disable'),
      {
        type: 'warning',
        confirmButtonText: t('dictTypes.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableSettingsDictType(dictType.id);
    ElMessage.success(t('dictTypes.disableSuccess'));
    if (selectedType.value?.id === dictType.id) {
      closeItems();
    }
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

// 硬删除已禁用的字典类型，二次确认后调用删除接口；删除成功后若当前选中类型被删则关闭字典项面板。
async function deleteDictType(dictType: SettingsDictType): Promise<void> {
  if (changing.value || dictType.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('dictTypes.confirmDelete', { name: dictType.code }),
      t('dictTypes.delete'),
      {
        type: 'warning',
        confirmButtonText: t('dictTypes.delete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await deleteSettingsDictType(dictType.id, dictType.version);
    ElMessage.success(t('dictTypes.deleteSuccess'));
    if (selectedType.value?.id === dictType.id) {
      closeItems();
    }
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

function openItemCreate(): void {
  if (!selectedType.value) {
    return;
  }
  itemEditorMode.value = 'create';
  editingDictItem.value = null;
  itemEditorForm.label = '';
  itemEditorForm.value = '';
  itemEditorForm.color = '';
  itemEditorForm.displayOrder = '0';
  clearItemFieldErrors();
  itemEditorOpen.value = true;
}

function openItemEdit(item: SettingsDictItem): void {
  if (changing.value || !item.isActive) {
    return;
  }
  itemEditorMode.value = 'edit';
  editingDictItem.value = item;
  itemEditorForm.label = item.label;
  itemEditorForm.value = item.value;
  itemEditorForm.color = item.color ?? '';
  itemEditorForm.displayOrder = String(item.displayOrder);
  clearItemFieldErrors();
  itemEditorOpen.value = true;
}

async function submitItemEditor(): Promise<void> {
  if (changing.value || !selectedType.value) {
    return;
  }
  if (itemEditorMode.value === 'create') {
    itemEditorForm.value = normalizeDictItemValue(itemEditorForm.value);
  }
  itemEditorForm.label = itemEditorForm.label.trim();
  if (!applyItemFieldErrors()) {
    return;
  }
  if (itemEditorMode.value === 'create') {
    await createDictItem();
    return;
  }
  await saveItemEdit();
}

async function createDictItem(): Promise<void> {
  const dictType = selectedType.value;
  if (!canCreate.value || !dictType) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createSettingsDictItem(
      dictType.id,
      itemEditorForm.label,
      itemEditorForm.value,
      itemEditorForm.color.trim() || null,
      Number.parseInt(itemEditorForm.displayOrder, 10) || 0
    );
    itemEditorOpen.value = false;
    ElMessage.success(t('dictItems.createSuccess'));
    await loadItems(dictType.id);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveItemEdit(): Promise<void> {
  const item = editingDictItem.value;
  const dictType = selectedType.value;
  if (!canUpdate.value || !item || !dictType) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateSettingsDictItem(
      item.id,
      itemEditorForm.label,
      itemEditorForm.color.trim() || null,
      Number.parseInt(itemEditorForm.displayOrder, 10) || 0,
      item.version
    );
    itemEditorOpen.value = false;
    ElMessage.success(t('dictItems.updateSuccess'));
    await loadItems(dictType.id);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disableDictItem(item: SettingsDictItem): Promise<void> {
  const dictType = selectedType.value;
  if (changing.value || !item.isActive || !dictType) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('dictItems.confirmDisable', { name: item.value }),
      t('dictItems.disable'),
      {
        type: 'warning',
        confirmButtonText: t('dictItems.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableSettingsDictItem(item.id);
    ElMessage.success(t('dictItems.disableSuccess'));
    await loadItems(dictType.id);
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

// 硬删除已禁用的字典项，二次确认后调用删除接口并刷新当前字典项列表。
async function deleteDictItem(item: SettingsDictItem): Promise<void> {
  const dictType = selectedType.value;
  if (changing.value || item.isActive || !dictType) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('dictItems.confirmDelete', { name: item.value }),
      t('dictItems.delete'),
      {
        type: 'warning',
        confirmButtonText: t('dictItems.delete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await deleteSettingsDictItem(item.id, item.version);
    ElMessage.success(t('dictItems.deleteSuccess'));
    await loadItems(dictType.id);
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey:
    | 'dictTypes.loadFailed'
    | 'dictTypes.operationFailed'
    | 'dictItems.loadFailed'
    | 'dictItems.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_dict_type_failed',
        title: t(fallbackKey)
      };
}
</script>

<template>
  <section class="dict-types-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('dictTypes.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div class="dict-types-view__split">
      <!-- 左：字典类型 -->
      <el-card class="art-table-card dict-types-view__pane" shadow="never">
        <ArtSearchBar
          v-model="typeSearchForm"
          :items="typeSearchItems"
          :default-visible-count="3"
          :search-label="t('dictTypes.query')"
          :reset-label="t('dictTypes.reset')"
          :expand-label="t('dictTypes.expand')"
          :collapse-label="t('dictTypes.collapse')"
          @search="handleTypeSearch"
          @reset="resetTypeSearch"
        />

        <div ref="tableMainRef" class="art-crud-table-main">
          <ArtTableHeader
            v-model:columns="typeTableColumns"
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
              <PermissionGate code="settings.dict_types.create">
                <el-button
                  type="primary"
                  plain
                  :icon="Plus"
                  data-testid="dict-types-action-create"
                  @click="openTypeCreate"
                >
                  {{ t('dictTypes.addDictType') }}
                </el-button>
              </PermissionGate>
            </template>
          </ArtTableHeader>

          <div class="art-table" :class="{ 'is-empty': pagedDictTypes.length === 0 }">
            <el-table
              v-loading="loading"
              :data="pagedDictTypes"
              :height="tableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
              class="art-crud-data-table"
              :class="{ 'art-table--header-bg': tableHeaderBackground }"
              highlight-current-row
              @row-click="openItems($event as SettingsDictType)"
            >
              <el-table-column :label="t('users.columnIndex')" width="64" align="center" fixed="left">
                <template #default="{ $index }">{{ typeRowIndex($index) }}</template>
              </el-table-column>

              <el-table-column
                :label="t('dictTypes.name')"
                min-width="160"
                align="left"
                header-align="center"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  <div class="art-crud-table-row">
                    <span class="art-crud-table-row__avatar">{{ row.code.slice(0, 2).toUpperCase() }}</span>
                    <div>
                      <div class="art-crud-table-row__name" translate="no">{{ row.name }}</div>
                      <div class="art-crud-table-row__sub" translate="no">{{ row.code }}</div>
                    </div>
                  </div>
                </template>
              </el-table-column>

              <el-table-column
                :label="t('dictTypes.displayOrder')"
                width="90"
                align="center"
                header-align="center"
                prop="displayOrder"
              />

              <el-table-column
                :label="t('users.status')"
                width="96"
                align="center"
                header-align="center"
              >
                <template #default="{ row }">
                  <el-tag :type="row.isActive ? 'success' : 'info'" effect="light">
                    {{ t(row.isActive ? 'dictTypes.active' : 'dictTypes.inactive') }}
                  </el-tag>
                </template>
              </el-table-column>

              <el-table-column
                v-if="isTypeColumnVisible('createdAt')"
                :label="t('dictTypes.createdAt')"
                min-width="150"
                align="center"
                header-align="center"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  <span translate="no">{{ formatDateTime(row.createdAtUtc) }}</span>
                </template>
              </el-table-column>

              <el-table-column
                v-if="isTypeColumnVisible('description')"
                :label="t('dictTypes.descriptionLabel')"
                min-width="180"
                align="left"
                header-align="center"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  <span translate="no">{{ row.description ?? '—' }}</span>
                </template>
              </el-table-column>

              <el-table-column
                :label="t('users.columnActions')"
                width="170"
                fixed="right"
                align="center"
              >
                <template #default="{ row }">
                  <div class="art-crud-table-actions">
                    <ArtTableActionButton
                      type="view"
                      test-id="dict-types-action-manage-items"
                      :title="t('dictItems.manage')"
                      :disabled="changing || itemsLoading"
                      @click="openItems(row as SettingsDictType)"
                    />
                    <PermissionGate v-if="canUpdate" code="settings.dict_types.update">
                      <ArtTableActionButton
                        type="edit"
                        test-id="dict-types-action-edit"
                        :title="t('dictTypes.edit')"
                        :disabled="changing || !row.isActive"
                        @click="openTypeEdit(row as SettingsDictType)"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="row.isActive && canDisable" code="settings.dict_types.disable">
                      <ArtTableActionButton
                        type="delete"
                        test-id="dict-types-action-disable"
                        :title="t('dictTypes.disable')"
                        :disabled="changing"
                        @click="disableDictType(row as SettingsDictType)"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="!row.isActive && canDelete" code="settings.dict_types.delete">
                      <ArtTableActionButton
                        type="delete"
                        test-id="dict-types-action-delete"
                        :title="t('dictTypes.delete')"
                        :disabled="changing"
                        @click="deleteDictType(row as SettingsDictType)"
                      />
                    </PermissionGate>
                  </div>
                </template>
              </el-table-column>

              <template #empty>{{ t('dictTypes.emptyDirectory') }}</template>
            </el-table>

            <div class="art-table__pagination center custom-pagination">
              <el-pagination
                v-model:current-page="typePage"
                v-model:page-size="typePageSize"
                :total="typeTotal"
                background
                layout="total, sizes, prev, pager, next, jumper"
                :page-sizes="[10, 20, 50, 100]"
              />
            </div>
          </div>
        </div>
      </el-card>

      <!-- 右：字典项 -->
      <el-card
        class="art-table-card dict-types-view__pane"
        shadow="never"
        :aria-busy="itemsLoading"
      >
        <template #header>
          <div class="dict-types-view__pane-header">
            <span>{{ selectedType ? t('dictItems.panelTitle', { name: selectedType.code }) : t('dictItems.manage') }}</span>
            <el-button
              v-if="selectedType"
              plain
              size="small"
              data-testid="dict-items-action-close"
              @click="closeItems"
            >
              {{ t('dictItems.close') }}
            </el-button>
          </div>
        </template>

        <ArtSearchBar
          v-model="itemSearchForm"
          :items="itemSearchItems"
          :default-visible-count="3"
          :search-label="t('dictItems.query')"
          :reset-label="t('dictItems.reset')"
          :expand-label="t('dictItems.expand')"
          :collapse-label="t('dictItems.collapse')"
          @search="handleItemSearch"
          @reset="resetItemSearch"
        />

        <div ref="itemsTableMainRef" class="art-crud-table-main">
          <ArtTableHeader
            v-model:columns="itemTableColumns"
            v-model:table-size="itemsTableSize"
            v-model:zebra="itemsTableZebra"
            v-model:border="itemsTableBorder"
            v-model:header-background="itemsTableHeaderBackground"
            :loading="itemsLoading"
            full-class="art-crud-table-main"
            layout="refresh,size,columns,settings"
            @refresh="selectedType && loadItems(selectedType.id)"
          >
            <template #left>
              <PermissionGate code="settings.dict_types.create">
                <el-button
                  type="primary"
                  plain
                  :icon="Plus"
                  data-testid="dict-items-action-create"
                  @click="openItemCreate"
                  :disabled="!selectedType"
                >
                  {{ t('dictItems.addItem') }}
                </el-button>
              </PermissionGate>
            </template>
          </ArtTableHeader>

          <div
            class="art-table"
            :class="{ 'is-empty': !selectedType || pagedDictItems.length === 0 }"
            data-dict-items-directory
          >
            <el-table
              v-loading="itemsLoading"
              :data="selectedType ? pagedDictItems : []"
              :height="itemsTableHeight"
              :size="itemsTableSize"
              :stripe="itemsTableZebra"
              :border="itemsTableBorder"
              :header-cell-style="itemsTableHeaderCellStyle"
              class="art-crud-data-table"
              :class="{ 'art-table--header-bg': itemsTableHeaderBackground }"
            >
              <el-table-column :label="t('users.columnIndex')" width="64" align="center" fixed="left">
                <template #default="{ $index }">{{ itemRowIndex($index) }}</template>
              </el-table-column>

              <el-table-column
                :label="t('dictItems.label')"
                min-width="180"
                align="left"
                header-align="center"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  <el-tag
                    :type="tagTypeFromColor(row.color)"
                    :style="tagStyleFromColor(row.color)"
                    effect="dark"
                    class="dict-items__label-tag"
                  >
                    {{ row.label }}
                  </el-tag>
                </template>
              </el-table-column>

              <el-table-column
                :label="t('dictItems.value')"
                min-width="140"
                align="left"
                header-align="center"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  <span translate="no">{{ row.value }}</span>
                </template>
              </el-table-column>

              <el-table-column
                :label="t('dictItems.displayOrder')"
                width="90"
                align="center"
                header-align="center"
                prop="displayOrder"
              />

              <el-table-column
                :label="t('users.status')"
                width="96"
                align="center"
                header-align="center"
              >
                <template #default="{ row }">
                  <el-tag :type="row.isActive ? 'success' : 'info'" effect="light">
                    {{ t(row.isActive ? 'dictItems.active' : 'dictItems.inactive') }}
                  </el-tag>
                </template>
              </el-table-column>

              <el-table-column
                v-if="isItemColumnVisible('createdAt')"
                :label="t('dictItems.createdAt')"
                min-width="150"
                align="center"
                header-align="center"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  <span translate="no">{{ formatDateTime(row.createdAtUtc) }}</span>
                </template>
              </el-table-column>

              <el-table-column
                v-if="isItemColumnVisible('color')"
                :label="t('dictItems.color')"
                width="110"
                align="center"
                header-align="center"
              >
                <template #default="{ row }">
                  <span translate="no">{{ row.color ?? t('dictItems.emptyColor') }}</span>
                </template>
              </el-table-column>

              <el-table-column
                :label="t('users.columnActions')"
                width="160"
                fixed="right"
                align="center"
              >
                <template #default="{ row }">
                  <div class="art-crud-table-actions">
                    <PermissionGate v-if="canUpdate" code="settings.dict_types.update">
                      <ArtTableActionButton
                        type="edit"
                        test-id="dict-items-action-edit"
                        :title="t('dictItems.edit')"
                        :disabled="changing || !row.isActive"
                        @click="openItemEdit(row as SettingsDictItem)"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="row.isActive && canDisable" code="settings.dict_types.disable">
                      <ArtTableActionButton
                        type="delete"
                        test-id="dict-items-action-disable"
                        :title="t('dictItems.disable')"
                        :disabled="changing"
                        @click="disableDictItem(row as SettingsDictItem)"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="!row.isActive && canDelete" code="settings.dict_types.delete">
                      <ArtTableActionButton
                        type="delete"
                        test-id="dict-items-action-delete"
                        :title="t('dictItems.delete')"
                        :disabled="changing"
                        @click="deleteDictItem(row as SettingsDictItem)"
                      />
                    </PermissionGate>
                  </div>
                </template>
              </el-table-column>

              <template #empty>
                <span data-dict-items-empty>
                  {{ selectedType ? t('dictItems.emptyDirectory') : t('dictItems.selectType') }}
                </span>
              </template>
            </el-table>

            <div class="art-table__pagination center custom-pagination">
              <el-pagination
                v-model:current-page="itemPage"
                v-model:page-size="itemPageSize"
                :total="itemTotal"
                background
                layout="total, sizes, prev, pager, next, jumper"
                :page-sizes="[10, 20, 50, 100]"
              />
            </div>
          </div>
        </div>
      </el-card>
    </div>

    <ArtFormDialog
      v-model:open="typeEditorOpen"
      :title="typeEditorMode === 'create' ? t('dictTypes.createDialogTitle') : t('dictTypes.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="dict-types-editor-submit"
      :show-confirm="typeEditorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitTypeEditor"
    >
      <el-form
        ref="typeEditorFormRef"
        data-testid="dict-types-editor-form"
        :model="typeEditorForm"
        label-width="96px"
        class="dict-types-editor-form"
      >
        <el-form-item
          v-if="typeEditorMode === 'create'"
          :label="t('dictTypes.code')"
          prop="code"
          required
          :error="typeFieldErrors.code || undefined"
        >
          <el-input
            v-model="typeEditorForm.code"
            :placeholder="t('dictTypes.codePlaceholder')"
            @blur="onTypeCodeBlur"
            @update:model-value="typeFieldErrors.code = validateTypeCode()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('dictTypes.code')">
          <el-input v-model="typeEditorForm.code" disabled />
        </el-form-item>
        <el-form-item
          :label="t('dictTypes.name')"
          prop="name"
          required
          :error="typeFieldErrors.name || undefined"
        >
          <el-input
            v-model="typeEditorForm.name"
            :placeholder="t('dictTypes.namePlaceholder')"
            @update:model-value="typeFieldErrors.name = validateTypeName()"
          />
        </el-form-item>
        <!-- 对齐 Admin.NET：创建/编辑均显示 说明 字段 -->
        <el-form-item :label="t('dictTypes.descriptionLabel')">
          <el-input
            v-model="typeEditorForm.description"
            :placeholder="t('dictTypes.descriptionPlaceholder')"
            type="textarea"
            :rows="3"
          />
        </el-form-item>
        <!-- 对齐 Admin.NET：创建/编辑均显示 排序 字段 -->
        <el-form-item
          :label="t('dictTypes.displayOrder')"
          prop="displayOrder"
          :error="typeFieldErrors.displayOrder || undefined"
        >
          <el-input
            v-model="typeEditorForm.displayOrder"
            type="number"
            @update:model-value="typeFieldErrors.displayOrder = validateTypeDisplayOrder()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="itemEditorOpen"
      :title="itemEditorMode === 'create' ? t('dictItems.createDialogTitle') : t('dictItems.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="dict-items-editor-submit"
      :show-confirm="itemEditorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitItemEditor"
    >
      <el-form
        ref="itemEditorFormRef"
        data-testid="dict-items-editor-form"
        :model="itemEditorForm"
        label-width="96px"
        class="dict-items-editor-form"
        data-dict-items-create-form
      >
        <el-form-item
          :label="t('dictItems.label')"
          prop="label"
          required
          :error="itemFieldErrors.label || undefined"
        >
          <el-input
            v-model="itemEditorForm.label"
            :placeholder="t('dictItems.labelPlaceholder')"
            @update:model-value="itemFieldErrors.label = validateItemLabel()"
          />
        </el-form-item>
        <el-form-item
          v-if="itemEditorMode === 'create'"
          :label="t('dictItems.value')"
          prop="value"
          required
          :error="itemFieldErrors.value || undefined"
        >
          <el-input
            v-model="itemEditorForm.value"
            :placeholder="t('dictItems.valuePlaceholder')"
            @update:model-value="itemFieldErrors.value = validateItemValue()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('dictItems.value')">
          <el-input v-model="itemEditorForm.value" disabled />
        </el-form-item>
        <!-- 对齐 Admin.NET：颜色作为标签类型，创建/编辑均可修改 -->
        <el-form-item :label="t('dictItems.color')">
          <el-input
            v-model="itemEditorForm.color"
            :placeholder="t('dictItems.colorPlaceholder')"
          />
        </el-form-item>
        <el-form-item
          :label="t('dictItems.displayOrder')"
          prop="displayOrder"
          :error="itemFieldErrors.displayOrder || undefined"
        >
          <el-input
            v-model="itemEditorForm.displayOrder"
            type="number"
            @update:model-value="itemFieldErrors.displayOrder = validateItemDisplayOrder()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.dict-types-view {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  min-height: 0;
}

.dict-types-view__split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 12px;
  flex: 1;
  min-height: 0;
}

.dict-types-view__pane {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.dict-types-view__pane :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.dict-types-view__pane-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.dict-types-view__pane-header :deep(span) {
  font-weight: 600;
  font-size: 14px;
}

.dict-items__label-tag {
  font-weight: 500;
}

.dict-types-editor-form,
.dict-items-editor-form {
  padding-top: 8px;
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

/* 窄屏回退为上下布局，保证移动端可读性 */
@media (max-width: 1100px) {
  .dict-types-view__split {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
