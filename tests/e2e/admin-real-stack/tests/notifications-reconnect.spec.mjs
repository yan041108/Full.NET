import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import {
  adminOrigin,
  findSeedAdminUserViaApi,
  loginAsHostAdmin,
  markAllInboxMessagesReadViaApi,
  sendHostInboxMessageViaApi
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('离线期间遗漏的站内信在 SignalR 重连后补拉恢复', async ({
  browser,
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const recipient = await findSeedAdminUserViaApi(request, clientKind);
  await markAllInboxMessagesReadViaApi(request, clientKind);
  const processedBefore = await readProcessedInboxEventCount();

  const observerContext = await browser.newContext({
    baseURL: adminOrigin(clientKind)
  });
  await observerContext.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
  const observerPage = await observerContext.newPage();

  try {
    const [recoverySocketControl] = await Promise.all([
      loginAndWaitForNotificationsConnection(page),
      loginAndWaitForNotificationsConnection(observerPage)
    ]);
    await expectUnreadCount(page, clientKind, 0);
    await expectUnreadCount(observerPage, clientKind, 0);

    await page.context().setOffline(true);
    await recoverySocketControl.disconnectFromServer();

    const suffix = clientKind === 'layui' ? 'l' : 'v';
    await sendHostInboxMessageViaApi(
      request,
      clientKind,
      recipient.id,
      {
        title: `重连修复-${Date.now().toString(36)}-${suffix}`,
        content: '真实 Worker 必须在恢复端离线期间消费 Outbox。'
      }
    );

    await expect.poll(
      readProcessedInboxEventCount,
      {
        message: '独立 Worker 应在恢复端离线期间处理站内信 Outbox',
        timeout: 20_000
      }
    ).toBeGreaterThan(processedBefore);
    // Worker 只有在 Redis 发布成功后才记录 processed；在线端徽标同时证明下行可见。
    await expectUnreadCount(observerPage, clientKind, 1, 20_000);

    await page.context().setOffline(false);
    // 不刷新页面也不发送第二条消息；重连回调必须补拉数据库当前未读数。
    await expectUnreadCount(page, clientKind, 1, 30_000);
  } finally {
    await page.context().setOffline(false);
    await observerContext.close();
  }
});

async function loginAndWaitForNotificationsConnection(page) {
  let activeServerRoute;
  let resolveConnected;
  const connected = new Promise(resolve => {
    resolveConnected = resolve;
  });
  await page.routeWebSocket(
    url => url.pathname === '/hubs/notifications',
    pageRoute => {
      const serverRoute = pageRoute.connectToServer();
      activeServerRoute = serverRoute;
      serverRoute.onMessage(message => {
        pageRoute.send(message);
        resolveConnected?.();
        resolveConnected = undefined;
      });
    }
  );
  await loginAsHostAdmin(page);
  await connected;
  return {
    async disconnectFromServer() {
      if (!activeServerRoute) {
        throw new Error('Notifications SignalR server route 尚未建立');
      }

      await activeServerRoute.close({
        code: 1012,
        reason: 'e2e reconnect exercise'
      });
    }
  };
}

async function expectUnreadCount(page, clientKind, count, timeout = 15_000) {
  const button = clientKind === 'layui'
    ? page.locator('[data-shell-notifications-open]')
    : page.locator('.art-header__notice-btn');
  const expected = count === 0 ? '通知' : `通知 (${count})`;
  await expect(button).toHaveAttribute('aria-label', expected, { timeout });
}

async function readProcessedInboxEventCount() {
  const statePath = new URL('../.stack-state.json', import.meta.url);
  const state = JSON.parse(await readFile(statePath, 'utf8'));
  const workerLogPath = process.env.FULLNET_E2E_WORKER_LOG_PATH
    ?? state.workerLogPath;
  const log = await readFile(workerLogPath, 'utf8').catch(() => '');
  return log.split('fullnet.notifications.inbox.received').length - 1;
}
