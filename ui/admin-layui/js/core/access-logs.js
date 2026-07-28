import { applyAuditingAccessLogContainsDefaults } from '@fullnet/client-contracts';

/**
 * 装配 Host 访问日志只读视图。
 */
export function createAccessLogsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-access-logs-directory]');
  const loadMoreButton = root.querySelector('[data-access-logs-load-more]');
  const pathContainsInput = root.querySelector(
    '[data-access-logs-path-contains]'
  );
  const fromUtcInput = root.querySelector('[data-access-logs-from-utc]');
  const toUtcInput = root.querySelector('[data-access-logs-to-utc]');
  const searchButton = root.querySelector('[data-access-logs-search]');
  let loading;
  let items = [];
  let nextCursor = null;
  let hasMore = false;
  let activeQuery = {};
  let defaultRangeApplied = false;

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

  const search = async () => {
    activeQuery = buildQuery(
      pathContainsInput,
      fromUtcInput,
      toUtcInput
    );
    return await load();
  };

  const applyDefaults = () => {
    if (!pathContainsInput?.value.trim()) {
      if (defaultRangeApplied) {
        fromUtcInput.value = '';
        toUtcInput.value = '';
      }
      defaultRangeApplied = false;
      return;
    }

    const hadNoTimeRange = !fromUtcInput?.value && !toUtcInput?.value;
    const query = buildQuery(pathContainsInput, fromUtcInput, toUtcInput);
    if (hadNoTimeRange && query.fromUtc && query.toUtc) {
      defaultRangeApplied = true;
    }
  };

  const markTimeRangeEdited = () => {
    defaultRangeApplied = false;
  };

  const loadBatch = async (cursor, append) => {
    if (loading) return await loading;
    const parameters = new URLSearchParams();
    parameters.set('limit', '20');
    if (activeQuery.fromUtc) {
      parameters.set('fromUtc', activeQuery.fromUtc);
    }
    if (activeQuery.toUtc) {
      parameters.set('toUtc', activeQuery.toUtc);
    }
    if (activeQuery.pathContains) {
      parameters.set('pathContains', activeQuery.pathContains);
    }
    if (cursor) {
      parameters.set('cursor', cursor);
    }
    loading = request(
      `/api/v1/auditing/access-logs/cursor?${parameters.toString()}`
    )
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
  pathContainsInput?.addEventListener('input', applyDefaults);
  fromUtcInput?.addEventListener('input', markTimeRangeEdited);
  toUtcInput?.addEventListener('input', markTimeRangeEdited);
  searchButton?.addEventListener('click', search);
  updateLoadMoreButton(loadMoreButton, false);

  return {
    load,
    dispose() {
      loadMoreButton?.removeEventListener('click', loadMore);
      pathContainsInput?.removeEventListener('input', applyDefaults);
      fromUtcInput?.removeEventListener('input', markTimeRangeEdited);
      toUtcInput?.removeEventListener('input', markTimeRangeEdited);
      searchButton?.removeEventListener('click', search);
    }
  };
}

function buildQuery(pathInput, fromInput, toInput) {
  const query = applyAuditingAccessLogContainsDefaults({
    pathContains: pathInput?.value,
    fromUtc: toUtcIso(fromInput?.value),
    toUtc: toUtcIso(toInput?.value)
  });
  applyVisibleDefaults(query, fromInput, toInput);
  return query;
}

function applyVisibleDefaults(query, fromInput, toInput) {
  if (query.fromUtc && fromInput && !fromInput.value) {
    fromInput.value = toDateTimeLocal(query.fromUtc);
  }
  if (query.toUtc && toInput && !toInput.value) {
    toInput.value = toDateTimeLocal(query.toUtc);
  }
}

function toUtcIso(value) {
  if (!value) return undefined;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

function toDateTimeLocal(value) {
  const parsed = new Date(value);
  const local = new Date(
    parsed.getTime() - parsed.getTimezoneOffset() * 60_000
  );
  return local.toISOString().slice(0, 16);
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
