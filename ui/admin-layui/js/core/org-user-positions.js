/**
 * 装配租户用户-职位隶属视图；支持分配、设主职位与取消隶属。
 */
export function createOrgUserPositionsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const canWrite = () => options.hasPermission?.(
    'organization.user_positions.write'
  ) === true;
  const form = root.querySelector('[data-org-user-positions-create-form]');
  const directory = root.querySelector('[data-org-user-positions-directory]');
  const userSelect = root.querySelector('[data-org-user-positions-user]');
  const loadMoreUsersButton = root.querySelector(
    '[data-org-user-positions-load-more-users]'
  );
  const positionSelect = root.querySelector('[data-org-user-positions-position]');
  let loading;
  let changing = false;
  let loadingMoreUsers = false;
  let users = [];
  let userPage = 1;
  let userTotal = 0;
  if (form) form.hidden = !canWrite();

  const load = async () => {
    if (loading) return await loading;
    loading = Promise.all([
      request('/api/v1/organization/user-positions?page=1&pageSize=20'),
      request('/api/v1/organization/positions?page=1&pageSize=20'),
      canWrite()
        ? request('/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100')
            .catch(problem => {
              if (problem?.status === 403) {
                return { items: [], page: 1, pageSize: 100, total: 0 };
              }
              throw problem;
            })
        : Promise.resolve({ items: [], page: 1, pageSize: 100, total: 0 })
    ])
      .then(([assignmentPage, positionPage, userPageResult]) => {
        users = Array.isArray(userPageResult?.items) ? userPageResult.items : [];
        userPage = positiveIntegerOr(userPageResult?.page, 1);
        userTotal = nonNegativeIntegerOr(userPageResult?.total, users.length);
        renderSelectOptions(
          userSelect,
          users,
          user => `${user.displayName} (${user.username})`,
          user => user.id
        );
        updateLoadMoreButton(
          loadMoreUsersButton,
          users.length < userTotal,
          loadingMoreUsers
        );
        renderSelectOptions(
          positionSelect,
          Array.isArray(positionPage?.items)
            ? positionPage.items.filter(position => position.isActive)
            : [],
          position => `${position.name} (${position.code})`,
          position => position.id
        );
        renderDirectory(
          directory,
          Array.isArray(assignmentPage?.items) ? assignmentPage.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('orgUserPositions.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onLoadMoreUsers = async () => {
    if (loadingMoreUsers || !canWrite() || users.length >= userTotal) return;
    loadingMoreUsers = true;
    updateLoadMoreButton(loadMoreUsersButton, true, true);
    const selectedUserId = userSelect?.value ?? '';
    try {
      const nextPage = await request(
        `/api/v1/organization/user-positions/assignable-users?page=${userPage + 1}&pageSize=100`
      );
      users = appendUniqueUsers(
        users,
        Array.isArray(nextPage?.items) ? nextPage.items : []
      );
      userPage = positiveIntegerOr(nextPage?.page, userPage + 1);
      userTotal = nonNegativeIntegerOr(nextPage?.total, users.length);
      renderSelectOptions(
        userSelect,
        users,
        user => `${user.displayName} (${user.username})`,
        user => user.id
      );
      if (userSelect && users.some(user => user.id === selectedUserId)) {
        userSelect.value = selectedUserId;
      }
      hideProblem(root);
    } catch (problem) {
      if (problem?.status === 403) {
        userTotal = users.length;
      } else {
        showProblem(root, problem, translation().t('orgUserPositions.loadFailed'));
      }
    } finally {
      loadingMoreUsers = false;
      updateLoadMoreButton(
        loadMoreUsersButton,
        users.length < userTotal,
        false
      );
    }
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form || !canWrite()) return;
    const data = new FormData(form);
    const userId = String(data.get('userId') ?? '').trim();
    const positionId = String(data.get('positionId') ?? '').trim();
    const isPrimary = data.get('isPrimary') === 'on';
    if (!userId || !positionId) return;
    changing = true;
    try {
      await request('/api/v1/organization/user-positions', jsonRequest({
        userId,
        positionId,
        isPrimary
      }));
      form.reset();
      notify(translation().t('orgUserPositions.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('orgUserPositions.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const primaryButton = event.target instanceof Element
      ? event.target.closest('[data-org-user-positions-primary]')
      : undefined;
    if (primaryButton && !changing) {
      const assignmentId = primaryButton.dataset.orgUserPositionsPrimary;
      const version = Number(primaryButton.dataset.version ?? '0');
      changing = true;
      void request(
        `/api/v1/organization/user-positions/${encodeURIComponent(assignmentId)}`,
        {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ isPrimary: true, version })
        }
      )
        .then(async () => {
          notify(translation().t('orgUserPositions.primarySuccess'), 1);
          await load();
        })
        .catch(problem => {
          showProblem(root, problem, translation().t('orgUserPositions.operationFailed'));
        })
        .finally(() => { changing = false; });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-org-user-positions-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const assignmentId = disableButton.dataset.orgUserPositionsDisable;
    const label = disableButton.dataset.label ?? '';
    const message = translation().t('orgUserPositions.confirmDisable', { name: label });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/organization/user-positions/${encodeURIComponent(assignmentId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('orgUserPositions.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('orgUserPositions.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDirectoryAction);
  loadMoreUsersButton?.addEventListener('click', onLoadMoreUsers);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDirectoryAction);
      loadMoreUsersButton?.removeEventListener('click', onLoadMoreUsers);
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

function renderSelectOptions(container, items, labelFor, valueFor) {
  if (!container) return;
  const fragment = container.ownerDocument.createDocumentFragment();
  const placeholder = container.ownerDocument.createElement('option');
  placeholder.value = '';
  placeholder.textContent = '';
  fragment.append(placeholder);
  items.forEach(item => {
    const option = container.ownerDocument.createElement('option');
    option.value = valueFor(item);
    option.textContent = labelFor(item);
    fragment.append(option);
  });
  container.replaceChildren(fragment);
}

function appendUniqueUsers(current, incoming) {
  const byId = new Map(current.map(user => [user.id, user]));
  incoming.forEach(user => byId.set(user.id, user));
  return [...byId.values()];
}

function updateLoadMoreButton(button, hasMore, loading) {
  if (!button) return;
  button.hidden = !hasMore;
  button.disabled = loading;
}

function positiveIntegerOr(value, fallback) {
  return Number.isInteger(value) && value >= 1 ? value : fallback;
}

function nonNegativeIntegerOr(value, fallback) {
  return Number.isInteger(value) && value >= 0 ? value : fallback;
}

function renderDirectory(container, assignments, translation) {
  if (!container) return;
  if (assignments.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-org-user-positions__empty';
    empty.textContent = translation.t('orgUserPositions.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  assignments.forEach(assignment => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-org-user-positions__mark';
    mark.textContent = String(assignment.positionCode ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = assignment.displayName ?? '';
    const code = container.ownerDocument.createElement('code');
    code.textContent = `${assignment.username ?? ''} · ${assignment.positionName ?? ''}`;
    identity.append(name, code);
    const tags = container.ownerDocument.createElement('div');
    tags.className = 'fn-org-user-positions__tags';
    if (assignment.isPrimary) {
      const primary = container.ownerDocument.createElement('span');
      primary.textContent = translation.t('orgUserPositions.primary');
      tags.append(primary);
    }
    const status = container.ownerDocument.createElement('span');
    status.textContent = translation.t(
      assignment.isActive ? 'orgUserPositions.active' : 'orgUserPositions.inactive'
    );
    tags.append(status);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-org-user-positions__actions';
    if (assignment.isActive && !assignment.isPrimary) {
      const primaryButton = container.ownerDocument.createElement('button');
      primaryButton.type = 'button';
      primaryButton.className = 'layui-btn layui-btn-primary layui-btn-sm';
      primaryButton.dataset.orgUserPositionsPrimary = assignment.id;
      primaryButton.dataset.version = String(assignment.version ?? 0);
      primaryButton.textContent = translation.t('orgUserPositions.setPrimary');
      actions.append(primaryButton);
    }
    if (assignment.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-sm';
      disable.dataset.orgUserPositionsDisable = assignment.id;
      disable.dataset.label = `${assignment.displayName ?? ''} / ${assignment.positionName ?? ''}`;
      disable.textContent = translation.t('orgUserPositions.disable');
      actions.append(disable);
    }
    article.append(mark, identity, tags, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-org-user-positions-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.error';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-org-user-positions-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  if (typeof layui !== 'undefined') {
    layui.layer.msg(message, { icon });
  }
}

function confirmAction(message, onConfirm) {
  if (typeof layui === 'undefined') return;
  layui.layer.confirm(message, { btn: ['确认', '取消'] }, index => {
    layui.layer.close(index);
    void onConfirm();
  });
}
