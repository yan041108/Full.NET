/**
 * 装配 Host 系统配置目录视图；支持创建、值更新与禁用。
 */
import { applyPermissionVisibility } from './navigation.js';

export function createConfigEntriesController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-config-entries-create-form]');
  const directory = root.querySelector('[data-config-entries-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/settings/config-entries?page=1&pageSize=20')
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
        showProblem(root, problem, translation().t('configEntries.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const configKey = String(data.get('configKey') ?? '').trim().toLowerCase();
    const displayName = String(data.get('displayName') ?? '').trim();
    const description = String(data.get('description') ?? '').trim();
    const valueKind = String(data.get('valueKind') ?? 'string').trim().toLowerCase();
    const value = String(data.get('value') ?? '').trim();
    const displayOrder = Number.parseInt(String(data.get('displayOrder') ?? '0'), 10) || 0;
    if (!configKey || !displayName) return;
    changing = true;
    try {
      await request(
        '/api/v1/settings/config-entries',
        jsonRequest({
          configKey,
          displayName,
          description: description || null,
          valueKind,
          value,
          displayOrder
        })
      );
      form.reset();
      const valueKindSelect = form.querySelector('[name="valueKind"]');
      if (valueKindSelect) valueKindSelect.value = 'string';
      notify(translation().t('configEntries.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('configEntries.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-config-entries-edit]')
      : undefined;
    if (editButton && !changing) {
      const entryId = editButton.dataset.configEntriesEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const currentValue = editButton.dataset.value ?? '';
      const displayName = editButton.dataset.displayName ?? '';
      const description = editButton.dataset.description ?? '';
      const displayOrder = Number(editButton.dataset.displayOrder ?? '0');
      promptText(translation().t('configEntries.editTitle'), currentValue, async nextValue => {
        changing = true;
        try {
          await request(
            `/api/v1/settings/config-entries/${encodeURIComponent(entryId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                displayName,
                description: description || null,
                value: nextValue,
                displayOrder,
                version
              })
            }
          );
          notify(translation().t('configEntries.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('configEntries.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-config-entries-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const entryId = disableButton.dataset.configEntriesDisable;
    const configKey = disableButton.dataset.configKey ?? '';
    const message = translation().t('configEntries.confirmDisable', { name: configKey });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/settings/config-entries/${encodeURIComponent(entryId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('configEntries.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('configEntries.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDirectoryAction);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDirectoryAction);
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

function renderDirectory(container, entries, translation) {
  if (!container) return;
  if (entries.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('configEntries.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  entries.forEach(entry => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-tenants__mark';
    mark.textContent = String(entry.configKey ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = entry.displayName ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = entry.configKey ?? '';
    identity.append(name, meta);
    const valueMeta = container.ownerDocument.createElement('small');
    valueMeta.textContent = `${entry.valueKind ?? ''} · ${entry.value ?? ''} · ${translation.t('configEntries.displayOrder')}: ${entry.displayOrder ?? 0}`;
    identity.append(valueMeta);
    if (entry.description) {
      const description = container.ownerDocument.createElement('small');
      description.textContent = entry.description;
      identity.append(description);
    }
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(entry.isActive ? 'configEntries.active' : 'configEntries.inactive');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-tenants__actions';
    const edit = container.ownerDocument.createElement('button');
    edit.type = 'button';
    edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
    edit.dataset.permission = 'settings.config.update';
    edit.dataset.configEntriesEdit = entry.id;
    edit.dataset.version = String(entry.version ?? 0);
    edit.dataset.value = entry.value ?? '';
    edit.dataset.displayName = entry.displayName ?? '';
    edit.dataset.description = entry.description ?? '';
    edit.dataset.displayOrder = String(entry.displayOrder ?? 0);
    edit.textContent = translation.t('configEntries.edit');
    actions.append(edit);
    if (entry.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.permission = 'settings.config.disable';
      disable.dataset.configEntriesDisable = entry.id;
      disable.dataset.configKey = entry.configKey;
      disable.textContent = translation.t('configEntries.disable');
      actions.append(disable);
    }
    article.append(mark, identity, state, actions);
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

function promptText(title, value, confirm) {
  if (globalThis.layui?.layer?.prompt) {
    globalThis.layui.layer.prompt({ title, value, formType: 0 }, (input, index) => {
      globalThis.layui.layer.close(index);
      if (input !== undefined && input !== null) void confirm(input);
    });
    return;
  }
  const input = globalThis.prompt(title, value);
  if (input !== null) void confirm(input);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-config-entries-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.settings_config_entry_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-config-entries-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
