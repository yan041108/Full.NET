import { onScopeDispose, ref } from 'vue';

/** 由组件作用域拥有预览 URL；关闭、替换和卸载都会使旧下载失效。 */
export function useBlobPreview() {
  const url = ref<string | null>(null);
  let generation = 0;
  let disposed = false;

  function clear(): void {
    generation++;
    if (url.value) URL.revokeObjectURL(url.value);
    url.value = null;
  }

  async function load(download: () => Promise<Blob | null>): Promise<boolean> {
    if (disposed) return false;
    clear();
    const current = generation;
    try {
      const blob = await download();
      if (disposed || current !== generation) return false;
      if (blob) url.value = URL.createObjectURL(blob);
      return true;
    } catch (error) {
      // 旧请求失败不能清理新预览，也不应在已经离开的页面弹出错误。
      if (disposed || current !== generation) return false;
      throw error;
    }
  }

  onScopeDispose(() => {
    disposed = true;
    clear();
  });
  return { url, clear, load };
}
