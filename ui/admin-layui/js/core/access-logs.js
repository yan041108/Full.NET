/**
 * 装配 Host 访问日志只读视图。
 */
export function createAccessLogsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-access-logs-directory]');
  const loadMoreButton = root.querySelector('[data-access-logs-load-more]');
  let loading;
  let items = [];
  let nextCursor = null;
  let hasMore = false;

  const load = async () => {
    // 路由或会话刷新必须在当前追加结束后重新读取首批，避免旧追加覆盖新状态。
    if (loading) await loading;
    items = [];
    nextCursor = null;
    hasMore = false;
    return await loadBatch(null, false);
  };

  const loadMore = async () => {
    if (!hasMore || !nextCursor) return;
    return await loadBatch(nextCursor, true);
  };

  const loadBatch = async (cursor, append) => {
    if (loading) return await loading;
    const cursorQuery = cursor
      ? `&cursor=${encodeURIComponent(cursor)}`
      : '';
    loading = request(`/api/v1/auditing/access-logs/cursor?limit=20${cursorQuery}`)
      .then(page => {
        const batch = Array.isArray(page?.items) ? page.items : [];
        items = append ? [...items, ...batch] : batch;
        nextCursor = typeof page?.nextCursor === 'string'
          ? page.nextCursor
          : null;
        hasMore = page?.hasMore === true && nextCursor !== null;
        renderDirectory(directory, items, translation());
        updateLoadMoreButton(loadMoreButton, hasMore);
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('accessLogs.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  loadMoreButton?.addEventListener('click', loadMore);
  updateLoadMoreButton(loadMoreButton, false);

  return {
    load,
    dispose() {
      loadMoreButton?.removeEventListener('click', loadMore);
    }
  };
}

function updateLoadMoreButton(button, hasMore) {
  if (button) button.hidden = !hasMore;
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
