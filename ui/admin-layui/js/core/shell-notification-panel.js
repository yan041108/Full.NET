const noticeItems = [
  { title: '平台运行状态正常', time: '2026-07-25 07:00', type: 'notice' },
  { title: '双库迁移校验通过', time: '2026-07-24 18:30', type: 'notice' },
  { title: 'Outbox 队列无积压', time: '2026-07-24 09:15', type: 'notice' }
];

const messageItems = [
  { title: '系统管理员 登录成功', time: '2026-07-25 06:58', avatarText: '系', avatarColor: '#0b8f87' },
  { title: '租户上下文已切换', time: '2026-07-24 16:20', avatarText: '租', avatarColor: '#409eff' }
];

const pendingItems = [];

const iconClassMap = {
  notice: 'layui-icon-notice',
  message: 'layui-icon-dialogue',
  email: 'layui-icon-email',
  collection: 'layui-icon-star',
  user: 'layui-icon-user'
};

/**
 * Layui 顶栏通知面板；结构与 Vue ArtNotificationPanel 对齐，数据为壳层演示项。
 */
export function createShellNotificationPanel(root, options = {}) {
  const panel = root.querySelector('[data-shell-notifications]');
  const openButton = root.querySelector('[data-shell-notifications-open]');
  const markReadButton = root.querySelector('[data-shell-notifications-mark-read]');
  const viewAllButton = root.querySelector('[data-shell-notifications-view-all]');
  const tabsNode = root.querySelector('[data-shell-notifications-tabs]');
  const bodyNode = root.querySelector('[data-shell-notifications-body]');
  const emptyNode = root.querySelector('[data-shell-notifications-empty]');
  const titleNode = root.querySelector('[data-shell-notifications-title]');
  let translate = key => key;
  let activeTab = 0;
  let isOpen = false;
  let unreadCount = 0;

  function lists() {
    return [noticeItems, messageItems, pendingItems];
  }

  function tabLabels() {
    return [
      translate('shell.noticeTabNotice'),
      translate('shell.noticeTabMessage'),
      translate('shell.noticeTabPending')
    ];
  }

  function renderTabs() {
    if (!tabsNode) {
      return;
    }

    const labels = tabLabels();
    const ownerDocument = tabsNode.ownerDocument;
    const fragment = ownerDocument.createDocumentFragment();
    labels.forEach((label, index) => {
      const item = ownerDocument.createElement('li');
      item.className = 'fn-notice-panel__tab';
      item.classList.toggle('is-active', index === activeTab);
      item.textContent = `${label} (${lists()[index].length})`;
      item.addEventListener('click', () => {
        activeTab = index;
        render();
      });
      fragment.append(item);
    });
    tabsNode.replaceChildren(fragment);
  }

  function renderBody() {
    if (!bodyNode || !emptyNode) {
      return;
    }

    const data = lists()[activeTab] ?? [];
    bodyNode.replaceChildren();
    if (data.length === 0) {
      emptyNode.hidden = false;
      const label = tabLabels()[activeTab] ?? '';
      emptyNode.textContent = translate('shell.noticeEmpty', { name: label });
      return;
    }

    emptyNode.hidden = true;
    const ownerDocument = bodyNode.ownerDocument;
    const list = ownerDocument.createElement('ul');
    list.className = 'fn-notice-panel__list';
    data.forEach(entry => {
      const item = ownerDocument.createElement('li');
      item.className = 'fn-notice-panel__item';
      if (activeTab === 0) {
        const icon = ownerDocument.createElement('span');
        icon.className = 'fn-notice-panel__icon is-theme';
        const iconGlyph = ownerDocument.createElement('i');
        iconGlyph.className = `layui-icon ${iconClassMap[entry.type] ?? iconClassMap.notice}`;
        iconGlyph.setAttribute('aria-hidden', 'true');
        icon.append(iconGlyph);
        item.append(icon);
      } else if (activeTab === 1) {
        const avatar = ownerDocument.createElement('span');
        avatar.className = 'fn-notice-panel__avatar';
        avatar.style.background = entry.avatarColor;
        avatar.textContent = entry.avatarText;
        item.append(avatar);
      }

      const body = ownerDocument.createElement('div');
      const title = ownerDocument.createElement('h4');
      title.textContent = entry.title;
      const time = ownerDocument.createElement('p');
      time.textContent = entry.time;
      body.append(title, time);
      item.append(body);
      list.append(item);
    });
    bodyNode.append(list);
  }

  function render(t) {
    if (t) {
      translate = t;
    }

    if (titleNode) {
      titleNode.textContent = translate('shell.noticeTitle');
    }
    if (markReadButton) {
      markReadButton.textContent = translate('shell.noticeMarkRead');
    }
    if (viewAllButton) {
      viewAllButton.textContent = translate('shell.noticeViewAll');
    }
    renderUnreadCount();

    renderTabs();
    renderBody();
    if (panel) {
      panel.classList.toggle('is-open', isOpen);
      panel.hidden = !isOpen;
    }
  }

  function open() {
    isOpen = true;
    render();
  }

  function close() {
    isOpen = false;
    if (panel) {
      panel.classList.remove('is-open');
      panel.hidden = true;
    }
  }

  function renderUnreadCount() {
    if (!openButton) {
      return;
    }

    let badge = openButton.querySelector('[data-shell-notifications-unread]');
    if (!badge) {
      badge = openButton.ownerDocument.createElement('span');
      badge.className = 'fn-topbar__unread-badge';
      badge.dataset.shellNotificationsUnread = '';
      badge.setAttribute('aria-hidden', 'true');
      openButton.append(badge);
    }

    badge.textContent = unreadCount > 99 ? '99+' : String(unreadCount);
    badge.hidden = unreadCount === 0;
    const label = translate('shell.notifications');
    openButton.setAttribute(
      'aria-label',
      unreadCount > 0 ? `${label} (${unreadCount})` : label
    );
  }

  function onDocumentClick(event) {
    if (!isOpen) {
      return;
    }

    const target = event.target;
    if (target instanceof Element
      && (target.closest('[data-shell-notifications-open]')
        || target.closest('[data-shell-notifications]'))) {
      return;
    }

    close();
  }

  const onToggle = (event) => {
    event?.stopPropagation?.();
    if (isOpen) {
      close();
    } else {
      open();
    }
  };

  openButton?.addEventListener('click', onToggle);
  viewAllButton?.addEventListener('click', () => close());
  markReadButton?.addEventListener('click', () => close());
  document.addEventListener('click', onDocumentClick);

  return {
    render,
    close,
    setUnreadCount(value) {
      unreadCount = Number.isSafeInteger(value) && value >= 0 ? value : 0;
      renderUnreadCount();
    },
    dispose() {
      openButton?.removeEventListener('click', onToggle);
      document.removeEventListener('click', onDocumentClick);
    }
  };
}
