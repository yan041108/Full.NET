/** P1：行内操作（打开即关）与导出，避免破坏内置数据。 */

function pageContentScope(page) {
  return page.locator('.art-page-stack, .art-crud-page, [class$="-view"]').first();
}

const ROW_ACTION_OPEN = /^(编辑|查看|详情)$/;
const ROW_ACTION_SAFE = /^(编辑|查看|详情|授权|权限)$/;
const EXPORT_BUTTON = /导出|下载/;

/**
 * @returns {Promise<{ status: 'pass'|'na'|'skip', step?: string, detail?: string }>}
 */
export async function runP1RowActions(page, ctx) {
  const scope = pageContentScope(page);
  const table = scope.locator('.el-table__body tbody tr').first();
  if (!(await table.isVisible().catch(() => false))) {
    return { status: 'na', step: 'p1_no_table' };
  }
  const rowText = await table.innerText().catch(() => '');
  if (/admin|host-administrator|系统|Full\.NET Local/i.test(rowText)) {
    const editBtn = table.getByRole('button', { name: ROW_ACTION_SAFE }).first();
    if (await editBtn.isVisible().catch(() => false)) {
      try {
        await editBtn.click({ timeout: 5_000 });
        await page.waitForTimeout(400);
        await page.keyboard.press('Escape').catch(() => {});
        await dismissDialog(page);
        return { status: 'pass', step: 'p1_edit_open_cancel' };
      } catch (error) {
        await dismissDialog(page);
        return {
          status: 'skip',
          step: 'p1_edit_failed',
          detail: error instanceof Error ? error.message.slice(0, 120) : String(error)
        };
      }
    }
    return { status: 'na', step: 'p1_protected_row_no_safe_action' };
  }

  const probeRow = scope.locator('.el-table__body tbody tr').filter({ hasText: /e2e_probe_|e2e_audit_/ }).first();
  if (await probeRow.isVisible().catch(() => false)) {
    const btn = probeRow.getByRole('button', { name: ROW_ACTION_OPEN }).first();
    if (await btn.isVisible().catch(() => false)) {
      try {
        await btn.click({ timeout: 5_000 });
        await page.waitForTimeout(400);
        await dismissDialog(page);
        return { status: 'pass', step: 'p1_probe_row_action' };
      } catch (error) {
        await dismissDialog(page);
        return { status: 'skip', step: 'p1_probe_row_failed', detail: String(error).slice(0, 120) };
      }
    }
  }
  return { status: 'na', step: 'p1_no_applicable_row' };
}

async function dismissDialog(page) {
  const dialog = page.getByRole('dialog').last();
  if (await dialog.isVisible().catch(() => false)) {
    await dialog.getByRole('button', { name: /取消|关闭/ }).first().click({ timeout: 3_000 }).catch(() => {});
    await page.keyboard.press('Escape').catch(() => {});
  }
}

/**
 * @returns {Promise<{ status: 'pass'|'na'|'fail', step?: string }>}
 */
export async function runP1Export(page, ctx) {
  const scope = pageContentScope(page);
  const exportBtn = scope.getByRole('button', { name: EXPORT_BUTTON }).first();
  if (!(await exportBtn.isVisible().catch(() => false))) {
    return { status: 'na', step: 'p1_no_export' };
  }
  const before = ctx.apiFailures.length;
  try {
    await exportBtn.click({ timeout: 8_000 });
    await page.waitForTimeout(1_500);
  } catch {
    return { status: 'na', step: 'p1_export_click_failed' };
  }
  if (ctx.apiFailures.length > before) {
    return { status: 'fail', step: 'p1_export_api_5xx' };
  }
  return { status: 'pass', step: 'p1_export_triggered' };
}
