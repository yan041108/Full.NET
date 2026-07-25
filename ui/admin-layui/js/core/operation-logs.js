/**
 * 装配 Host 操作日志只读视图。
 */
export function createOperationLogsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-operation-logs-directory]');
  let loading;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/auditing/operation-logs?page=1&pageSize=20')
      .then(page => {
        const items = Array.isArray(page?.items) ? page.items : [];
        renderDirectory(directory, items, translation());
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('operationLogs.loadFailed'));
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
    empty.textContent = translation.t('operationLogs.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const title = container.ownerDocument.createElement('strong');
    title.textContent = item.actionKey ?? '';
    const status = container.ownerDocument.createElement('small');
    status.textContent = `${translation.t('operationLogs.statusCode')}: ${item.statusCode ?? ''}`;
    const duration = container.ownerDocument.createElement('small');
    duration.textContent = `${translation.t('operationLogs.durationMs')}: ${item.durationMs ?? ''}`;
    const occurred = container.ownerDocument.createElement('small');
    occurred.textContent = `${translation.t('operationLogs.occurredAt')}: ${item.occurredAtUtc ?? ''}`;
    identity.append(title, status, duration, occurred);
    const badge = container.ownerDocument.createElement('span');
    badge.className = 'layui-badge layui-bg-gray';
    badge.textContent = item.succeeded
      ? translation.t('operationLogs.succeeded')
      : translation.t('operationLogs.failed');
    article.append(identity, badge);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-operation-logs-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'error';
  panel.querySelector('span').textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-operation-logs-problem]');
  if (panel) panel.hidden = true;
}
