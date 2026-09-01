export interface ComboboxLabelOptions {
  pageSize: string;
  tenantSelector?: string;
}

/** 为 Element Plus 分页与壳层租户选择器补齐 combobox 可访问名称。 */
export function labelComboboxesIn(
  root: ParentNode,
  labels: ComboboxLabelOptions
): void {
  /** 仅在缺少无障碍名称时补写，避免覆盖业务侧显式声明的 aria 语义。 */
  root.querySelectorAll('.el-pagination .el-select__input').forEach(input => {
    if (!input.getAttribute('aria-label') && !input.getAttribute('aria-labelledby')) {
      input.setAttribute('aria-label', labels.pageSize);
    }
  });

  if (labels.tenantSelector) {
    /** 租户选择器与分页器共用 Element Plus Select，需要按测试标识精确限定作用域。 */
    root.querySelectorAll('[data-testid="shell-tenant-select"] .el-select__input').forEach(input => {
      if (!input.getAttribute('aria-label') && !input.getAttribute('aria-labelledby')) {
        input.setAttribute('aria-label', labels.tenantSelector!);
      }
    });
  }
}

/** 在异步渲染后多次补标，避免 Playwright/axe 早于 Element Plus 挂载执行。 */
export function scheduleComboboxLabeling(
  root: ParentNode,
  labels: ComboboxLabelOptions
): void {
  const run = () => labelComboboxesIn(root, labels);
  run();
  for (const delayMs of [50, 150, 350]) {
    window.setTimeout(run, delayMs);
  }
}
