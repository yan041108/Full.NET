/**
 * 装配超级管理员管理视图；只使用安全 DOM API，密码与 TOTP 仅在单次请求闭包内短暂存在。
 */
export function createSuperAdministratorController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-super-admin-grant-form]');
  const directory = root.querySelector('[data-super-admin-directory]');
  const audit = root.querySelector('[data-super-admin-audits]');
  const totpStatus = root.querySelector('[data-super-admin-totp-status]');
  const totpPending = root.querySelector('[data-super-admin-totp-pending]');
  const totpBegin = root.querySelector('[data-super-admin-totp-begin]');
  const totpConfirm = root.querySelector('[data-super-admin-totp-confirm]');
  const totpSecret = root.querySelector('[data-super-admin-totp-secret]');
  const totpUri = root.querySelector('[data-super-admin-totp-uri]');
  const enrollCode = root.querySelector('[name="enrollTotpCode"]');
  let loading;
  let changing = false;
  let enrolling = false;
  let enabled = false;

  const load = async () => {
    if (loading) return await loading;
    loading = Promise.all([
      request('/api/v1/identity/super-administrators/'),
      request('/api/v1/identity/super-administrators/audits?limit=50'),
      request('/api/v1/identity/me/mfa/totp/').catch(() => ({
        isEnrolled: false,
        isEnabled: false
      }))
    ]).then(([administrators, audits, status]) => {
      renderDirectory(directory, Array.isArray(administrators) ? administrators : [], translation());
      renderAudits(audit, Array.isArray(audits) ? audits : [], translation());
      renderTotpStatus(status);
      hideProblem(root);
    }).catch(problem => {
      showProblem(root, problem, translation().t('superAdmin.loadFailed'));
    }).finally(() => { loading = undefined; });
    return await loading;
  };

  const renderTotpStatus = status => {
    enabled = Boolean(status?.isEnabled);
    if (totpStatus) {
      totpStatus.textContent = translation().t(
        enabled ? 'superAdmin.totpEnabled' : 'superAdmin.totpDisabled'
      );
    }
    if (totpBegin) {
      totpBegin.disabled = enabled;
      totpBegin.textContent = translation().t(
        enabled ? 'superAdmin.totpEnabled' : 'superAdmin.totpBegin'
      );
      totpBegin.hidden = Boolean(totpPending && !totpPending.hidden);
    }
    if (enabled && totpPending) {
      totpPending.hidden = true;
      if (totpSecret) totpSecret.textContent = '';
      if (totpUri) totpUri.textContent = '';
      if (enrollCode) enrollCode.value = '';
      if (totpBegin) totpBegin.hidden = false;
    }
  };

  const onBeginTotp = async () => {
    if (enrolling || enabled) return;
    enrolling = true;
    try {
      const began = await request('/api/v1/identity/me/mfa/totp/begin', { method: 'POST' });
      if (totpSecret) totpSecret.textContent = began?.sharedSecretBase32 ?? '';
      if (totpUri) totpUri.textContent = began?.otpAuthUri ?? '';
      if (totpPending) totpPending.hidden = false;
      if (totpBegin) totpBegin.hidden = true;
      notify(translation().t('superAdmin.totpBeginSuccess'), 1);
    } catch (problem) {
      showProblem(root, problem, translation().t('superAdmin.operationFailed'));
    } finally {
      enrolling = false;
    }
  };

  const onConfirmTotp = async () => {
    if (enrolling) return;
    const code = String(enrollCode?.value ?? '').trim();
    if (!code) return;
    enrolling = true;
    try {
      const status = await request(
        '/api/v1/identity/me/mfa/totp/confirm',
        jsonRequest({ totpCode: code })
      );
      renderTotpStatus(status);
      notify(translation().t('superAdmin.totpConfirmSuccess'), 1);
    } catch (problem) {
      showProblem(root, problem, translation().t('superAdmin.operationFailed'));
    } finally {
      enrolling = false;
    }
  };

  const onGrant = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const username = String(data.get('username') ?? '').trim();
    const currentPassword = String(data.get('currentPassword') ?? '');
    const totpCode = String(data.get('totpCode') ?? '').trim();
    if (!username || !currentPassword) return;
    changing = true;
    try {
      await request('/api/v1/identity/super-administrators/grant', jsonRequest({
        username,
        currentPassword,
        ...(totpCode ? { totpCode } : {})
      }));
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
    }), currentPassword => {
      askOptionalTotp(translation().t('superAdmin.confirmRevokeTotp'), async totpCode => {
        changing = true;
        try {
          await request(
            `/api/v1/identity/super-administrators/${encodeURIComponent(button.dataset.superAdminRevoke)}/revoke`,
            jsonRequest({
              currentPassword,
              ...(totpCode ? { totpCode } : {})
            })
          );
          notify(translation().t('superAdmin.revokeSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('superAdmin.operationFailed'));
        } finally {
          changing = false;
        }
      });
    });
  };

  form?.addEventListener('submit', onGrant);
  directory?.addEventListener('click', onRevoke);
  totpBegin?.addEventListener('click', onBeginTotp);
  totpConfirm?.addEventListener('click', onConfirmTotp);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onGrant);
      directory?.removeEventListener('click', onRevoke);
      totpBegin?.removeEventListener('click', onBeginTotp);
      totpConfirm?.removeEventListener('click', onConfirmTotp);
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

function askOptionalTotp(title, confirm) {
  if (!globalThis.layui?.layer?.prompt) {
    void confirm('');
    return;
  }
  globalThis.layui.layer.prompt({ title, formType: 0 }, (value, index) => {
    globalThis.layui.layer.close(index);
    void confirm(String(value ?? '').trim());
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
