import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createWriteStream, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { runPnpm } from './pnpm-process.mjs';
import { runPrintingBrowserResponseAction, watchPrintingBrowserCancellation } from './application-printing-browser-lifecycle.mjs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

const requireE2e = createRequire(new URL('../../e2e/admin-real-stack/package.json', import.meta.url));
const requireParity = createRequire(new URL('../../e2e/admin-parity/package.json', import.meta.url));
const { chromium, expect } = requireE2e('@playwright/test');
const AxeBuilder = requireParity('@axe-core/playwright');
const requestsPath = '/api/v1/enterprise_request/enterprise-requests';

// 业务操作由生成应用的实际 Vue 完成；HTTP 只建立所属模块夹具和回读权威结果。
export async function verifyEnterpriseApprovalBrowser(appRoot, apiUrl, reportDirectory,
  { port, signal, startWorker, stopWorker }) {
  const origin = 'http://localhost:' + port;
  const evidence = { completed: false, responses: [], accessibility: [], requests: [] };
  const execute = (stage, args) => {
    signal?.throwIfAborted();
    const result = runPnpm(args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000 });
    writeFileSync(join(reportDirectory, 'approval-' + stage + '.log'), (result.stdout ?? '') + (result.stderr ?? ''));
    assert.equal(result.status, 0, 'generated approval ' + stage + ' failed');
  };
  execute('install', ['install', '--frozen-lockfile']);
  execute('contracts', ['--filter', '@fullnet/client-contracts', 'build']);
  execute('build', ['--filter', '@fullnet/admin', 'build']);
  const stream = createWriteStream(join(reportDirectory, 'approval-vite.log'));
  const child = spawn(process.execPath, [join(appRoot, 'ui/admin/node_modules/vite/bin/vite.js'), '--host', 'localhost',
    '--port', String(port), '--strictPort', '--logLevel', 'error'], { cwd: join(appRoot, 'ui/admin'),
    env: { ...process.env, VITE_API_PROXY_TARGET: apiUrl, VITE_STRICT_CSP: '1' }, stdio: 'pipe', windowsHide: true });
  child.stdout.pipe(stream, { end: false }); child.stderr.pipe(stream, { end: false });
  let browser; let context; let cancellation; let page; let stage = 'vite'; let token; let failed = false;
  const send = async (path, method = 'GET', body, expected = 200) => {
    signal?.throwIfAborted();
    const response = await fetch(apiUrl + path, { method, headers: { Authorization: 'Bearer ' + token,
      Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body), redirect: 'error',
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000) });
    assert.equal(response.status, expected, path + ': unexpected HTTP ' + response.status);
    if (expected >= 400) { await response.body?.cancel(); return; }
    return response.json();
  };
  const poll = async (read, predicate, label) => {
    const deadline = Date.now() + 90_000;
    while (true) { const value = await read(); if (predicate(value)) return value;
      assert.ok(Date.now() < deadline, label + ' timeout'); await sleep(250, undefined, { signal }); }
  };
  const action = async (path, method, click, expected = 200) => {
    const response = await runPrintingBrowserResponseAction(page,
      response => new URL(response.url()).pathname === path && response.request().method() === method, click);
    assert.equal(response.status(), expected, path + ': unexpected UI HTTP ' + response.status()); return response.json();
  };
  try {
    cancellation = watchPrintingBrowserCancellation(signal, async () => { await context?.close(); await browser?.close(); });
    const deadline = Date.now() + 60_000;
    while (true) {
      signal?.throwIfAborted(); assert.equal(child.exitCode, null, 'generated approval Vue exited');
      try { if ((await fetch(origin, { signal: AbortSignal.timeout(2_000) })).ok) break; } catch { /* 等待本次服务器。 */ }
      assert.ok(Date.now() < deadline); await sleep(500, undefined, { signal });
    }
    browser = await chromium.launch(process.platform === 'win32' ? { channel: 'msedge' } : {});
    context = await browser.newContext(); page = await context.newPage();
    page.on('response', response => {
      const path = new URL(response.url()).pathname;
      if (['/api/v1/enterprise_request/', '/api/v1/workflow/', '/api/v1/notifications/'].some(prefix => path.startsWith(prefix)))
        evidence.responses.push({ path, method: response.request().method(), status: response.status() });
    });
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    const audit = async (surface, selector) => {
      await page.locator(selector).evaluate(async element => {
        await Promise.all(element.getAnimations({ subtree: true }).filter(animation => animation.effect?.getTiming().iterations !== Infinity)
          .map(animation => animation.finished.catch(() => {})));
      });
      const result = await new AxeBuilder({ page }).include(selector).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
      const violations = result.violations.map(({ id, nodes }) => ({ id, targets: nodes.map(node => node.target) }));
      evidence.accessibility.push({ surface, violations }); assert.equal(violations.length, 0, surface + ' accessibility');
    };
    stage = 'login'; await page.goto(origin);
    await page.getByLabel('账号', { exact: true }).fill('admin'); await page.getByLabel('密码', { exact: true }).fill('FullNet!2026Secure');
    await page.getByRole('button', { name: '进入控制台' }).click();
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({ timeout: 30_000 });
    stage = 'tenant'; await page.goto(origin + '/#/tenant-context');
    const tenantRow = page.locator('.tenant-context-view .el-table__row').filter({ hasText: 'local' });
    await expect(tenantRow).toHaveCount(1);
    const switched = await action('/api/v1/tenancy/context', 'PUT', () => tenantRow.getByRole('button', { name: '进入租户' }).click());
    token = switched.accessToken; assert.ok(token); const tenantId = switched.context.tenantId;
    await expect(page).toHaveURL(origin + '/#/');
    stage = 'fixtures'; const me = await send('/api/v1/me');
    const unit = await send('/api/v1/organization/units', 'POST',
      { parentId: null, code: 'approval-' + randomUUID().replaceAll('-', ''), name: 'Approval acceptance unit', displayOrder: 10 }, 201);
    await send('/api/v1/organization/user-units', 'POST', { userId: me.id, unitId: unit.id, isPrimary: false }, 201);
    const target = await send('/api/v1/identity/tenant-members/provision', 'POST', { username: 'approval-' + randomUUID().slice(0, 8),
      displayName: 'Acceptance assignee', password: 'FullNet!2026Secure', memberRole: 'Member', email: null });
    assert.ok(target.userId && target.userId !== me.id);
    const candidates = await send('/api/v1/workflow/definitions/recipient-candidates?page=1&pageSize=100');
    assert.ok(candidates.items.some(item => item.id === target.userId));
    const memberPolicy = { sources: [{ resolverKindKey: 'specified_users', userIds: [target.userId] }] };
    const preview = await send('/api/v1/workflow/definitions/assignee-preview', 'POST', { assigneePolicy: memberPolicy });
    assert.ok(preview.users.some(item => item.id === target.userId)); evidence.memberCandidateAndPreview = true;
    const form = await send('/api/v1/workflow/forms', 'POST', { formKey: 'approval.' + randomUUID(), draft: {
      schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [{ fieldKey: 'title', fieldTypeKey: 'text', required: false, constraints: {} }] }] } }, 201);
    const formVersion = await send('/api/v1/workflow/forms/' + form.id + '/publish', 'POST', { expectedRevision: form.draftRevision });
    const definitionKey = 'demo.enterprise_request.approval';
    const definition = await send('/api/v1/workflow/definitions', 'POST', { definitionKey, draft: { schemaVersion: 1, nodes: [
      { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['approve'] } },
      { nodeKey: 'approve', nodeTypeKey: 'human.approval', nodeSchemaVersion: 1, config: { nextNodeKeys: ['end'],
        assigneePolicy: { sources: [{ resolverKindKey: 'specified_users', userIds: [me.id] }] },
        approvalPolicy: { modeKey: 'any', approverUserIds: [me.id] } } },
      { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: { nextNodeKeys: [] } }] } }, 201);
    await send('/api/v1/workflow/definitions/' + definition.id + '/publish', 'POST', { expectedRevision: definition.draftRevision, formVersionId: formVersion.id });
    const view = page.locator('.generated-crud-view');
    const create = async outcome => {
      stage = 'create-' + outcome; await page.goto(origin + '/#/enterprise-requests');
      await view.getByRole('button', { name: '创建', exact: true }).click();
      const dialog = page.getByRole('dialog', { name: '创建', exact: true });
      const number = 'EA-' + outcome + '-' + randomUUID().slice(0, 8);
      await dialog.getByLabel('所属机构标识', { exact: true }).fill(unit.id);
      await dialog.getByLabel('申请编号', { exact: true }).fill(number);
      await dialog.getByLabel('标题', { exact: true }).fill('Acceptance ' + outcome);
      await dialog.getByLabel('申请金额', { exact: true }).fill('12.50');
      await dialog.getByLabel('申请人标识', { exact: true }).fill(me.id);
      const result = await action(requestsPath, 'POST', () => dialog.getByRole('button', { name: '保存', exact: true }).click(), 201);
      assert.equal(result.tenantId, tenantId); assert.equal(result.organizationUnitId, unit.id); assert.equal(result.status, 'Draft');
      await expect(dialog).toBeHidden(); evidence.requests.push({ id: result.id, outcome }); return result;
    };
    const approved = await create('Approved');
    const row = view.locator('.el-table__row').filter({ hasText: approved.requestNumber });
    stage = 'edit'; await row.getByRole('button', { name: '编辑', exact: true }).click();
    const edit = page.getByRole('dialog', { name: '编辑', exact: true }); await edit.getByLabel('标题', { exact: true }).fill('Acceptance Approved updated');
    await action(requestsPath + '/' + approved.id, 'PUT', () => edit.getByRole('button', { name: '保存', exact: true }).click()); await expect(edit).toBeHidden();
    stage = 'lines'; await row.getByRole('button', { name: '明细', exact: true }).click();
    const lines = page.getByRole('dialog', { name: '申请明细', exact: true });
    await lines.getByRole('button', { name: '编辑明细', exact: true }).click(); await lines.getByRole('button', { name: '添加明细', exact: true }).click();
    await lines.getByLabel('项目说明', { exact: true }).fill('Precision item'); await lines.getByLabel('数量', { exact: true }).fill('1.0001');
    await lines.getByLabel('单价', { exact: true }).fill('50');
    const savedLines = await action(requestsPath + '/' + approved.id + '/lines', 'PUT', () => lines.getByRole('button', { name: '保存明细', exact: true }).click());
    assert.equal(savedLines.totalAmount, '50.01'); assert.equal(savedLines.items[0].lineAmount, '50.01');
    await expect(lines.getByRole('table')).toContainText('Precision item'); await lines.getByRole('button', { name: '取消', exact: true }).click();
    stage = 'attachments'; await row.getByRole('button', { name: '附件', exact: true }).click();
    const attachments = page.getByRole('dialog', { name: '申请附件', exact: true }); const bytes = Buffer.from('owned application approval attachment');
    await attachments.locator('input[type="file"]').setInputFiles({ name: 'approval.txt', mimeType: 'text/plain', buffer: bytes });
    const uploaded = await action(requestsPath + '/' + approved.id + '/attachments', 'POST', () => attachments.getByRole('button', { name: '上传附件', exact: true }).click());
    const attachment = uploaded.attachment; assert.equal(attachment.originalFileName, 'approval.txt');
    await expect(attachments).toContainText('approval.txt');
    const [download] = await Promise.all([page.waitForEvent('download'), attachments.getByRole('button', { name: '下载', exact: true }).click()]);
    assert.equal(download.suggestedFilename(), 'approval.txt'); assert.deepEqual(readFileSync(await download.path()), bytes);
    await audit('attachments', '[role="dialog"][aria-label="申请附件"]');
    await attachments.getByRole('button', { name: '取消', exact: true }).click(); evidence.linesAndAttachments = true;
    const deleted = await create('Deleted');
    stage = 'detail-and-delete';
    const deletedRow = view.locator('.el-table__row').filter({ hasText: deleted.requestNumber });
    await deletedRow.getByRole('button', { name: '详情', exact: true }).click();
    const detail = page.getByRole('dialog', { name: '申请详情', exact: true });
    await expect(detail).toContainText(deleted.title); await detail.getByRole('button', { name: '取消', exact: true }).click();
    await deletedRow.getByRole('button', { name: '删除', exact: true }).click();
    const deletion = page.getByRole('dialog', { name: '确认删除', exact: true });
    await deletion.getByRole('button', { name: '取消', exact: true }).click(); await expect(deletedRow).toBeVisible();
    await deletedRow.getByRole('button', { name: '删除', exact: true }).click();
    await action(requestsPath + '/' + deleted.id + '/delete', 'POST', () => deletion.getByRole('button', { name: '确认删除', exact: true }).click());
    await expect(deletedRow).toHaveCount(0); await send(requestsPath + '/' + deleted.id, 'GET', undefined, 404);
    evidence.detailAndDelete = true;
    const rejected = await create('Rejected'); const cancelled = await create('Cancelled');
    const submit = async request => {
      stage = 'submit-' + request.id; await page.goto(origin + '/#/enterprise-requests');
      const current = view.locator('.el-table__row').filter({ hasText: request.requestNumber }); await expect(current).toBeVisible();
      await current.getByRole('button', { name: '提交审批', exact: true }).click();
      const dialog = page.getByRole('dialog', { name: '确认提交审批', exact: true });
      const result = await action(requestsPath + '/' + request.id + '/submit-for-approval', 'POST', () => dialog.getByRole('button', { name: '确认提交', exact: true }).click());
      assert.equal(result.status, 'Submitted'); await expect(dialog).toBeHidden(); return result;
    };
    const progress = id => send(requestsPath + '/' + id + '/approval-progress');
    for (const request of [approved, rejected, cancelled]) {
      const submitted = await submit(request); const queued = await progress(request.id);
      assert.equal(queued.deliveryState, 'queued'); assert.ok(queued.workflowInstanceId);
      const replay = await send(requestsPath + '/' + request.id + '/submit-for-approval', 'POST');
      assert.equal(replay.version, submitted.version); assert.equal((await progress(request.id)).workflowInstanceId, queued.workflowInstanceId);
    }
    evidence.queuedWithoutWorker = true; stage = 'start-worker'; await startWorker();
    const instances = new Map();
    for (const request of [approved, rejected, cancelled]) {
      const started = await poll(() => progress(request.id), value => value.deliveryState === 'started', 'approval start');
      const instance = await send('/api/v1/workflow/instances/' + started.workflowInstanceId);
      assert.equal(instance.statusKey, 'active'); assert.equal(instance.businessId, request.id); assert.ok(instance.activeTodoId);
      instances.set(request.id, instance);
    }
    stage = 'stop-worker-before-outcome'; await stopWorker(); evidence.workerStoppedBeforeOutcomes = true;
    const openInstance = async id => {
      await page.goto(origin + '/#/workflow/instances'); await page.getByTestId('workflow-instance-id').fill(id);
      await page.getByTestId('workflow-instance-search').click();
      await expect(page.getByTestId('workflow-instance-summary').getByText(id, { exact: true })).toBeVisible();
    };
    stage = 'reassign'; const initial = instances.get(approved.id); await openInstance(initial.id);
    for (const assignee of [target.userId, me.id]) {
      await page.getByTestId('workflow-instance-reassign').click();
      await page.getByTestId('workflow-reassign-user').fill(assignee); await page.getByTestId('workflow-reassign-reason').fill('Acceptance reassignment');
      await audit('reassignment', '[role="dialog"][aria-label="改派活动待办"]');
      await page.getByTestId('workflow-reassign-save').click();
      const result = await action('/api/v1/workflow/instances/' + initial.id + '/reassign', 'POST',
        () => page.locator('.el-message-box').getByRole('button', { name: '改派活动待办', exact: true }).click());
      assert.ok(result.revision > initial.revision);
      await expect(page.getByTestId('workflow-reassign-save')).toHaveCount(0);
      await expect(page.getByTestId('workflow-instance-reassign')).toBeEnabled();
    }
    evidence.reassignedAndReturned = true;
    const decide = async (request, outcome) => {
      stage = 'decision-' + outcome; await page.goto(origin + '/#/workflow/todos');
      await page.getByTestId('workflow-todo-definition-filter').fill(definitionKey); await page.getByTestId('workflow-todo-filter-apply').click();
      const businessTitle = instances.get(request.id).businessTitle; assert.ok(businessTitle);
      const todoRow = page.getByRole('row').filter({ hasText: businessTitle }); await expect(todoRow).toBeVisible({ timeout: 15_000 });
      await todoRow.getByTestId('workflow-todo-open').click(); await expect(page.getByTestId('workflow-form-renderer')).toBeVisible();
      await page.getByTestId('workflow-todo-comment').fill('Acceptance ' + outcome);
      const todoId = (await send('/api/v1/workflow/instances/' + instances.get(request.id).id)).activeTodoId;
      const result = await action('/api/v1/workflow/todos/' + todoId + '/' + outcome, 'POST', () => page.getByTestId('workflow-todo-' + outcome).click());
      assert.equal(result.statusKey, outcome === 'approve' ? 'completed' : 'rejected');
      assert.equal((await progress(request.id)).requestStatus, 'Submitted', 'business result must wait for independent Worker');
    };
    await decide(approved, 'approve'); await decide(rejected, 'reject');
    stage = 'cancel'; const cancelInstance = instances.get(cancelled.id); await openInstance(cancelInstance.id);
    await page.getByTestId('workflow-instance-cancel').click();
    const cancelResult = await action('/api/v1/workflow/instances/' + cancelInstance.id + '/cancel', 'POST',
      () => page.locator('.el-message-box').getByRole('button', { name: '取消实例', exact: true }).click());
    assert.equal(cancelResult.statusKey, 'cancelled'); assert.equal((await progress(cancelled.id)).requestStatus, 'Submitted');
    stage = 'restart-worker'; await startWorker();
    for (const request of [approved, rejected, cancelled]) {
      const expected = evidence.requests.find(item => item.id === request.id).outcome;
      const final = await poll(() => progress(request.id), value => value.deliveryState === 'finalized', 'business outcome');
      assert.equal(final.requestStatus, expected); assert.ok(final.completedAtUtc); assert.equal(final.workflowInstanceId, instances.get(request.id).id);
      evidence.requests.find(item => item.id === request.id).finalVersion = final.requestVersion;
    }
    evidence.durableOutcomeRecovered = true;
    const inbox = () => send('/api/v1/notifications/my-inbox-messages?page=1&pageSize=100');
    const terminalTitles = new Map([[approved.id, '审批已完成'], [rejected.id, '审批已驳回'], [cancelled.id, '审批已取消']]);
    stage = 'notification'; const messages = await poll(inbox, page => [approved, rejected, cancelled].every(request =>
      page.items.some(item => item.title === terminalTitles.get(request.id) && item.content?.includes(request.id))), 'business notifications');
    for (const request of [approved, rejected, cancelled]) assert.equal(messages.items.filter(item =>
      item.title === terminalTitles.get(request.id) && item.content?.includes(request.id)).length, 1);
    const message = messages.items.find(item => item.title === terminalTitles.get(approved.id) && item.content?.includes(approved.id)); assert.ok(message);
    await page.goto(origin + '/#/notifications/inbox-messages');
    const messageRow = page.locator('.inbox-messages-data-table .el-table__row').filter({ hasText: message.content });
    await expect(messageRow).toHaveCount(1); await messageRow.click();
    await expect(page.getByTestId('inbox-messages-detail-drawer')).toContainText(approved.id); evidence.notificationViewed = true;
    await page.screenshot({ path: join(reportDirectory, 'approval-inbox.png'), fullPage: true });
    stage = 'restart-idempotency'; const messageIds = messages.items.map(item => item.id).sort(); await stopWorker(); await startWorker();
    await sleep(2500, undefined, { signal });
    assert.deepEqual((await inbox()).items.map(item => item.id).sort(), messageIds, 'restart duplicated notifications');
    for (const request of [approved, rejected, cancelled]) {
      const final = await progress(request.id); assert.equal(final.requestVersion, evidence.requests.find(item => item.id === request.id).finalVersion);
      assert.equal(final.workflowInstanceId, instances.get(request.id).id);
    }
    const list = await send('/api/v1/workflow/instances?page=1&pageSize=100&definitionKey=' + definitionKey);
    for (const request of [approved, rejected, cancelled]) assert.equal(list.items.filter(item => item.businessId === request.id).length, 1);
    evidence.restartStable = true; evidence.completed = true; return evidence;
  } catch (error) {
    failed = true; evidence.failureKind = error?.name ?? 'Error'; evidence.error = stage + ': generated approval browser acceptance failed';
    // 凭据与未知服务端正文不进入证据；固定阶段足以定位相应拥有的日志。
    throw new Error(evidence.error);
  } finally {
    const errors = [];
    try { await cancellation?.dispose(); } catch (error) { errors.push(error); }
    if (failed && page) try { await page.screenshot({ path: join(reportDirectory, 'approval-failure.png'), fullPage: true }); } catch { /* 页面可能已取消。 */ }
    try { await context?.close(); } catch (error) { errors.push(error); }
    try { await browser?.close(); } catch (error) { errors.push(error); }
    try { await stopLoggedProcess(child, stream); } catch (error) { errors.push(error); }
    evidence.cleanupSucceeded = errors.length === 0; writeFileSync(join(reportDirectory, 'approval-browser.json'), JSON.stringify(evidence, null, 2));
    if (errors.length && !failed) throw new AggregateError(errors, 'Approval browser cleanup failed');
  }
}
