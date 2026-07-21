/**
 * 装配租户用户-机构隶属视图；支持分配、设主部门与取消隶属。
 */
export function createOrgUserUnitsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-org-user-units-create-form]');
  const directory = root.querySelector('[data-org-user-units-directory]');
  const userSelect = root.querySelector('[data-org-user-units-user]');
  const unitSelect = root.querySelector('[data-org-user-units-unit]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = Promise.all([
      request('/api/v1/organization/user-units?page=1&pageSize=20'),
      request('/api/v1/identity/users?page=1&pageSize=20'),
      request('/api/v1/organization/units?page=1&pageSize=20')
    ])
      .then(([assignmentPage, userPage, unitPage]) => {
        renderSelectOptions(
          userSelect,
          Array.isArray(userPage?.items)
            ? userPage.items.filter(user => user.isActive)
            : [],
          user => `${user.displayName} (${user.username})`,
          user => user.id
        );
        renderSelectOptions(
          unitSelect,
          Array.isArray(unitPage?.items)
            ? unitPage.items.filter(unit => unit.isActive)
            : [],
          unit => `${unit.name} (${unit.code})`,
          unit => unit.id
        );
        renderDirectory(
          directory,
          Array.isArray(assignmentPage?.items) ? assignmentPage.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('orgUserUnits.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const userId = String(data.get('userId') ?? '').trim();
    const unitId = String(data.get('unitId') ?? '').trim();
    const isPrimary = data.get('isPrimary') === 'on';
    if (!userId || !unitId) return;
    changing = true;
    try {
      await request('/api/v1/organization/user-units', jsonRequest({
        userId,
        unitId,
        isPrimary
      }));
      form.reset();
      notify(translation().t('orgUserUnits.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('orgUserUnits.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const primaryButton = event.target instanceof Element
      ? event.target.closest('[data-org-user-units-primary]')
      : undefined;
    if (primaryButton && !changing) {
      const assignmentId = primaryButton.dataset.orgUserUnitsPrimary;
      const version = Number(primaryButton.dataset.version ?? '0');
      changing = true;
      void request(
        `/api/v1/organization/user-units/${encodeURIComponent(assignmentId)}`,
        {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ isPrimary: true, version })
        }
      )
        .then(async () => {
          notify(translation().t('orgUserUnits.primarySuccess'), 1);
          await load();
        })
        .catch(problem => {
          showProblem(root, problem, translation().t('orgUserUnits.operationFailed'));
        })
        .finally(() => { changing = false; });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-org-user-units-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const assignmentId = disableButton.dataset.orgUserUnitsDisable;
    const label = disableButton.dataset.label ?? '';
    const message = translation().t('orgUserUnits.confirmDisable', { name: label });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/organization/user-units/${encodeURIComponent(assignmentId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('orgUserUnits.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('orgUserUnits.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDirectoryAction);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDirectoryAction);
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

function renderDirectory(container, assignments, translation) {
  if (!container) return;
  if (assignments.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-org-user-units__empty';
    empty.textContent = translation.t('orgUserUnits.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  assignments.forEach(assignment => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-org-user-units__mark';
    mark.textContent = String(assignment.unitCode ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = assignment.displayName ?? '';
    const code = container.ownerDocument.createElement('code');
    code.textContent = `${assignment.username ?? ''} · ${assignment.unitName ?? ''}`;
    identity.append(name, code);
    const tags = container.ownerDocument.createElement('div');
    tags.className = 'fn-org-user-units__tags';
    if (assignment.isPrimary) {
      const primary = container.ownerDocument.createElement('span');
      primary.textContent = translation.t('orgUserUnits.primary');
      tags.append(primary);
    }
    const status = container.ownerDocument.createElement('span');
    status.textContent = translation.t(
      assignment.isActive ? 'orgUserUnits.active' : 'orgUserUnits.inactive'
    );
    tags.append(status);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-org-user-units__actions';
    if (assignment.isActive && !assignment.isPrimary) {
      const primaryButton = container.ownerDocument.createElement('button');
      primaryButton.type = 'button';
      primaryButton.className = 'layui-btn layui-btn-primary layui-btn-sm';
      primaryButton.dataset.orgUserUnitsPrimary = assignment.id;
      primaryButton.dataset.version = String(assignment.version ?? 0);
      primaryButton.textContent = translation.t('orgUserUnits.setPrimary');
      actions.append(primaryButton);
    }
    if (assignment.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-sm';
      disable.dataset.orgUserUnitsDisable = assignment.id;
      disable.dataset.label = `${assignment.displayName ?? ''} / ${assignment.unitName ?? ''}`;
      disable.textContent = translation.t('orgUserUnits.disable');
      actions.append(disable);
    }
    article.append(mark, identity, tags, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-org-user-units-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.error';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-org-user-units-problem]');
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
