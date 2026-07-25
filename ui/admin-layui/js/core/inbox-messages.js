/**
 * 装配消息中心视图；支持收件箱列表、已读操作与 Host 发信。
 */
export function createInboxMessagesController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const sendForm = root.querySelector('[data-inbox-messages-send-form]');
  const directory = root.querySelector('[data-inbox-messages-directory]');
  const unreadNode = root.querySelector('[data-inbox-messages-unread-count]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = Promise.all([
      request('/api/v1/notifications/my-inbox-messages?page=1&pageSize=20'),
      request('/api/v1/notifications/my-inbox-messages/unread-count')
    ])
      .then(([page, unread]) => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        if (unreadNode) {
          unreadNode.textContent = String(unread?.unreadCount ?? 0);
        }
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('inboxMessages.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onSend = async event => {
    event.preventDefault();
    if (changing || !sendForm) return;
    const data = new FormData(sendForm);
    const recipientUserId = String(data.get('recipientUserId') ?? '').trim();
    const title = String(data.get('title') ?? '').trim();
    const content = String(data.get('content') ?? '').trim();
    if (!recipientUserId || !title || !content) return;
    changing = true;
    try {
      await request('/api/v1/notifications/host-inbox-messages', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ recipientUserId, title, content })
      });
      sendForm.reset();
      notify(translation().t('inboxMessages.sendSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('inboxMessages.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = async event => {
    const readButton = event.target instanceof Element
      ? event.target.closest('[data-inbox-messages-read]')
      : undefined;
    if (!readButton || changing) return;
    const messageId = readButton.dataset.inboxMessagesRead;
    changing = true;
    try {
      await request(
        `/api/v1/notifications/my-inbox-messages/${encodeURIComponent(messageId)}/read`,
        { method: 'POST' }
      );
      notify(translation().t('inboxMessages.markReadSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('inboxMessages.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onMarkAllRead = async () => {
    if (changing) return;
    changing = true;
    try {
      await request('/api/v1/notifications/my-inbox-messages/read-all', { method: 'POST' });
      notify(translation().t('inboxMessages.markAllReadSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('inboxMessages.operationFailed'));
    } finally {
      changing = false;
    }
  };

  sendForm?.addEventListener('submit', onSend);
  directory?.addEventListener('click', onDirectoryAction);
  root.querySelector('[data-inbox-messages-mark-all]')
    ?.addEventListener('click', onMarkAllRead);
  return {
    load,
    dispose() {
      sendForm?.removeEventListener('submit', onSend);
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(directory, items, translation) {
  if (!directory) return;
  if (items.length === 0) {
    directory.innerHTML = `<p class="fn-empty">${escapeHtml(translation.t('inboxMessages.emptyList'))}</p>`;
    return;
  }

  directory.innerHTML = items.map(item => {
    const statusLabel = item.status === 'read'
      ? translation.t('inboxMessages.statusRead')
      : translation.t('inboxMessages.statusUnread');
    const readAction = item.status === 'unread'
      ? `<button type="button" class="layui-btn layui-btn-primary layui-btn-sm"
            data-inbox-messages-read="${escapeHtml(item.id)}">${escapeHtml(translation.t('inboxMessages.markRead'))}</button>`
      : '';
    return `<article class="fn-data-row">
      <div>
        <strong translate="no">${escapeHtml(item.title)}</strong>
        <span class="layui-badge">${escapeHtml(statusLabel)}</span>
        <p>${escapeHtml(item.content)}</p>
        <small>${escapeHtml(translation.t('inboxMessages.createdAt'))}: ${escapeHtml(item.createdAtUtc)}</small>
      </div>
      <div class="fn-data-row__actions">${readAction}</div>
    </article>`;
  }).join('');
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-inbox-messages-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.error';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-inbox-messages-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  if (typeof window.layui?.layer?.msg === 'function') {
    window.layui.layer.msg(message, { icon });
  }
}
