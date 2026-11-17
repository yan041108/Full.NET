import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const scheduleId = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const modelId = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const runId = '019bc2b1-2a40-7cc3-8992-a80de51bf301';
const now = '2026-11-18T04:00:00Z';
const permissions = [
  'identity.navigation.read', 'calendar.personal_schedules.read', 'calendar.personal_schedules.set_status',
  'calendar.personal_schedules.assistant_complete', 'ai.agent_runs.create', 'ai.agent_runs.read',
  'ai.agent_runs.resume', 'ai.agent_approvals.read', 'ai.agent_approvals.request', 'ai.models.read'
];
const json = (route, body, status = 200) => route.fulfill({ status, json: body });

for (const width of [1440, 390]) {
  test(`完成日程回执的锁定模型在 ${width} 像素可读`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.clock.setFixedTime(new Date(now));
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    await page.route('**/api/v1/auth/refresh', route => json(route, {
      accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z'
    }));
    await page.route('**/api/v1/me', route => json(route, {
      id: scheduleId, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host',
      isSuperAdministrator: false, passwordChangeRequired: false, sessionId: scheduleId,
      preferredLocale: 'zh-CN', profileVersion: 1, permissions
    }));
    await page.route('**/api/v1/navigation', route => json(route, [{
      id: 'personal-schedules', parentId: null, routeName: 'personal-schedules', path: '/calendar/personal-schedules',
      componentKey: 'personal-schedules', title: '个人日程', caption: '', icon: 'calendar', order: 1,
      requiredPermission: 'calendar.personal_schedules.read', children: []
    }]));
    await page.route('**/api/v1/calendar/my-personal-schedules?*', route => json(route, {
      items: [{ id: scheduleId, content: '完成日程对比度验证', startAtUtc: now, endAtUtc: '2026-11-18T04:30:00Z',
        status: 'pending', version: 1, createdAtUtc: now, updatedAtUtc: null, completedAtUtc: null }],
      page: 1, pageSize: 500, total: 1
    }));
    await page.route('**/api/v1/ai/model-configs?*', route => json(route, {
      items: [{ id: modelId, name: '日程完成配置', isEnabled: true, isDefault: false, hasApiKey: false,
        createdAtUtc: now, updatedAtUtc: null, lastTestedAtUtc: null, lastTestMessage: null,
        lastTestStatusKey: null, maskedEndpointBaseUrl: 'https://provider.test', modelId: 'metadata',
        providerKey: 'ollama', tenantId: null, version: 1 }], page: 1, pageSize: 100, total: 1
    }));
    await page.route('**/api/v1/ai/agent/runs', route => json(route, { runId }, 202));
    await page.route('**/api/v1/ai/agent/runs/' + runId, route => json(route, {
      id: runId, statusKey: 'completed', createdAtUtc: now, updatedAtUtc: now, deadlineAtUtc: now,
      definitionKey: 'fullnet-personal-schedule-complete-v1', definitionVersion: 1
    }));
    await page.route('**/api/v1/ai/agent/runs/' + runId + '/schedule-completion', route => json(route, {
      runId, scheduleId, operationId: runId, expectedVersion: 1, actualVersion: 2,
      state: 'completed', reason: null, completedAtUtc: now
    }));

    await page.goto('/#/calendar/personal-schedules');
    await page.getByTestId('personal-schedules-assistant-complete').click();
    const dialog = page.getByRole('dialog', { name: '独立审批后完成日程', exact: true });
    await dialog.getByTestId('schedule-completion-model').click();
    await page.getByRole('option', { name: '日程完成配置', exact: true }).click();
    await expect(page.getByRole('listbox')).toBeHidden();
    await dialog.getByTestId('schedule-completion-create').click();
    await expect(dialog.getByTestId('schedule-completion-result')).toContainText('接收方已确认本操作完成');
    await expect(dialog.getByTestId('schedule-completion-model').locator('input')).toBeDisabled();
    const contrast = await dialog.getByTestId('schedule-completion-model')
      .locator('.el-select__selected-item > span').evaluate(element => {
        const rgba = value => value.match(/[\d.]+/g).map(Number);
        const blend = (foreground, background) => foreground.slice(0, 3)
          .map((channel, index) => channel * (foreground[3] ?? 1) + background[index] * (1 - (foreground[3] ?? 1)));
        const layers = [];
        for (let node = element; node; node = node.parentElement) {
          layers.push(rgba(getComputedStyle(node).backgroundColor));
        }
        const background = layers.reverse().reduce((color, layer) => blend(layer, color), [255, 255, 255]);
        const foreground = blend(rgba(getComputedStyle(element).color), background);
        const luminance = color => color.map(channel => {
          const value = channel / 255;
          return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
        }).reduce((sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index], 0);
        const light = luminance(background), dark = luminance(foreground);
        return (Math.max(light, dark) + 0.05) / (Math.min(light, dark) + 0.05);
      });
    expect(contrast).toBeGreaterThanOrEqual(4.5);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    expect(await dialog.evaluate(node => node.scrollWidth <= node.clientWidth + 1)).toBe(true);
  });
}
