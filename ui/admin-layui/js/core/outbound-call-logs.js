/**
 * 装配 Host 出站调用审计只读视图。
 */
export function createOutboundCallLogsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-outbound-call-logs-directory]');
  let loading;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/auditing/outbound-call-logs?page=1&pageSize=20')
      .then(page => {
        const items = Array.isArray(page?.items) ? page.items : [];
        renderDirectory(directory, items, translation());
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('outboundCallLogs.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  return {
    load,
    dispose() {}
  };
}

function renderDirectory(container, items, translation) {
  if (!container) return;
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('outboundCallLogs.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const title = container.ownerDocument.createElement('strong');
    title.textContent = `${item.providerKey ?? ''} / ${item.operationKey ?? ''}`;
    const status = container.ownerDocument.createElement('small');
    status.textContent = `${translation.t('outboundCallLogs.statusCode')}: ${item.statusCode ?? ''}`;
    const duration = container.ownerDocument.createElement('small');
    duration.textContent = `${translation.t('outboundCallLogs.durationMs')}: ${item.durationMs ?? ''}`;
    const occurred = container.ownerDocument.createElement('small');
    occurred.textContent = `${translation.t('outboundCallLogs.occurredAt')}: ${item.occurredAtUtc ?? ''}`;
    identity.append(title, status, duration, occurred);
    const badge = container.ownerDocument.createElement('span');
    badge.className = 'layui-badge layui-bg-gray';
    badge.textContent = item.succeeded
      ? translation.t('outboundCallLogs.succeeded')
      : translation.t('outboundCallLogs.failed');
    article.append(identity, badge);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-outbound-call-logs-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'error';
  panel.querySelector('span').textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-outbound-call-logs-problem]');
  if (panel) panel.hidden = true;
}
