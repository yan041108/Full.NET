import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const path = '/api/v1/calendar/my-personal-schedules';
const createdAtUtc = '2026-11-09T04:00:00.000Z';
const json = (route, body, status = 200) => route.fulfill({ status, json: body });

test.use({ timezoneId: 'Asia/Shanghai' });

test('个人日程提交失败保留输入，再次打开可用并按本地钟点提交 UTC', async ({ page }, info) => {
  test.skip(info.project.metadata.clientKind !== 'vue', '个人日程由 Vue 后台交付');
  const writes = [];
  let schedule;
  await page.clock.setFixedTime(new Date(createdAtUtc));
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, {
    accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z'
  }));
  await page.route('**/api/v1/me', route => json(route, {
    id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1,
    permissions: ['identity.navigation.read', 'calendar.personal_schedules.read', 'calendar.personal_schedules.create']
  }));
  await page.route('**/api/v1/navigation', route => json(route, [{
    id: 'personal-schedules', parentId: null, routeName: 'personal-schedules', path: '/calendar/personal-schedules',
    componentKey: 'personal-schedules', title: '个人日程', caption: '', icon: 'calendar', order: 1,
    requiredPermission: 'calendar.personal_schedules.read', children: []
  }]));
  await page.route('**' + path + '?*', route => json(route, {
    items: schedule ? [schedule] : [], page: 1, pageSize: 500, total: schedule ? 1 : 0
  }));
  await page.route('**' + path, route => {
    const body = route.request().postDataJSON();
    writes.push(body);
    if (writes.length === 1) return route.fulfill({
      status: 409, contentType: 'application/problem+json',
      body: JSON.stringify({ status: 409, code: 'calendar.fixture_conflict', title: '保存冲突，请重试', traceId: 'calendar-trace' })
    });
    schedule = { id, ...body, status: 'pending', completedAtUtc: null, createdAtUtc, updatedAtUtc: null, version: 1 };
    return json(route, schedule, 201);
  });

  await page.goto('/#/calendar/personal-schedules');
  const create = page.getByTestId('personal-schedules-create');
  const dialog = page.getByRole('dialog', { name: '新建个人日程' });
  const content = dialog.getByRole('textbox').first();
  const start = dialog.locator('#personal-schedules-start');
  const submit = dialog.getByTestId('personal-schedules-submit');
  await create.click();
  await submit.click();
  await expect(dialog.getByRole('alert')).toContainText('请输入日程内容');
  expect(writes).toHaveLength(0);

  await content.fill('保留的日程内容');
  await submit.click();
  await expect(dialog.getByTestId('personal-schedules-submit-problem')).toContainText('保存冲突，请重试');
  await expect(dialog.getByTestId('personal-schedules-submit-problem')).toContainText('calendar-trace');
  await expect(content).toHaveValue('保留的日程内容');
  await dialog.getByRole('button', { name: '取消', exact: true }).click();
  await expect(dialog).toBeHidden();

  await create.click();
  await expect(dialog).toBeVisible();
  await expect(content).toHaveValue('');
  await expect(dialog.getByTestId('personal-schedules-submit-problem')).toHaveCount(0);
  await expect(start).toHaveValue('2026-11-09 09:00:00');
  await start.fill('2026-11-09 09:30:00');
  await start.press('Enter');
  await content.fill('实际保存的日程');
  await submit.click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText('个人日程已创建')).toBeVisible();
  await expect(page.getByText('实际保存的日程')).toBeVisible();
  expect(writes).toEqual([
    { content: '保留的日程内容', startAtUtc: '2026-11-09T01:00:00.000Z', endAtUtc: '2026-11-09T02:00:00.000Z' },
    { content: '实际保存的日程', startAtUtc: '2026-11-09T01:30:00.000Z', endAtUtc: '2026-11-09T02:00:00.000Z' }
  ]);
});
