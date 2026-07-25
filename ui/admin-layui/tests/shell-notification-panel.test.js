import { describe, expect, it } from 'vitest';
import { createShellNotificationPanel } from '../js/core/shell-notification-panel.js';

describe('shell-notification-panel', () => {
  it('打开通知面板并切换待办空状态', () => {
    document.body.innerHTML = `
      <div id="root">
        <button data-shell-notifications-open></button>
        <aside data-shell-notifications hidden>
          <span data-shell-notifications-title></span>
          <button data-shell-notifications-mark-read></button>
          <ul data-shell-notifications-tabs></ul>
          <div data-shell-notifications-body></div>
          <p data-shell-notifications-empty hidden></p>
          <button data-shell-notifications-view-all></button>
        </aside>
      </div>
    `;
    const root = document.getElementById('root');
    const panel = createShellNotificationPanel(root);
    const t = (key, params) => {
      if (key === 'shell.noticeEmpty') return `暂无${params?.name ?? ''}`;
      if (key === 'shell.noticeTabPending') return '待办';
      if (key === 'shell.noticeTabNotice') return '通知';
      if (key === 'shell.noticeTabMessage') return '消息';
      return key;
    };
    panel.render(t);
    root.querySelector('[data-shell-notifications-open]').click();
    expect(root.querySelector('[data-shell-notifications]').classList.contains('is-open')).toBe(true);
    root.querySelectorAll('.fn-notice-panel__tab')[2].click();
    expect(root.querySelector('[data-shell-notifications-empty]').textContent).toBe('暂无待办');
  });
});
