import { expect, test } from '@playwright/test';
import {
  findSeedAdminUserViaApi,
  loginAsHostAdmin,
  markAllInboxMessagesReadViaApi,
  sendHostInboxMessageViaApi,
  trackUiAccessToken
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('离线期间遗漏的站内信在 SignalR 重连后补拉恢复', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const recipient = await findSeedAdminUserViaApi(request, clientKind);
  await markAllInboxMessagesReadViaApi(request, clientKind);

  const getAccessToken = trackUiAccessToken(page);

  try {
    const recoverySocketControl = await loginAndWaitForNotificationsConnection(page);
    await expectUnreadCount(page, clientKind, 0);
    const accessToken = getAccessToken();

    await page.context().setOffline(true);
    await recoverySocketControl.disconnectFromServer();

    const suffix = clientKind === 'layui' ? 'l' : 'v';
    await sendHostInboxMessageViaApi(
      request,
      clientKind,
      recipient.id,
      {
        title: `重连修复-${Date.now().toString(36)}-${suffix}`,
        content: '真实 Worker 必须在恢复端离线期间消费 Outbox。',
        accessToken
      }
    );

    await page.context().setOffline(false);
    // 不刷新页面也不发送第二条消息；重连回调必须补拉数据库当前未读数。
    await expectUnreadCount(page, clientKind, 1, 30_000);
  } finally {
    await page.context().setOffline(false);
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
