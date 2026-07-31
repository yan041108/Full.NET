/**
 * 装配 Host 用户管理视图；禁用操作需经 Layer 确认，避免误触立即撤销会话。
 */
export function createUsersController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-users-create-form]');
  const directory = root.querySelector('[data-users-directory]');
  const exportButton = root.querySelector('[data-users-export]');
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

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-users-edit]')
      : undefined;
    if (editButton && !changing) {
      const userId = editButton.dataset.usersEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const currentName = editButton.dataset.displayName ?? '';
      promptText(translation().t('users.editTitle'), currentName, async displayName => {
        if (!displayName.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/identity/users/${encodeURIComponent(userId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ displayName: displayName.trim(), version })
            }
          );
          notify(translation().t('users.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('users.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-users-disable]')
      : undefined;
    if (disableButton && !changing) {
      const userId = disableButton.dataset.usersDisable;
      const username = disableButton.dataset.username ?? '';
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
      return;
    }

    const enableButton = event.target instanceof Element
      ? event.target.closest('[data-users-enable]')
      : undefined;
    if (enableButton && !changing) {
      const userId = enableButton.dataset.usersEnable;
      const username = enableButton.dataset.username ?? '';
      const message = translation().t('users.confirmEnable', { name: username });
      confirmAction(message, async () => {
        changing = true;
        try {
          await request(
            `/api/v1/identity/users/${encodeURIComponent(userId)}/enable`,
            { method: 'POST' }
          );
          notify(translation().t('users.enableSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('users.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const rolesButton = event.target instanceof Element
      ? event.target.closest('[data-users-roles]')
      : undefined;
    if (rolesButton && !changing) {
      void openRolesDialog(
        rolesButton.dataset.usersRoles,
        translation(),
        request,
        async (roleIds, version) => {
          changing = true;
          try {
            await request(
              `/api/v1/identity/users/${encodeURIComponent(rolesButton.dataset.usersRoles)}/roles`,
              {
                method: 'PUT',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ roleIds, version })
              }
            );
            notify(translation().t('users.rolesSuccess'), 1);
            await load();
          } catch (problem) {
            showProblem(root, problem, translation().t('users.operationFailed'));
          } finally {
            changing = false;
          }
        }
      );
      return;
    }

    const resetButton = event.target instanceof Element
      ? event.target.closest('[data-users-reset-password]')
      : undefined;
    if (resetButton && !changing) {
      const userId = resetButton.dataset.usersResetPassword;
      promptPassword(translation().t('users.resetPasswordTitle'), async password => {
        if (!password) return;
        changing = true;
        try {
          await request(
            `/api/v1/identity/users/${encodeURIComponent(userId)}/reset-password`,
            {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ password })
            }
          );
          notify(translation().t('users.resetPasswordSuccess'), 1);
        } catch (problem) {
          showProblem(root, problem, translation().t('users.operationFailed'));
        } finally {
          changing = false;
        }
      });
    }
  };

  const onExport = async () => {
    if (changing) return;
    changing = true;
    try {
      const rows = await request('/api/v1/identity/users/export');
      const blob = new Blob([JSON.stringify(rows, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'host-users.json';
      link.click();
      URL.revokeObjectURL(url);
    } catch (problem) {
      showProblem(root, problem, translation().t('users.operationFailed'));
    } finally {
      changing = false;
    }
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDirectoryAction);
  exportButton?.addEventListener('click', onExport);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDirectoryAction);
      exportButton?.removeEventListener('click', onExport);
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
    const projected = user.projectedFields;
    if (Array.isArray(projected?.effectiveFieldKeys)) {
      const details = container.ownerDocument.createElement('small');
      const values = [];
      if (projected.effectiveFieldKeys.includes('preferred_locale')) {
        values.push(`locale: ${projected.preferredLocale ?? '—'}`);
      }
      if (projected.effectiveFieldKeys.includes('failed_login_count')) {
        values.push(`failed-login: ${projected.failedLoginCount ?? 0}`);
      }
      if (projected.effectiveFieldKeys.includes('lockout_end_utc')) {
        values.push(`lockout: ${projected.lockoutEndUtc ?? '—'}`);
      }
      details.textContent = values.join(' · ');
      identity.append(details);
    }
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(user.isActive ? 'users.active' : 'users.inactive');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-users__actions';
    const edit = container.ownerDocument.createElement('button');
    edit.type = 'button';
    edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
    edit.dataset.usersEdit = user.id;
    edit.dataset.version = String(user.version ?? 0);
    edit.dataset.displayName = user.displayName ?? '';
    edit.textContent = translation.t('users.edit');
    const roles = container.ownerDocument.createElement('button');
    roles.type = 'button';
    roles.className = 'layui-btn layui-btn-primary layui-btn-sm';
    roles.dataset.usersRoles = user.id;
    roles.dataset.version = String(user.version ?? 0);
    roles.textContent = translation.t('users.roles');
    actions.append(roles, edit);
    if (user.isActive) {
      const resetPassword = container.ownerDocument.createElement('button');
      resetPassword.type = 'button';
      resetPassword.className = 'layui-btn layui-btn-primary layui-btn-sm';
      resetPassword.dataset.usersResetPassword = user.id;
      resetPassword.textContent = translation.t('users.resetPassword');
      actions.append(resetPassword);
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.usersDisable = user.id;
      disable.dataset.username = user.username;
      disable.textContent = translation.t('users.disable');
      actions.append(disable);
    } else {
      const enable = container.ownerDocument.createElement('button');
      enable.type = 'button';
      enable.className = 'layui-btn layui-btn-normal layui-btn-primary layui-btn-sm';
      enable.dataset.usersEnable = user.id;
      enable.dataset.username = user.username;
      enable.textContent = translation.t('users.enable');
      actions.append(enable);
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

function promptPassword(title, confirm) {
  if (globalThis.layui?.layer?.prompt) {
    globalThis.layui.layer.prompt({ title, formType: 1 }, (input, index) => {
      globalThis.layui.layer.close(index);
      if (input) void confirm(input);
    });
    return;
  }
  const input = globalThis.prompt(title);
  if (input) void confirm(input);
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

function openRolesDialog(userId, translation, request, confirm) {
  return Promise.all([
    request('/api/v1/identity/roles?page=1&pageSize=20'),
    request(`/api/v1/identity/users/${encodeURIComponent(userId)}/roles`)
  ]).then(([rolesPage, userRoles]) => {
    const assignableRoles = (Array.isArray(rolesPage?.items) ? rolesPage.items : [])
      .filter(role => role.isActive && !role.isSystem && !role.isSuperAdministrator);
    const selected = new Set(
      Array.isArray(userRoles?.roleIds) ? userRoles.roleIds.map(String) : []
    );
    const rolesVersion = userRoles?.version ?? 0;
    const checkboxes = assignableRoles.map(role => (
      `<label><input type="checkbox" value="${role.id}"${selected.has(String(role.id)) ? ' checked' : ''}> ${role.name} <code>${role.code}</code></label>`
    )).join('');
    const html = `<div class="fn-users__roles-dialog">${checkboxes || `<p>${translation.t('users.emptyDirectory')}</p>`}</div>`;

    if (!globalThis.layui?.layer?.open) {
      const fallback = document.createElement('div');
      fallback.innerHTML = html;
      const roleIds = [...fallback.querySelectorAll('input:checked')].map(input => input.value);
      void confirm(roleIds);
      return;
    }

    globalThis.layui.layer.open({
      type: 1,
      title: translation.t('users.rolesTitle'),
      area: ['520px', '420px'],
      content: html,
      btn: [translation.t('users.saveRoles'), translation.t('status.back')],
      yes(index, layero) {
        const dialogRoot = resolveLayerContent(layero, '.fn-users__roles-dialog');
        const roleIds = [...(dialogRoot?.querySelectorAll('input:checked') ?? [])]
          .map(input => input.value)
          .sort();
        globalThis.layui.layer.close(index);
        void confirm(roleIds, rolesVersion);
      }
    });
  });
}

function resolveLayerContent(layero, selector) {
  if (layero && typeof layero.find === 'function') {
    return layero.find(selector)[0];
  }
  if (layero instanceof Element) {
    return layero.querySelector(selector) ?? layero;
  }
  const root = layero?.[0];
  return root?.querySelector?.(selector) ?? root;
}
