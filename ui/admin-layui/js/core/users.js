/**
 * 装配 Host 用户管理视图；禁用操作需经 Layer 确认，避免误触立即撤销会话。
 */
export function createUsersController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-users-create-form]');
  const directory = root.querySelector('[data-users-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/identity/users?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('users.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const username = String(data.get('username') ?? '').trim();
    const displayName = String(data.get('displayName') ?? '').trim();
    const password = String(data.get('password') ?? '');
    if (!username || !displayName || !password) return;
    changing = true;
    try {
      await request('/api/v1/identity/users', jsonRequest({ username, displayName, password }));
      form.reset();
      notify(translation().t('users.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('users.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDisable = event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-users-disable]')
      : undefined;
    if (!button || changing) return;
    const userId = button.dataset.usersDisable;
    const username = button.dataset.username ?? '';
    const message = translation().t('users.confirmDisable', { name: username });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/identity/users/${encodeURIComponent(userId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('users.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('users.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDisable);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDisable);
    }
  };
}

function jsonRequest(body) {
  return {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}

function renderDirectory(container, users, translation) {
  if (!container) return;
  if (users.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-users__empty';
    empty.textContent = translation.t('users.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  users.forEach(user => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-users__mark';
    mark.textContent = String(user.username ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = user.displayName ?? '';
    const username = container.ownerDocument.createElement('code');
    username.textContent = user.username ?? '';
    identity.append(name, username);
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(user.isActive ? 'users.active' : 'users.inactive');
    article.append(mark, identity, state);
    if (user.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.usersDisable = user.id;
      disable.dataset.username = user.username;
      disable.textContent = translation.t('users.disable');
      article.append(disable);
    }
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function confirmAction(message, confirm) {
  if (globalThis.layui?.layer?.confirm) {
    globalThis.layui.layer.confirm(message, { icon: 3 }, (index) => {
      globalThis.layui.layer.close(index);
      void confirm();
    });
    return;
  }
  if (globalThis.confirm(message)) {
    void confirm();
  }
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-users-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.host_user_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-users-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
