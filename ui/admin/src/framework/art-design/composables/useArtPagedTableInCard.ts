import { nextTick, type Ref } from 'vue';
import { useArtCrudTableLayout } from './useArtCrudTableLayout';

/** 卡片内分页表格：表格区域自适应高度，分页固定在底部。 */
export function useArtPagedTableInCard(loading: Ref<boolean>) {
  const layout = useArtCrudTableLayout();
  layout.watchLoading(loading);

  async function syncTableLayout(): Promise<void> {
    await nextTick(layout.updateTableHeight);
  }

  return {
    ...layout,
    syncTableLayout
  };
}
