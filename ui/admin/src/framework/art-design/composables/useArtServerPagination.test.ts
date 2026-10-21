import { describe, expect, it, vi } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import { useArtServerPagination } from './useArtCrudTableLayout';

describe('服务端分页请求预算', () => {
  it('保留当前服务端页，翻页和修改页大小各只发起一次刷新', async () => {
    const scope = effectScope();
    const reload = vi.fn().mockResolvedValue(undefined);
    const rows = ref([21, 22]);
    const paging = scope.run(() => useArtServerPagination(rows, reload))!;
    paging.total.value = 100;
    paging.page.value = 2;
    await nextTick();
    expect(reload).toHaveBeenCalledTimes(1);
    expect(paging.pagedItems.value).toEqual([21, 22]);
    paging.pageSize.value = 50;
    await nextTick();
    await nextTick();
    expect(paging.page.value).toBe(1);
    expect(reload).toHaveBeenCalledTimes(2);
    scope.stop();
  });

  it('筛选重置与总数收缩不会在当前页重复请求', async () => {
    const scope = effectScope();
    const reload = vi.fn().mockResolvedValue(undefined);
    const paging = scope.run(() => useArtServerPagination(ref([1]), reload))!;
    paging.total.value = 100;
    paging.page.value = 4;
    await nextTick();
    reload.mockClear();
    paging.resetPage();
    await nextTick();
    expect(reload).toHaveBeenCalledTimes(1);
    paging.total.value = 1;
    await nextTick();
    expect(reload).toHaveBeenCalledTimes(1);
    paging.resetPage();
    expect(reload).toHaveBeenCalledTimes(2);
    scope.stop();
  });
});
