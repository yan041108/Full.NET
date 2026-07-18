/**
 * 装配超级管理员管理视图；只使用安全 DOM API，密码仅在单次请求闭包内短暂存在。
 */
export function createSuperAdministratorController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-super-admin-grant-form]');
  const directory = root.querySelector('[data-super-admin-directory]');
  const audit = root.querySelector('[data-super-admin-audits]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = Promise.all([
      request('/api/v1/identity/super-administrators/'),
      request('/api/v1/identity/super-administrators/audits?limit=50')
    ]).then(([administrators, audits]) => {
      renderDirectory(directory, Array.isArray(administrators) ? administrators : [], translation());
      renderAudits(audit, Array.isArray(audits) ? audits : [], translation());
      hideProblem(root);
    }).catch(problem => {
      showProblem(root, problem, translation().t('superAdmin.loadFailed'));
    }).finally(() => { loading = undefined; });
    return await loading;
  };

  const onGrant = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const username = String(data.get('username') ?? '').trim();
    const currentPassword = String(data.get('currentPassword') ?? '');
    if (!username || !currentPassword) return;
    changing = true;
    try {
      await request('/api/v1/identity/super-administrators/grant', jsonRequest({ username, currentPassword }));
      form.reset();
      notify(translation().t('superAdmin.grantSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('superAdmin.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onRevoke = event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-super-admin-revoke]')
      : undefined;
    if (!button || changing) return;
    askPassword(translation().t('superAdmin.confirmRevoke', {
      name: button.dataset.username ?? ''
    }), async currentPassword => {
      changing = true;
      try {
        await request(
          `/api/v1/identity/super-administrators/${encodeURIComponent(button.dataset.superAdminRevoke)}/revoke`,
          jsonRequest({ currentPassword })
        );
        notify(translation().t('superAdmin.revokeSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('superAdmin.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onGrant);
  directory?.addEventListener('click', onRevoke);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onGrant);
      directory?.removeEventListener('click', onRevoke);
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

function renderDirectory(container, administrators, translation) {
  if (!container) return;
  const fragment = container.ownerDocument.createDocumentFragment();
  administrators.forEach(administrator => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.textContent = String(administrator.username ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = administrator.displayName ?? '';
    const username = container.ownerDocument.createElement('code');
    username.textContent = administrator.username ?? '';
    identity.append(name, username);
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(administrator.isActive
      ? 'superAdmin.active'
      : 'superAdmin.inactive');
    const revoke = container.ownerDocument.createElement('button');
    revoke.type = 'button';
    revoke.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
    revoke.dataset.superAdminRevoke = administrator.userId;
    revoke.dataset.username = administrator.username;
    revoke.textContent = translation.t('superAdmin.revoke');
    article.append(mark, identity, state, revoke);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function renderAudits(container, audits, translation) {
  if (!container) return;
  if (audits.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.textContent = translation.t('superAdmin.emptyAudit');
    container.replaceChildren(empty);
    return;
  }
  const fragment = container.ownerDocument.createDocumentFragment();
  audits.forEach(item => {
    const row = container.ownerDocument.createElement('li');
    const time = container.ownerDocument.createElement('time');
    time.textContent = new Date(item.occurredAtUtc).toLocaleString(translation.locale);
    const event = container.ownerDocument.createElement('strong');
    event.textContent = item.eventType;
    const relation = container.ownerDocument.createElement('code');
    relation.textContent = `${item.actorUserId ?? 'system'} → ${item.targetUserId}`;
    row.append(time, event, relation);
    fragment.append(row);
  });
  container.replaceChildren(fragment);
}

function askPassword(title, confirm) {
  if (!globalThis.layui?.layer?.prompt) {
    // 浏览器原生 prompt 无法隐藏密码；Layer 不可用时禁止降级执行高风险操作。
    return;
  }
  globalThis.layui.layer.prompt({ title, formType: 1 }, (value, index) => {
    globalThis.layui.layer.close(index);
    if (value) void confirm(value);
  });
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-super-admin-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.super_administrator_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-super-admin-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
