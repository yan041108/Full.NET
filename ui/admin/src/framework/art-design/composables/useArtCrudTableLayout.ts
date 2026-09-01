import { computed, nextTick, onActivated, onMounted, onUnmounted, ref, watch, type Ref } from 'vue';
import { useAdminI18n } from '../../../i18n/adminI18n';
import { labelComboboxesIn, scheduleComboboxLabeling } from '../accessibility/labelComboboxes';

export interface ArtCrudTableLayoutOptions {
  /** 表格底部为分页预留的像素高度。 */
  bottomOffset?: number;
}

/** 管理端 CRUD 列表页表格区域高度与表头样式。 */
export function useArtCrudTableLayout(options: ArtCrudTableLayoutOptions = {}) {
  const bottomOffset = options.bottomOffset ?? 68;
  const { t } = useAdminI18n();
  const tableMainRef = ref<HTMLElement | null>(null);
  const tableHeight = ref(360);
  const tableSize = ref<'large' | 'default' | 'small'>('default');
  const tableZebra = ref(true);
  const tableBorder = ref(true);
  const tableHeaderBackground = ref(true);

  const tableHeaderCellStyle = computed(() => ({
    background: tableHeaderBackground.value
      ? 'var(--art-gray-100)'
      : 'var(--art-default-box-color)'
  }));

  /** 为分页下拉补齐无障碍名称，避免 Element Plus 输入框仅靠视觉上下文。 */
  function labelPaginationComboboxes(): void {
    const container = tableMainRef.value;
    if (!container) {
      return;
    }

    labelComboboxesIn(container, { pageSize: t('table.pageSize') });
  }

  /** 按视口高度重算表格区域，保证分页条固定在底部时主体仍可滚动。 */
  function updateTableHeight(): void {
    const container = tableMainRef.value;
    if (!container) {
      return;
    }
    const top = container.getBoundingClientRect().top;
    tableHeight.value = Math.max(240, window.innerHeight - top - bottomOffset);
    void nextTick(labelPaginationComboboxes);
  }

  onMounted(() => {
    updateTableHeight();
    window.addEventListener('resize', updateTableHeight);
    schedulePaginationLabeling();
  });

  onActivated(() => {
    void nextTick(updateTableHeight);
    schedulePaginationLabeling();
  });

  onUnmounted(() => {
    window.removeEventListener('resize', updateTableHeight);
  });

  /** 在异步渲染后重复补标，兼容分页器和下拉框延迟挂载。 */
  function schedulePaginationLabeling(): void {
    const container = tableMainRef.value;
    if (!container) {
      void nextTick(labelPaginationComboboxes);
      return;
    }

    scheduleComboboxLabeling(container, { pageSize: t('table.pageSize') });
  }

  /** 监听列表 loading，在数据切换后重新测量表格高度。 */
  function watchLoading(loading: Ref<boolean>): void {
    watch(loading, () => {
      void nextTick(updateTableHeight);
    });
  }

  return {
    tableMainRef,
    tableHeight,
    tableSize,
    tableZebra,
    tableBorder,
    tableHeaderBackground,
    tableHeaderCellStyle,
    updateTableHeight,
    watchLoading
  };
}

/** 客户端分页切片，分页条固定在表格区域底部时使用。 */
export function useArtClientPagination<T>(filteredItems: Ref<T[]>, initialPageSize = 20) {
  const page = ref(1);
  const pageSize = ref(initialPageSize);
  const total = ref(0);

  const pagedItems = computed(() => {
    const start = (page.value - 1) * pageSize.value;
    return filteredItems.value.slice(start, start + pageSize.value);
  });

  watch(
    filteredItems,
    rows => {
      total.value = rows.length;
      const maxPage = Math.max(1, Math.ceil(rows.length / pageSize.value) || 1);
      if (page.value > maxPage) {
        page.value = maxPage;
      }
    },
    { immediate: true }
  );

  /** 重置到第一页，供筛选条件变化或查询重置后复用。 */
  function resetPage(): void {
    page.value = 1;
  }

  return {
    page,
    pageSize,
    total,
    pagedItems,
    resetPage
  };
}
