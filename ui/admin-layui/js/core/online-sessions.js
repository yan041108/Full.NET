/**
 * 装配 Host 在线会话列表与强制下线视图。
 */
import { applyPermissionVisibility } from './navigation.js';

export function createOnlineSessionsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-online-sessions-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/identity/online-sessions?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        if (typeof options.getPermissions === 'function') {
          applyPermissionVisibility(root, options.getPermissions());
        }
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('onlineSessions.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onDirectoryAction = event => {
    const revokeButton = event.target instanceof Element
      ? event.target.closest('[data-online-sessions-revoke]')
      : undefined;
    if (!revokeButton || changing) return;
    const sessionId = revokeButton.dataset.onlineSessionsRevoke;
    const username = revokeButton.dataset.username ?? '';
    const message = translation().t('onlineSessions.confirmRevoke', { name: username });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/identity/online-sessions/${encodeURIComponent(sessionId)}/revoke`,
          { method: 'POST' }
        );
        notify(translation().t('onlineSessions.revokeSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('onlineSessions.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  directory?.addEventListener('click', onDirectoryAction);
  return {
    load,
    dispose() {
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(container, items, translation) {
  if (!container) return;
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('onlineSessions.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = item.displayName ?? '';
    const username = container.ownerDocument.createElement('code');
    username.textContent = item.username ?? '';
    const client = container.ownerDocument.createElement('small');
    client.textContent = `${translation.t('onlineSessions.clientId')}: ${item.clientId ?? ''}`;
    const created = container.ownerDocument.createElement('small');
    created.textContent = `${translation.t('onlineSessions.createdAt')}: ${item.createdAtUtc ?? ''}`;
    const expires = container.ownerDocument.createElement('small');
    expires.textContent = `${translation.t('onlineSessions.expiresAt')}: ${item.expiresAtUtc ?? ''}`;
    identity.append(name, username, client, created, expires);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-users__actions';
    const revoke = container.ownerDocument.createElement('button');
    revoke.type = 'button';
    revoke.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
    revoke.dataset.onlineSessionsRevoke = item.id;
    revoke.dataset.permission = 'identity.sessions.revoke';
    revoke.dataset.username = item.username ?? '';
    revoke.textContent = translation.t('onlineSessions.revoke');
    actions.append(revoke);
    article.append(identity, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function confirmAction(message, confirm) {
  if (globalThis.layui?.layer?.confirm) {
    globalThis.layui.layer.confirm(message, { icon: 3 }, index => {
      globalThis.layui.layer.close(index);
      void confirm();
    });
    return;
  }
  if (globalThis.confirm(message)) {
    void confirm();
  }
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-online-sessions-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'error';
  panel.querySelector('span').textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-online-sessions-problem]');
  if (panel) panel.hidden = true;
}
