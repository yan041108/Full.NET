/**
 * 装配 Host 访问日志只读视图。
 */
export function createAccessLogsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-access-logs-directory]');
  let loading;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/auditing/access-logs?page=1&pageSize=20')
      .then(page => {
        const items = Array.isArray(page?.items) ? page.items : [];
        renderDirectory(directory, items, translation());
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('accessLogs.loadFailed'));
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
    empty.textContent = translation.t('accessLogs.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const title = container.ownerDocument.createElement('strong');
    title.textContent = `${item.httpMethod ?? ''} ${item.requestPath ?? ''}`;
    const status = container.ownerDocument.createElement('small');
    status.textContent = `${translation.t('accessLogs.statusCode')}: ${item.statusCode ?? ''}`;
    const duration = container.ownerDocument.createElement('small');
    duration.textContent = `${translation.t('accessLogs.durationMs')}: ${item.durationMs ?? ''}`;
    const occurred = container.ownerDocument.createElement('small');
    occurred.textContent = `${translation.t('accessLogs.occurredAt')}: ${item.occurredAtUtc ?? ''}`;
    identity.append(title, status, duration, occurred);
    const badge = container.ownerDocument.createElement('span');
    badge.className = 'layui-badge layui-bg-gray';
    badge.textContent = item.isAuthenticated
      ? translation.t('accessLogs.authenticated')
      : translation.t('accessLogs.anonymous');
    article.append(identity, badge);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-access-logs-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'error';
  panel.querySelector('span').textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-access-logs-problem]');
  if (panel) panel.hidden = true;
}
