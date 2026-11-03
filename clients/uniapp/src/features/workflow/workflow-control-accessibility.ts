import { onBeforeUnmount, onMounted, onUpdated, type Ref } from 'vue';

/** H5 的 uni 控件使用自定义宿主；把名称交给真实输入，并补齐按钮的键盘语义。 */
export function useWorkflowControlAccessibility(reference: Ref<unknown>): void {
  let root: HTMLElement | undefined;
  let observer: MutationObserver | undefined;
  function refresh(): void {
    let element = reference.value;
    if (typeof element === 'object' && element !== null && '$el' in element) element = element.$el;
    const current = element instanceof HTMLElement ? element : undefined;
    if (current !== root) {
      observer?.disconnect(); observer = undefined;
      root?.removeEventListener('keydown', keyboard); root = current;
      if (root) {
        root.addEventListener('keydown', keyboard);
        // uni 页面不会为后续异步内容触发普通 updated；仅观察结构/禁用变化，避免自身语义属性造成循环。
        observer = new MutationObserver(refresh);
        observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
      }
    }
    if (!root) return;
    for (const field of root.querySelectorAll<HTMLElement>('[data-field-key]')) {
      const name = field.getAttribute('data-field-key');
      if (!name) continue;
      for (const control of field.querySelectorAll<HTMLElement>('input, textarea, select')) {
        // 明细父字段不能覆盖其行内列名，也不覆盖已有组件提供的可访问名称。
        if (control.closest('[data-field-key]') !== field) continue;
        if (!control.getAttribute('aria-label')) control.setAttribute('aria-label', name);
        const host = control.closest('uni-input, uni-textarea');
        if (host?.getAttribute('aria-label') === name) host.removeAttribute('aria-label');
      }
    }
    for (const button of root.querySelectorAll<HTMLElement>('uni-button')) {
      const disabled = button.hasAttribute('disabled') && button.getAttribute('disabled') !== 'false';
      if (button.getAttribute('role') !== 'button') button.setAttribute('role', 'button');
      if (button.getAttribute('aria-disabled') !== String(disabled)) button.setAttribute('aria-disabled', String(disabled));
      if (button.tabIndex !== (disabled ? -1 : 0)) button.tabIndex = disabled ? -1 : 0;
    }
  }
  function keyboard(event: KeyboardEvent): void {
    if (!(event.target instanceof HTMLElement) || !['Enter', ' '].includes(event.key)) return;
    const button = event.target.closest<HTMLElement>('uni-button');
    if (!button || !root?.contains(button)) return;
    event.preventDefault(); event.stopPropagation();
    if (!button.hasAttribute('disabled') || button.getAttribute('disabled') === 'false') button.click();
  }
  onMounted(refresh);
  onUpdated(refresh);
  onBeforeUnmount(() => { observer?.disconnect(); root?.removeEventListener('keydown', keyboard); root = undefined; });
}
