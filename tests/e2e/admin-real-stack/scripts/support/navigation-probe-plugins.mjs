import {
  dismissBlockingOverlays,
  runListProbe
} from './navigation-probe-default.mjs';

/** 仅列表/查询探针，不尝试新增。 */
const READ_ONLY_KEYS = new Set([
  'overview',
  'online-sessions',
  'access-logs',
  'operation-logs',
  'exception-logs',
  'outbound-call-logs',
  'host-job-executions',
  'host-job-health',
  'observability-elasticsearch-health',
  'observability-server-monitor',
  'observability-log-files',
  'document-statistics',
  'document-recycle-bin',
  'workflow-todos',
  'workflow-cc',
  'workflow-instances',
  'workflow-recovery-tasks',
  'notifications-deliveries',
  'notifications-inbox-messages',
  'notifications-my-host-announcements',
  'notifications-preferences',
  'ai-agent-runs',
  'ocr-id-card-tasks',
  'reporting-execute',
  'printing-preview',
  'code-generation-previews',
  'host-messaging-ops',
  'module-selection',
  'tenant-context',
  'k3cloud-document-syncs',
  'open-access-clients',
  'oidc-signing-keys'
]);

async function readOnlyPlugin(page) {
  await runListProbe(page);
  return { status: 'readOnly', step: 'plugin_read_only' };
}

async function overviewPlugin(page) {
  await page.locator('.overview, [data-route-heading]').first().waitFor({
    state: 'visible',
    timeout: 15_000
  });
  return { status: 'readOnly', step: 'overview_dashboard' };
}

async function usersCreatePlugin(page, ctx) {
  await runListProbe(page);
  await page.locator('.users-view').waitFor({ state: 'visible', timeout: 15_000 });
  const create = page.locator('.users-view').getByTestId('users-action-create');
  if (!(await create.isVisible().catch(() => false))) {
    return { status: 'readOnly', step: 'users_no_create' };
  }
  const stamp = ctx.stamp;
  const username = `e2e_probe_${stamp}`;
  const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
  try {
    await create.click({ timeout: 8_000 });
    const dialog = page.getByRole('dialog').last();
    await dialog.getByLabel('用户名', { exact: true }).fill(username);
    await dialog.getByLabel('姓名', { exact: true }).fill(`探针用户 ${stamp}`);
    await dialog.getByLabel('初始密码', { exact: true }).fill(password);
    await dialog.getByTestId('users-editor-submit').click({ timeout: 8_000 });
    await dialog.waitFor({ state: 'hidden', timeout: 15_000 });
  } catch (error) {
    await dismissBlockingOverlays(page);
    return {
      status: 'needs_scenario',
      step: 'users_create_failed',
      detail: error instanceof Error ? error.message.slice(0, 200) : String(error)
    };
  }
  await dismissBlockingOverlays(page);
  return { status: 'pass', step: 'users_create' };
}

async function rolesCreatePlugin(page, ctx) {
  await runListProbe(page);
  await page.locator('.roles-view').waitFor({ state: 'visible', timeout: 15_000 });
  const create = page.locator('.roles-view').getByTestId('roles-action-create');
  if (!(await create.isVisible().catch(() => false))) {
    return { status: 'readOnly', step: 'roles_no_create' };
  }
  const stamp = ctx.stamp;
  const code = `e2e_role_${stamp}`;
  try {
    await create.click({ timeout: 8_000 });
    const dialog = page.getByRole('dialog').last();
    const form = dialog.getByTestId('roles-create-form');
    await form.locator('input').nth(0).fill(code);
    await form.locator('input').nth(1).fill(`探针角色 ${stamp}`);
    await dialog.getByTestId('roles-editor-submit').click({ timeout: 8_000 });
    await dialog.waitFor({ state: 'hidden', timeout: 15_000 });
  } catch (error) {
    await dismissBlockingOverlays(page);
    return {
      status: 'needs_scenario',
      step: 'roles_create_failed',
      detail: error instanceof Error ? error.message.slice(0, 200) : String(error)
    };
  }
  await dismissBlockingOverlays(page);
  return { status: 'pass', step: 'roles_create' };
}

async function menusCreatePlugin(page, ctx) {
  await runListProbe(page);
  await page.getByTestId('menus-tree-table').waitFor({ state: 'visible', timeout: 15_000 });
  const create = page.getByTestId('menus-action-create');
  if (!(await create.isVisible().catch(() => false))) {
    return { status: 'readOnly', step: 'menus_no_create' };
  }
  const stamp = ctx.stamp;
  const routeName = `e2e-probe-${stamp}`;
  const menuPath = `/e2e-probe/${stamp}`;
  try {
    await create.click({ timeout: 8_000 });
    const dialog = page.getByRole('dialog').last();
    const form = dialog.getByTestId('menus-editor-form');
    await form.getByText('目录', { exact: true }).click({ timeout: 3_000 }).catch(() => {});
    await form.locator('input').first().fill(routeName);
    await form.getByLabel('标题', { exact: true }).fill(`探针目录 ${stamp}`).catch(() =>
      form.locator('input').nth(1).fill(`探针目录 ${stamp}`)
    );
    await form.getByLabel('路径', { exact: true }).fill(menuPath).catch(() =>
      form.locator('input').filter({ hasNot: page.locator('[disabled]') }).nth(2).fill(menuPath)
    );
    await dialog.getByTestId('menus-editor-submit').click({ timeout: 8_000 });
    await dialog.waitFor({ state: 'hidden', timeout: 15_000 });
  } catch (error) {
    await dismissBlockingOverlays(page);
    return {
      status: 'needs_scenario',
      step: 'menus_create_failed',
      detail: error instanceof Error ? error.message.slice(0, 200) : String(error)
    };
  }
  await dismissBlockingOverlays(page);
  return { status: 'pass', step: 'menus_create' };
}

async function apiKeysCreatePlugin(page, ctx) {
  await runListProbe(page);
  await page.locator('.api-keys-view').waitFor({ state: 'visible', timeout: 15_000 });
  const create = page.getByTestId('api-keys-action-create');
  if (!(await create.isVisible().catch(() => false))) {
    return { status: 'readOnly', step: 'api_keys_no_create' };
  }
  if (!ctx.adminUserId) {
    return { status: 'needs_scenario', step: 'api_keys_no_admin_id' };
  }
  const stamp = ctx.stamp;
  const displayName = `e2e_audit_key_${stamp}`;
  try {
    await create.click({ timeout: 8_000 });
    const dialog = page.getByRole('dialog').last();
    await dialog.getByTestId('api-key-user-id').fill(ctx.adminUserId);
    await dialog.getByTestId('api-key-display-name').fill(displayName);
    await dialog.getByTestId('api-key-permissions').fill('identity.users.read');
    await dialog.getByTestId('api-keys-editor-submit').click({ timeout: 8_000 });
    await page.waitForTimeout(2_000);
    const secret = page.locator('[data-testid="api-key-secret"]');
    if (!(await secret.isVisible().catch(() => false))) {
      await dismissBlockingOverlays(page);
      return { status: 'needs_scenario', step: 'api_keys_no_secret_shown' };
    }
    await page.keyboard.press('Escape').catch(() => {});
  } catch (error) {
    await dismissBlockingOverlays(page);
    return {
      status: 'needs_scenario',
      step: 'api_keys_create_failed',
      detail: error instanceof Error ? error.message.slice(0, 200) : String(error)
    };
  }
  await dismissBlockingOverlays(page);
  return { status: 'pass', step: 'api_keys_create' };
}

async function tenantsCreatePlugin(page, ctx) {
  await runListProbe(page);
  await page
    .locator('.tenants-view, section.art-page-stack')
    .first()
    .waitFor({ state: 'visible', timeout: 25_000 });
  const create = page.getByTestId('tenants-action-create');
  if (!(await create.isVisible().catch(() => false))) {
    return { status: 'readOnly', step: 'tenants_no_create' };
  }
  const stamp = ctx.stamp;
  const id = `e2eaudit${stamp}`.slice(0, 32);
  try {
    await create.click({ timeout: 8_000 });
    const dialog = page.getByRole('dialog').last();
    const form = dialog.getByTestId('tenants-editor-form');
    const inputs = form.locator('input');
    await inputs.nth(0).fill(id);
    await inputs.nth(1).fill(`探针租户 ${stamp}`);
    await inputs.nth(2).fill(`${id}.local.test`);
    await page.getByTestId('tenants-editor-submit').click({ timeout: 8_000 });
    await dialog.waitFor({ state: 'hidden', timeout: 20_000 });
  } catch (error) {
    await dismissBlockingOverlays(page);
    return {
      status: 'needs_scenario',
      step: 'tenants_create_failed',
      detail: error instanceof Error ? error.message.slice(0, 200) : String(error)
    };
  }
  await dismissBlockingOverlays(page);
  return { status: 'pass', step: 'tenants_create' };
}

/** @type {Map<string, (page: import('@playwright/test').Page, ctx: object) => Promise<{ status: string, step?: string, detail?: string }>>} */
const plugins = new Map();

for (const key of READ_ONLY_KEYS) {
  plugins.set(key, readOnlyPlugin);
}

plugins.set('users', usersCreatePlugin);
plugins.set('roles', rolesCreatePlugin);
plugins.set('menus', menusCreatePlugin);
plugins.set('api-keys', apiKeysCreatePlugin);
plugins.set('tenants', tenantsCreatePlugin);
plugins.set('overview', overviewPlugin);

/** 按 componentKey 前缀匹配（如 ai-、payments-）仅列表。 */
const READ_ONLY_PREFIXES = ['auditing-', 'observability-', 'payments-', 'k3cloud-', 'ocr-'];

export function resolvePlugin(componentKey) {
  if (plugins.has(componentKey)) {
    return plugins.get(componentKey);
  }
  for (const prefix of READ_ONLY_PREFIXES) {
    if (componentKey.startsWith(prefix)) {
      return readOnlyPlugin;
    }
  }
  return null;
}

export function registerPlugin(componentKey, handler) {
  plugins.set(componentKey, handler);
}
