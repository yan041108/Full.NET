import { HOST_ROLE_ASSIGNABLE_PERMISSIONS } from '@fullnet/client-contracts';

/**
 * 装配 Host 角色管理视图；系统角色只读，自定义角色支持权限替换。
 */
export function createRolesController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-roles-create-form]');
  const directory = root.querySelector('[data-roles-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/identity/roles?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('roles.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const code = String(data.get('code') ?? '').trim();
    const name = String(data.get('name') ?? '').trim();
    if (!code || !name) return;
    changing = true;
    try {
      await request('/api/v1/identity/roles', jsonRequest({ code, name }));
      form.reset();
      notify(translation().t('roles.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('roles.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-roles-edit]')
      : undefined;
    if (editButton && !changing) {
      const roleId = editButton.dataset.rolesEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const currentName = editButton.dataset.name ?? '';
      promptText(translation().t('roles.editTitle'), currentName, async nextName => {
        if (!nextName.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/identity/roles/${encodeURIComponent(roleId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ name: nextName.trim(), version })
            }
          );
          notify(translation().t('roles.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('roles.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const permissionsButton = event.target instanceof Element
      ? event.target.closest('[data-roles-permissions]')
      : undefined;
    if (permissionsButton && !changing) {
      openPermissionsDialog(
        permissionsButton.dataset.rolesPermissions,
        Number(permissionsButton.dataset.version ?? '0'),
        permissionsButton.dataset.permissionCodes ?? '',
        translation(),
        async permissionCodes => {
          changing = true;
          try {
            await request(
              `/api/v1/identity/roles/${encodeURIComponent(permissionsButton.dataset.rolesPermissions)}/permissions`,
              {
                method: 'PUT',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({
                  permissionCodes,
                  version: Number(permissionsButton.dataset.version ?? '0')
                })
              }
            );
            notify(translation().t('roles.permissionsSuccess'), 1);
            await load();
          } catch (problem) {
            showProblem(root, problem, translation().t('roles.operationFailed'));
          } finally {
            changing = false;
          }
        }
      );
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-roles-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const roleId = disableButton.dataset.rolesDisable;
    const code = disableButton.dataset.code ?? '';
    const message = translation().t('roles.confirmDisable', { name: code });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/identity/roles/${encodeURIComponent(roleId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('roles.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('roles.operationFailed'));
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

function renderDirectory(container, roles, translation) {
  if (!container) return;
  if (roles.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-roles__empty';
    empty.textContent = translation.t('roles.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  roles.forEach(role => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-roles__mark';
    mark.textContent = String(role.code ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = role.name ?? '';
    const code = container.ownerDocument.createElement('code');
    code.textContent = role.code ?? '';
    identity.append(name, code);
    const state = container.ownerDocument.createElement('em');
    const tags = [];
    if (role.isSystem) tags.push(translation.t('roles.system'));
    tags.push(translation.t(role.isActive ? 'roles.active' : 'roles.inactive'));
    state.textContent = tags.join(' · ');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-roles__actions';
    if (!role.isSystem) {
      const edit = container.ownerDocument.createElement('button');
      edit.type = 'button';
      edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
      edit.dataset.rolesEdit = role.id;
      edit.dataset.version = String(role.version ?? 0);
      edit.dataset.name = role.name ?? '';
      edit.textContent = translation.t('roles.edit');
      actions.append(edit);
      const permissions = container.ownerDocument.createElement('button');
      permissions.type = 'button';
      permissions.className = 'layui-btn layui-btn-primary layui-btn-sm';
      permissions.dataset.rolesPermissions = role.id;
      permissions.dataset.version = String(role.version ?? 0);
      permissions.dataset.permissionCodes = (role.permissionCodes ?? []).join(',');
      permissions.textContent = translation.t('roles.permissions');
      actions.append(permissions);
    }
    if (role.isActive && !role.isSystem) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.rolesDisable = role.id;
      disable.dataset.code = role.code;
      disable.textContent = translation.t('roles.disable');
      actions.append(disable);
    }
    article.append(mark, identity, state, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function openPermissionsDialog(roleId, version, initialPermissionCodes, translation, confirm) {
  const selected = new Set(
    String(initialPermissionCodes)
      .split(',')
      .map(code => code.trim())
      .filter(Boolean)
  );
  const content = document.createElement('div');
  content.className = 'fn-roles__permission-dialog';
  HOST_ROLE_ASSIGNABLE_PERMISSIONS.forEach(code => {
    const label = document.createElement('label');
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.value = code;
    input.checked = selected.has(code);
    label.append(input, document.createTextNode(` ${code}`));
    content.append(label);
  });

  if (globalThis.layui?.layer?.open) {
    globalThis.layui.layer.open({
      type: 1,
      title: translation.t('roles.permissionsTitle'),
      area: ['520px', '420px'],
      content,
      btn: [translation.t('roles.savePermissions'), translation.t('status.back')],
      yes(index) {
        const permissionCodes = [...content.querySelectorAll('input:checked')]
          .map(input => input.value)
          .sort();
        globalThis.layui.layer.close(index);
        void confirm(permissionCodes);
      }
    });
    return;
  }

  const fallbackPermissionCodes = [...selected];
  void confirm(fallbackPermissionCodes);
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
  const panel = root.querySelector('[data-roles-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.host_role_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-roles-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
