import { nextTick } from 'vue';

/** 库未暴露 header/footer 的语义属性；只校正指定弹窗，保留外层 dialog 名称与焦点行为。 */
export function useArtDialogLandmarks(getContent: () => unknown) {
  return async function scopeLandmarks(): Promise<void> {
    await nextTick();
    const content = getContent();
    if (!(content instanceof HTMLElement)) return;
    for (const child of Array.from(content.children)) {
      if (child.tagName === 'HEADER' || child.tagName === 'FOOTER') child.setAttribute('role', 'presentation');
    }
  };
}
