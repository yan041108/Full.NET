/**
 * 装配 Host 限时诊断策略只读视图；写权限可恢复生产安全默认。
 * 禁止自由填写 Sink、索引名或 Metrics 标签。
 */
export function createDiagnosticPolicyController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const hasPermission = options.hasPermission;
  const directory = root.querySelector('[data-diagnostic-policy-directory]');
  const restoreButton = root.querySelector('[data-diagnostic-policy-restore]');
  let loading;
  let changing = false;
  let currentVersion = 0;

  const canWrite = () =>
    typeof hasPermission === 'function'
      ? hasPermission('settings.diagnostic_policy.write')
      : false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/settings/diagnostic-policy')
      .then(policy => {
        currentVersion = Number(policy?.configEntryVersion ?? 0);
        renderDirectory(directory, policy, translation());
        if (restoreButton) {
          restoreButton.hidden = !canWrite();
          restoreButton.disabled = !canWrite();
        }
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('diagnosticPolicy.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onRestore = async event => {
    event.preventDefault();
    if (changing || !canWrite()) return;
    changing = true;
    try {
      const restored = await request('/api/v1/settings/diagnostic-policy/restore', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ configEntryVersion: currentVersion })
      });
      currentVersion = Number(restored?.configEntryVersion ?? 0);
      renderDirectory(directory, restored, translation());
      notify(translation().t('diagnosticPolicy.restoreSuccess'), 1);
    } catch (problem) {
      showProblem(root, problem, translation().t('diagnosticPolicy.operationFailed'));
    } finally {
      changing = false;
    }
  };

  restoreButton?.addEventListener('click', onRestore);
  void load();

  return {
    destroy() {
      restoreButton?.removeEventListener('click', onRestore);
    }
  };
}

function renderDirectory(directory, policy, translation) {
  if (!directory) return;
  directory.replaceChildren();
  const summary = document.createElement('article');
  summary.className = 'fn-tenants__item';
  const title = document.createElement('strong');
  title.textContent = translation.t('diagnosticPolicy.pressureLabel');
  const pressure = document.createElement('code');
  pressure.textContent = policy?.pressureState ?? 'Normal';
  const meta = document.createElement('p');
  meta.textContent = translation.t('diagnosticPolicy.summary', {
    version: String(policy?.version ?? 0),
    configEntryVersion: String(policy?.configEntryVersion ?? 0),
    ruleCount: String(policy?.activeRules?.length ?? 0)
  });
  const state = document.createElement('span');
  state.dataset.diagnosticPolicyState = policy?.isDefault ? 'default' : 'active';
  state.textContent = policy?.isDefault
    ? translation.t('diagnosticPolicy.defaultState')
    : translation.t('diagnosticPolicy.activeState');
  summary.append(title, document.createTextNode(' '), pressure, meta, state);
  directory.append(summary);

  const rules = Array.isArray(policy?.activeRules) ? policy.activeRules : [];
  if (rules.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'layui-word-aux';
    empty.textContent = translation.t('diagnosticPolicy.emptyRules');
    directory.append(empty);
    return;
  }

  for (const rule of rules) {
    const item = document.createElement('article');
    item.className = 'fn-tenants__item';
    const heading = document.createElement('strong');
    heading.textContent = `${rule.scopeKind}=${rule.scopeValue}`;
    const expires = document.createElement('p');
    expires.textContent = translation.t('diagnosticPolicy.expiresAt', {
      expiresAtUtc: rule.expiresAtUtc ?? ''
    });
    item.append(heading, expires);
    directory.append(item);
  }
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-diagnostic-policy-problem]');
  if (!panel) return;
  panel.hidden = false;
  const strong = panel.querySelector('strong');
  const span = panel.querySelector('span');
  if (strong) strong.textContent = problem?.code ?? 'error';
  if (span) span.textContent = problem?.detail ?? problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-diagnostic-policy-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  if (globalThis.layui?.layer?.msg) {
    globalThis.layui.layer.msg(message, { icon });
  }
}
