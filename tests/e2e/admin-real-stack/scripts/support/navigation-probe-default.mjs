const QUERY_BUTTON = /查询|搜索|重新加载/;
const CREATE_BUTTON = /^(新增|创建|添加|新建)/;
const SUBMIT_BUTTON = /^(确定|保存|创建|提交|确认)$/;

export function createProbeContext() {
  return {
    apiFailures: [],
    consoleErrors: [],
    stamp: Date.now().toString(36)
  };
}

export function attachProbeListeners(page, ctx) {
  const onConsole = msg => {
    if (msg.type() === 'error') {
      const text = msg.text();
      if (text.includes('favicon') || text.includes('WebSocket closed')) {
        return;
      }
      ctx.consoleErrors.push(text.slice(0, 500));
    }
  };
  const onResponse = response => {
    const url = response.url();
    if (!url.includes('/api/') || url.includes('/auth/refresh')) {
      return;
    }
    const status = response.status();
    if (status >= 500) {
      ctx.apiFailures.push({ url, status });
    }
  };
  page.on('console', onConsole);
  page.on('response', onResponse);
  return () => {
    page.off('console', onConsole);
    page.off('response', onResponse);
  };
}

export async function dismissBlockingOverlays(page) {
  for (let attempt = 0; attempt < 3; attempt += 1) {
    const dialogOpen = await page.getByRole('dialog').first().isVisible().catch(() => false);
    const viteOverlay = await page.locator('vite-error-overlay').isVisible().catch(() => false);
    if (!dialogOpen && !viteOverlay) {
      return;
    }
    await page.keyboard.press('Escape').catch(() => {});
    await page.waitForTimeout(200);
  }
}

function pageContentScope(page) {
  return page.locator('.art-page-stack, .art-crud-page, .overview, [class$="-view"]').first();
}

export async function runListProbe(page) {
  await dismissBlockingOverlays(page);
  const scope = pageContentScope(page);
  const button = scope.getByRole('button', { name: QUERY_BUTTON }).first();
  if (await button.isVisible().catch(() => false)) {
    await button.click({ timeout: 5_000 });
    await page.waitForTimeout(800);
  }
}

async function fillRequiredFieldsInDialog(page, stamp) {
  const dialog = page.getByRole('dialog').last();
  await dialog.waitFor({ state: 'visible', timeout: 8_000 });

  const inputs = dialog.locator(
    'input:not([type="hidden"]):not([disabled]), textarea:not([disabled])'
  );
  const count = await inputs.count();
  for (let index = 0; index < count; index += 1) {
    const input = inputs.nth(index);
    if (!(await input.isVisible().catch(() => false))) {
      continue;
    }
    const value = await input.inputValue().catch(() => '');
    if (value.trim()) {
      continue;
    }
    const readonly = await input.getAttribute('readonly');
    const role = await input.getAttribute('role');
    if (readonly !== null || role === 'combobox') {
      continue;
    }
    const type = (await input.getAttribute('type')) ?? 'text';
    if (type === 'number') {
      await input.fill('1');
      continue;
    }
    if (type === 'checkbox' || type === 'radio') {
      continue;
    }
    await input.fill(`e2e_probe_${stamp}_${index}`, { timeout: 3_000 }).catch(() => {});
  }

  const selects = dialog.locator('.el-select');
  const selectCount = await selects.count();
  for (let index = 0; index < selectCount; index += 1) {
    const select = selects.nth(index);
    if (!(await select.isVisible().catch(() => false))) {
      continue;
    }
    await select.click({ timeout: 3_000 }).catch(() => {});
    const option = page.locator('.el-select-dropdown:visible .el-select-dropdown__item').first();
    if (await option.isVisible().catch(() => false)) {
      await option.click({ timeout: 3_000 }).catch(() => {});
    }
  }
}

async function findCreateButton(scope) {
  const byTestId = scope.locator('[data-testid$="-action-create"]').first();
  if (await byTestId.isVisible().catch(() => false)) {
    return byTestId;
  }
  const byRole = scope.getByRole('button', { name: CREATE_BUTTON }).first();
  if (await byRole.isVisible().catch(() => false)) {
    return byRole;
  }
  return null;
}

export async function runCreateProbe(page, stamp) {
  const scope = pageContentScope(page);
  const create = await findCreateButton(scope);
  if (!create) {
    return { mode: 'readOnly', step: 'no_create_button' };
  }
  try {
    await create.click({ timeout: 8_000 });
    await fillRequiredFieldsInDialog(page, stamp);
    const submit = page.getByRole('dialog').last().getByRole('button', { name: SUBMIT_BUTTON });
    await submit.first().click({ timeout: 8_000 });
  } catch (error) {
    await dismissBlockingOverlays(page);
    return {
      mode: 'needs_scenario',
      step: 'create_probe_heuristic_failed',
      detail: error instanceof Error ? error.message.slice(0, 200) : String(error)
    };
  }

  const errorToast = page.locator('.el-message--error');
  if (await errorToast.isVisible().catch(() => false)) {
    const text = await errorToast.innerText().catch(() => 'error toast');
    return { mode: 'fail', step: 'submit_error_toast', detail: text };
  }

  const dialog = page.getByRole('dialog').last();
  const stillOpen = await dialog.isVisible().catch(() => false);
  if (stillOpen) {
    await dismissBlockingOverlays(page);
    return { mode: 'needs_scenario', step: 'dialog_still_open_after_submit' };
  }

  await dismissBlockingOverlays(page);
  return { mode: 'pass', step: 'create_submitted' };
}

export async function hasErrorMessage(page) {
  return page.locator('.el-message--error').isVisible().catch(() => false);
}
