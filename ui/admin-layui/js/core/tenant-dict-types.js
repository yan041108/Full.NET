/**
 * 装配租户数据字典类型与字典项视图；类型目录支持创建/更新/禁用，选型后管理字典项。
 */
import { applyPermissionVisibility } from './navigation.js';

export function createTenantDictTypesController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-tenant-dict-types-create-form]');
  const directory = root.querySelector('[data-tenant-dict-types-directory]');
  const itemsPanel = root.querySelector('[data-tenant-dict-items-panel]');
  const itemsForm = root.querySelector('[data-tenant-dict-items-create-form]');
  const itemsDirectory = root.querySelector('[data-tenant-dict-items-directory]');
  const itemsPanelTitle = root.querySelector('[data-tenant-dict-items-panel-title]');
  const itemsClose = root.querySelector('[data-tenant-dict-items-close]');
  let loading;
  let changing = false;
  let selectedTypeId;
  let selectedTypeCode = '';

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/settings/tenant-dict-types?page=1&pageSize=20')
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
        if (selectedTypeId) {
          const stillExists = (page?.items ?? []).some(item => item.id === selectedTypeId);
          if (!stillExists) {
            closeItems();
          }
        }
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('dictTypes.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const loadItems = async () => {
    if (!selectedTypeId || !itemsDirectory) return;
    try {
      const page = await request(
        `/api/v1/settings/tenant-dict-types/${encodeURIComponent(selectedTypeId)}/items?page=1&pageSize=20`
      );
      renderItemsDirectory(
        itemsDirectory,
        Array.isArray(page?.items) ? page.items : [],
        translation()
      );
      if (typeof options.getPermissions === 'function') {
        applyPermissionVisibility(root, options.getPermissions());
      }
      hideProblem(root);
    } catch (problem) {
      showProblem(root, problem, translation().t('dictItems.loadFailed'));
    }
  };

  const openItems = async (dictTypeId, code) => {
    selectedTypeId = dictTypeId;
    selectedTypeCode = code ?? '';
    if (itemsPanel) itemsPanel.hidden = false;
    if (itemsForm) itemsForm.hidden = false;
    if (itemsPanelTitle) {
      itemsPanelTitle.textContent = translation().t('dictItems.panelTitle', {
        name: selectedTypeCode
      });
    }
    await loadItems();
  };

  const closeItems = () => {
    selectedTypeId = undefined;
    selectedTypeCode = '';
    if (itemsPanel) itemsPanel.hidden = true;
    if (itemsForm) {
      itemsForm.hidden = true;
      itemsForm.reset();
    }
    if (itemsDirectory) itemsDirectory.replaceChildren();
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const code = String(data.get('code') ?? '').trim().toLowerCase();
    const name = String(data.get('name') ?? '').trim();
    const description = String(data.get('description') ?? '').trim();
    const displayOrder = Number.parseInt(String(data.get('displayOrder') ?? '0'), 10) || 0;
    if (!code || !name) return;
    changing = true;
    try {
      await request(
        '/api/v1/settings/tenant-dict-types',
        jsonRequest({
          code,
          name,
          description: description || null,
          displayOrder
        })
      );
      form.reset();
      notify(translation().t('dictTypes.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('dictTypes.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onCreateItem = async event => {
    event.preventDefault();
    if (changing || !itemsForm || !selectedTypeId) return;
    const data = new FormData(itemsForm);
    const label = String(data.get('label') ?? '').trim();
    const value = String(data.get('value') ?? '').trim().toLowerCase();
    const color = String(data.get('color') ?? '').trim();
    const displayOrder = Number.parseInt(String(data.get('displayOrder') ?? '0'), 10) || 0;
    if (!label || !value) return;
    changing = true;
    try {
      await request(
        `/api/v1/settings/tenant-dict-types/${encodeURIComponent(selectedTypeId)}/items`,
        jsonRequest({
          label,
          value,
          color: color || null,
          displayOrder
        })
      );
      itemsForm.reset();
      notify(translation().t('dictItems.createSuccess'), 1);
      await loadItems();
    } catch (problem) {
      showProblem(root, problem, translation().t('dictItems.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const manageButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-dict-types-items]')
      : undefined;
    if (manageButton && !changing) {
      void openItems(
        manageButton.dataset.tenantDictTypesItems,
        manageButton.dataset.code ?? ''
      );
      return;
    }

    const editButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-dict-types-edit]')
      : undefined;
    if (editButton && !changing) {
      const dictTypeId = editButton.dataset.tenantDictTypesEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const displayOrder = Number(editButton.dataset.displayOrder ?? '0');
      const currentName = editButton.dataset.name ?? '';
      const currentDescription = editButton.dataset.description ?? '';
      promptText(translation().t('dictTypes.editTitle'), currentName, async nextName => {
        if (!nextName.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/settings/tenant-dict-types/${encodeURIComponent(dictTypeId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                name: nextName.trim(),
                description: currentDescription || null,
                displayOrder,
                version
              })
            }
          );
          notify(translation().t('dictTypes.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('dictTypes.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-dict-types-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const dictTypeId = disableButton.dataset.tenantDictTypesDisable;
    const code = disableButton.dataset.code ?? '';
    const message = translation().t('dictTypes.confirmDisable', { name: code });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/settings/tenant-dict-types/${encodeURIComponent(dictTypeId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('dictTypes.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('dictTypes.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  const onItemsDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-dict-items-edit]')
      : undefined;
    if (editButton && !changing) {
      const dictItemId = editButton.dataset.tenantDictItemsEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const displayOrder = Number(editButton.dataset.displayOrder ?? '0');
      const currentLabel = editButton.dataset.label ?? '';
      const currentColor = editButton.dataset.color ?? '';
      promptText(translation().t('dictItems.editTitle'), currentLabel, async nextLabel => {
        if (!nextLabel.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/settings/tenant-dict-items/${encodeURIComponent(dictItemId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                label: nextLabel.trim(),
                color: currentColor || null,
                displayOrder,
                version
              })
            }
          );
          notify(translation().t('dictItems.updateSuccess'), 1);
          await loadItems();
        } catch (problem) {
          showProblem(root, problem, translation().t('dictItems.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-dict-items-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const dictItemId = disableButton.dataset.tenantDictItemsDisable;
    const value = disableButton.dataset.value ?? '';
    const message = translation().t('dictItems.confirmDisable', { name: value });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/settings/tenant-dict-items/${encodeURIComponent(dictItemId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('dictItems.disableSuccess'), 1);
        await loadItems();
      } catch (problem) {
        showProblem(root, problem, translation().t('dictItems.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onCreate);
  itemsForm?.addEventListener('submit', onCreateItem);
  directory?.addEventListener('click', onDirectoryAction);
  itemsDirectory?.addEventListener('click', onItemsDirectoryAction);
  itemsClose?.addEventListener('click', closeItems);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      itemsForm?.removeEventListener('submit', onCreateItem);
      directory?.removeEventListener('click', onDirectoryAction);
      itemsDirectory?.removeEventListener('click', onItemsDirectoryAction);
      itemsClose?.removeEventListener('click', closeItems);
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

function renderDirectory(container, dictTypes, translation) {
  if (!container) return;
  if (dictTypes.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('dictTypes.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  dictTypes.forEach(dictType => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-tenants__mark';
    mark.textContent = String(dictType.code ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = dictType.name ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = dictType.code ?? '';
    identity.append(name, meta);
    const order = container.ownerDocument.createElement('small');
    order.textContent = `${translation.t('dictTypes.displayOrder')}: ${dictType.displayOrder ?? 0}`;
    identity.append(order);
    if (dictType.description) {
      const description = container.ownerDocument.createElement('small');
      description.textContent = dictType.description;
      identity.append(description);
    }
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(dictType.isActive ? 'dictTypes.active' : 'dictTypes.inactive');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-tenants__actions';
    const manage = container.ownerDocument.createElement('button');
    manage.type = 'button';
    manage.className = 'layui-btn layui-btn-primary layui-btn-sm';
    manage.dataset.tenantDictTypesItems = dictType.id;
    manage.dataset.code = dictType.code ?? '';
    manage.textContent = translation.t('dictItems.manage');
    actions.append(manage);
    const edit = container.ownerDocument.createElement('button');
    edit.type = 'button';
    edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
    edit.dataset.permission = 'settings.tenant_dict_types.update';
    edit.dataset.tenantDictTypesEdit = dictType.id;
    edit.dataset.version = String(dictType.version ?? 0);
    edit.dataset.displayOrder = String(dictType.displayOrder ?? 0);
    edit.dataset.name = dictType.name ?? '';
    edit.dataset.description = dictType.description ?? '';
    edit.textContent = translation.t('dictTypes.edit');
    actions.append(edit);
    if (dictType.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.permission = 'settings.tenant_dict_types.disable';
      disable.dataset.tenantDictTypesDisable = dictType.id;
      disable.dataset.code = dictType.code;
      disable.textContent = translation.t('dictTypes.disable');
      actions.append(disable);
    }
    article.append(mark, identity, state, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function renderItemsDirectory(container, items, translation) {
  if (!container) return;
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.dataset.tenantDictItemsEmpty = '';
    empty.textContent = translation.t('dictItems.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-tenants__mark';
    mark.textContent = String(item.value ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const label = container.ownerDocument.createElement('strong');
    label.textContent = item.label ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = item.value ?? '';
    identity.append(label, meta);
    if (item.color) {
      const color = container.ownerDocument.createElement('small');
      color.textContent = item.color;
      identity.append(color);
    }
    const order = container.ownerDocument.createElement('small');
    order.textContent = `${translation.t('dictItems.displayOrder')}: ${item.displayOrder ?? 0}`;
    identity.append(order);
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(item.isActive ? 'dictItems.active' : 'dictItems.inactive');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-tenants__actions';
    const edit = container.ownerDocument.createElement('button');
    edit.type = 'button';
    edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
    edit.dataset.permission = 'settings.tenant_dict_types.update';
    edit.dataset.tenantDictItemsEdit = item.id;
    edit.dataset.version = String(item.version ?? 0);
    edit.dataset.displayOrder = String(item.displayOrder ?? 0);
    edit.dataset.label = item.label ?? '';
    edit.dataset.color = item.color ?? '';
    edit.textContent = translation.t('dictItems.edit');
    actions.append(edit);
    if (item.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.permission = 'settings.tenant_dict_types.disable';
      disable.dataset.tenantDictItemsDisable = item.id;
      disable.dataset.value = item.value ?? '';
      disable.textContent = translation.t('dictItems.disable');
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
      if (input) void confirm(input);
    });
    return;
  }
  const input = globalThis.prompt(title, value);
  if (input) void confirm(input);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-tenant-dict-types-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.settings_dict_type_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-tenant-dict-types-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
