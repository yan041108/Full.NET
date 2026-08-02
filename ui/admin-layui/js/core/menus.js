import {
  HOST_MENU_ASSIGNABLE_PERMISSIONS,
  HOST_MENU_COMPONENT_OPTIONS
} from '@fullnet/client-contracts';
import { applyPermissionVisibility } from './navigation.js';

/**
 * 装配 Host 菜单管理视图；系统菜单只读，自定义菜单支持标题更新与禁用。
 */
export function createMenusController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-menus-create-form]');
  const directory = root.querySelector('[data-menus-directory]');
  const componentSelect = form?.querySelector('[name="componentKey"]');
  const pathInput = form?.querySelector('[name="path"]');
  let loading;
  let changing = false;

  const syncPathFromComponent = () => {
    if (!componentSelect || !pathInput) return;
    const entry = HOST_MENU_COMPONENT_OPTIONS.find(
      option => option.componentKey === componentSelect.value
    );
    pathInput.value = entry?.path ?? '/';
  };

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/identity/menus?page=1&pageSize=20')
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
        showProblem(root, problem, translation().t('menus.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const routeName = String(data.get('routeName') ?? '').trim();
    const title = String(data.get('title') ?? '').trim();
    const componentKey = String(data.get('componentKey') ?? '').trim();
    const path = String(data.get('path') ?? '').trim();
    const requiredPermission = String(data.get('requiredPermission') ?? '').trim();
    if (!routeName || !title || !componentKey || !path || !requiredPermission) return;
    changing = true;
    try {
      await request('/api/v1/identity/menus', jsonRequest({
        parentId: null,
        routeName,
        path,
        componentKey,
        title,
        caption: title,
        icon: 'grid',
        displayOrder: 50,
        requiredPermission
      }));
      form.reset();
      syncPathFromComponent();
      notify(translation().t('menus.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('menus.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-menus-edit]')
      : undefined;
    if (editButton && !changing) {
      const menuId = editButton.dataset.menusEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const currentTitle = editButton.dataset.title ?? '';
      promptText(translation().t('menus.editTitle'), currentTitle, async nextTitle => {
        if (!nextTitle.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/identity/menus/${encodeURIComponent(menuId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                parentId: editButton.dataset.parentId || null,
                path: editButton.dataset.path,
                componentKey: editButton.dataset.componentKey,
                title: nextTitle.trim(),
                caption: editButton.dataset.caption,
                icon: editButton.dataset.icon,
                displayOrder: Number(editButton.dataset.displayOrder ?? '0'),
                requiredPermission: editButton.dataset.requiredPermission,
                version
              })
            }
          );
          notify(translation().t('menus.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('menus.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-menus-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const menuId = disableButton.dataset.menusDisable;
    const routeLabel = disableButton.dataset.routeName ?? '';
    const message = translation().t('menus.confirmDisable', { name: routeLabel });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/identity/menus/${encodeURIComponent(menuId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('menus.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('menus.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  syncPathFromComponent();
  form?.addEventListener('submit', onCreate);
  componentSelect?.addEventListener('change', syncPathFromComponent);
  directory?.addEventListener('click', onDirectoryAction);

  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      componentSelect?.removeEventListener('change', syncPathFromComponent);
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

function renderDirectory(container, menus, translation) {
  if (!container) return;
  if (menus.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-menus__empty';
    empty.textContent = translation.t('menus.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  menus.forEach(menu => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-menus__mark';
    mark.textContent = String(menu.routeName ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const title = container.ownerDocument.createElement('strong');
    title.textContent = menu.title ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = `${menu.routeName ?? ''} · ${menu.componentKey ?? ''}`;
    identity.append(title, meta);
    const state = container.ownerDocument.createElement('em');
    const tags = [];
    if (menu.isSystem) tags.push(translation.t('menus.system'));
    tags.push(translation.t(menu.isActive ? 'menus.active' : 'menus.inactive'));
    state.textContent = tags.join(' · ');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-menus__actions';
    if (!menu.isSystem) {
      const edit = container.ownerDocument.createElement('button');
      edit.type = 'button';
      edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
      edit.dataset.menusEdit = menu.id;
      edit.dataset.permission = 'identity.menus.update';
      edit.dataset.version = String(menu.version ?? 0);
      edit.dataset.title = menu.title ?? '';
      edit.dataset.parentId = menu.parentId ?? '';
      edit.dataset.path = menu.path ?? '';
      edit.dataset.componentKey = menu.componentKey ?? '';
      edit.dataset.caption = menu.caption ?? '';
      edit.dataset.icon = menu.icon ?? 'grid';
      edit.dataset.displayOrder = String(menu.displayOrder ?? 0);
      edit.dataset.requiredPermission = menu.requiredPermission ?? '';
      edit.textContent = translation.t('menus.edit');
      actions.append(edit);
    }
    if (menu.isActive && !menu.isSystem) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.menusDisable = menu.id;
      disable.dataset.permission = 'identity.menus.disable';
      disable.dataset.routeName = menu.routeName;
      disable.textContent = translation.t('menus.disable');
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
  const panel = root.querySelector('[data-menus-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.host_menu_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-menus-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}

export { HOST_MENU_ASSIGNABLE_PERMISSIONS };
