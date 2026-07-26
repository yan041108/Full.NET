/**
 * 装配 Host API Key 管理视图；明文只保存在当前控制器内存与一次性展示节点中。
 */
export function createApiKeysController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const canWrite = options.canWrite ?? (() => false);
  const clipboard = options.clipboard ?? globalThis.navigator?.clipboard;
  const confirm = options.confirm ?? confirmAction;
  const form = root.querySelector('[data-api-keys-form]');
  const secretPanel = root.querySelector('[data-api-keys-secret]');
  const copyButton = root.querySelector('[data-api-keys-copy]');
  const directory = root.querySelector('[data-api-keys-directory]');
  let currentSecret = '';
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/identity/api-keys?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation(),
          canWrite()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('apiKeys.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onSubmit = async event => {
    event.preventDefault();
    if (changing || !form || !canWrite()) return;
    const data = new FormData(form);
    const userId = String(data.get('userId') ?? '').trim();
    const displayName = String(data.get('displayName') ?? '').trim();
    const permissions = [...new Set(
      String(data.get('permissions') ?? '')
        .split(/[\n,]+/)
        .map(value => value.trim())
        .filter(Boolean)
    )];
    const expiresAtValue = String(data.get('expiresAtUtc') ?? '').trim();
    if (!userId || !displayName || permissions.length === 0) return;

    changing = true;
    clearSecret(secretPanel);
    currentSecret = '';
    try {
      const result = await request('/api/v1/identity/api-keys', jsonRequest({
        userId,
        displayName,
        permissions,
        expiresAtUtc: expiresAtValue
          ? new Date(expiresAtValue).toISOString()
          : null
      }));
      if (!result?.secret || typeof result.secret !== 'string') {
        throw new Error('client.invalid_create_host_api_key_result');
      }
      currentSecret = result.secret;
      renderSecret(secretPanel, currentSecret);
      form.reset();
      notify(translation().t('apiKeys.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('apiKeys.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onCopy = async () => {
    if (!currentSecret || !clipboard?.writeText) return;
    try {
      await clipboard.writeText(currentSecret);
      notify(translation().t('apiKeys.copySuccess'), 1);
    } catch (problem) {
      showProblem(root, problem, translation().t('apiKeys.operationFailed'));
    }
  };

  const onDirectoryAction = async event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-api-keys-disable]')
      : undefined;
    if (!button || changing || !canWrite()) return;
    const accepted = await confirm(
      translation().t('apiKeys.confirmDisable', {
        name: button.dataset.displayName ?? ''
      })
    );
    if (!accepted) return;

    changing = true;
    try {
      await request(
        `/api/v1/identity/api-keys/${encodeURIComponent(button.dataset.apiKeysDisable)}/disable`,
        { method: 'POST' }
      );
      notify(translation().t('apiKeys.disableSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('apiKeys.operationFailed'));
    } finally {
      changing = false;
    }
  };

  form?.addEventListener('submit', onSubmit);
  copyButton?.addEventListener('click', onCopy);
  directory?.addEventListener('click', onDirectoryAction);

  return {
    load,
    dispose() {
      currentSecret = '';
      clearSecret(secretPanel);
      form?.removeEventListener('submit', onSubmit);
      copyButton?.removeEventListener('click', onCopy);
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(container, items, translation, canWrite) {
  if (!container) return;
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('apiKeys.emptyList');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    article.className = 'fn-tenants__item';
    const identity = container.ownerDocument.createElement('div');
    identity.append(
      textElement(container, 'strong', item.displayName ?? ''),
      textElement(container, 'code', item.username ?? ''),
      textElement(container, 'small', `${translation.t('apiKeys.prefix')}: ${item.keyPrefix ?? ''}`),
      textElement(container, 'small', `${translation.t('apiKeys.permissions')}: ${(item.permissions ?? []).join(', ')}`),
      textElement(container, 'small', `${translation.t('apiKeys.expiresAt')}: ${item.expiresAtUtc ?? translation.t('apiKeys.noExpiration')}`),
      textElement(container, 'small', `${translation.t('apiKeys.lastUsedAt')}: ${item.lastUsedAtUtc ?? translation.t('apiKeys.never')}`),
      textElement(container, 'span', item.isActive
        ? translation.t('apiKeys.statusActive')
        : translation.t('apiKeys.statusDisabled'))
    );
    article.append(identity);
    if (canWrite && item.isActive) {
      const actions = container.ownerDocument.createElement('div');
      actions.className = 'fn-tenants__actions';
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.apiKeysDisable = item.id ?? '';
      disable.dataset.displayName = item.displayName ?? '';
      disable.textContent = translation.t('apiKeys.disable');
      actions.append(disable);
      article.append(actions);
    }
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function textElement(container, tagName, value) {
  const element = container.ownerDocument.createElement(tagName);
  element.textContent = value;
  return element;
}

function renderSecret(panel, secret) {
  if (!panel) return;
  const code = panel.querySelector('code');
  if (code) code.textContent = secret;
  panel.hidden = false;
}

function clearSecret(panel) {
  if (!panel) return;
  const code = panel.querySelector('code');
  if (code) code.textContent = '';
  panel.hidden = true;
}

function jsonRequest(body) {
  return {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  };
}

function confirmAction(message) {
  if (globalThis.layui?.layer?.confirm) {
    return new Promise(resolve => {
      globalThis.layui.layer.confirm(message, { icon: 3 }, index => {
        globalThis.layui.layer.close(index);
        resolve(true);
      });
    });
  }
  return Promise.resolve(globalThis.confirm(message));
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-api-keys-problem]');
  if (!panel) return;
  panel.hidden = false;
  const code = panel.querySelector('strong');
  const title = panel.querySelector('span');
  if (code) code.textContent = problem?.code ?? 'error';
  if (title) title.textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-api-keys-problem]');
  if (panel) panel.hidden = true;
}
