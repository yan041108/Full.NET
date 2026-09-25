/** Tab 间距，与 art-layout.css 中 `.art-tabs__item` 的 margin-right 一致。 */
export const SHELL_TAB_TAG_SPACING_PX = 6;

/**
 * 将横向 Tab 条滚到当前项（对齐 vue-element-admin ScrollPane.moveToTarget）。
 */
export function moveHorizontalTabScrollToTarget(
  scrollContainer: HTMLElement,
  tabElements: readonly HTMLElement[],
  activeIndex: number
): void {
  if (activeIndex < 0 || tabElements.length === 0) {
    return;
  }

  const containerWidth = scrollContainer.clientWidth;
  const scrollWidth = scrollContainer.scrollWidth;
  if (scrollWidth <= containerWidth) {
    return;
  }

  const currentTag = tabElements[activeIndex];
  const firstTag = tabElements[0];
  const lastTag = tabElements[tabElements.length - 1];

  if (currentTag === firstTag) {
    scrollContainer.scrollLeft = 0;
    return;
  }

  if (currentTag === lastTag) {
    scrollContainer.scrollLeft = scrollWidth - containerWidth;
    return;
  }

  const prevTag = tabElements[activeIndex - 1];
  const nextTag = tabElements[activeIndex + 1];
  const afterNextTagOffsetLeft =
    nextTag.offsetLeft + nextTag.offsetWidth + SHELL_TAB_TAG_SPACING_PX;
  const beforePrevTagOffsetLeft = prevTag.offsetLeft - SHELL_TAB_TAG_SPACING_PX;

  if (afterNextTagOffsetLeft > scrollContainer.scrollLeft + containerWidth) {
    scrollContainer.scrollLeft = afterNextTagOffsetLeft - containerWidth;
  } else if (beforePrevTagOffsetLeft < scrollContainer.scrollLeft) {
    scrollContainer.scrollLeft = beforePrevTagOffsetLeft;
  }
}

/**
 * 在指定滚动容器内以最小位移露出目标元素（避免 scrollIntoView 带动整页）。
 */
export function revealElementInScrollContainer(
  scrollContainer: HTMLElement,
  target: HTMLElement,
  axis: 'vertical' | 'horizontal' = 'vertical'
): void {
  const parentRect = scrollContainer.getBoundingClientRect();
  const targetRect = target.getBoundingClientRect();

  if (axis === 'vertical') {
    if (targetRect.top < parentRect.top) {
      scrollContainer.scrollTop -= parentRect.top - targetRect.top;
    } else if (targetRect.bottom > parentRect.bottom) {
      scrollContainer.scrollTop += targetRect.bottom - parentRect.bottom;
    }
    return;
  }

  if (targetRect.left < parentRect.left) {
    scrollContainer.scrollLeft -= parentRect.left - targetRect.left;
  } else if (targetRect.right > parentRect.right) {
    scrollContainer.scrollLeft += targetRect.right - parentRect.right;
  }
}
