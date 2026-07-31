/**
 * 装配租户职位管理视图；支持创建、名称更新与禁用。
 */
export function createOrgPositionsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-org-positions-create-form]');
  const directory = root.querySelector('[data-org-positions-directory]');
  const hasPermission = options.hasPermission ?? (() => true);
  const canBindUnits = () => hasPermission('organization.positions.write')
    && hasPermission('organization.units.read');
  const canBindPositionLevels = () => hasPermission('organization.positions.write')
    && hasPermission('organization.position_levels.read');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    const positionsRequest = request(
      '/api/v1/organization/positions?page=1&pageSize=20'
    );
    const unitsRequest = canBindUnits()
      ? request('/api/v1/organization/units?page=1&pageSize=100')
        .catch(() => ({ items: [] }))
      : Promise.resolve({ items: [] });
    const positionLevelsRequest = canBindPositionLevels()
      ? request('/api/v1/organization/position-levels?page=1&pageSize=100')
        .catch(() => ({ items: [] }))
      : Promise.resolve({ items: [] });
    loading = Promise.all([
      positionsRequest,
      unitsRequest,
      positionLevelsRequest
    ])
      .then(([page, unitPage, positionLevelPage]) => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          Array.isArray(unitPage?.items)
            ? unitPage.items.filter(unit => unit?.isActive)
            : [],
          canBindUnits(),
          Array.isArray(positionLevelPage?.items)
            ? positionLevelPage.items.filter(level => level?.isActive)
            : [],
          canBindPositionLevels(),
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('orgPositions.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const code = String(data.get('code') ?? '').trim();
    const name = String(data.get('name') ?? '').trim();
    if (!code || !name) return;
    changing = true;
    try {
      await request('/api/v1/organization/positions', jsonRequest({
        code,
        name,
        displayOrder: 10
      }));
      form.reset();
      notify(translation().t('orgPositions.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('orgPositions.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-org-positions-edit]')
      : undefined;
    if (editButton && !changing) {
      const positionId = editButton.dataset.orgPositionsEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const displayOrder = Number(editButton.dataset.displayOrder ?? '0');
      const currentName = editButton.dataset.name ?? '';
      promptText(translation().t('orgPositions.editTitle'), currentName, async nextName => {
        if (!nextName.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/organization/positions/${encodeURIComponent(positionId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                name: nextName.trim(),
                displayOrder,
                version
              })
            }
          );
          notify(translation().t('orgPositions.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('orgPositions.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-org-positions-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const positionId = disableButton.dataset.orgPositionsDisable;
    const code = disableButton.dataset.code ?? '';
    const message = translation().t('orgPositions.confirmDisable', { name: code });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/organization/positions/${encodeURIComponent(positionId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('orgPositions.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('orgPositions.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  const onPositionLevelChange = async event => {
    const select = event.target instanceof Element
      ? event.target.closest('[data-org-positions-position-level]')
      : undefined;
    if (!select || changing) return;
    changing = true;
    try {
      await request(
        `/api/v1/organization/positions/${encodeURIComponent(select.dataset.orgPositionsPositionLevel)}/position-level`,
        {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            positionLevelId: select.value || null,
            version: Number(select.dataset.version ?? '0')
          })
        }
      );
      notify(translation().t('orgPositions.positionLevelUpdateSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('orgPositions.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onUnitChange = async event => {
    const select = event.target instanceof Element
      ? event.target.closest('[data-org-positions-unit]')
      : undefined;
    if (!select || changing) return;
    changing = true;
    try {
      await request(
        `/api/v1/organization/positions/${encodeURIComponent(select.dataset.orgPositionsUnit)}/unit`,
        {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            unitId: select.value || null,
            version: Number(select.dataset.version ?? '0')
          })
        }
      );
      notify(translation().t('orgPositions.unitUpdateSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('orgPositions.operationFailed'));
    } finally {
      changing = false;
    }
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDirectoryAction);
  directory?.addEventListener('change', onUnitChange);
  directory?.addEventListener('change', onPositionLevelChange);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDirectoryAction);
      directory?.removeEventListener('change', onUnitChange);
      directory?.removeEventListener('change', onPositionLevelChange);
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

function renderDirectory(
  container,
  positions,
  units,
  canBindUnits,
  positionLevels,
  canBindPositionLevels,
  translation
) {
  if (!container) return;
  if (positions.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-org-units__empty';
    empty.textContent = translation.t('orgPositions.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  positions.forEach(position => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-org-units__mark';
    mark.textContent = String(position.code ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = position.name ?? '';
    const code = container.ownerDocument.createElement('code');
    code.textContent = position.code ?? '';
    const unitName = container.ownerDocument.createElement('span');
    unitName.textContent = position.unitName
      ?? translation.t('orgPositions.unitUnassigned');
    const positionLevelName = container.ownerDocument.createElement('span');
    positionLevelName.textContent = position.positionLevelName
      ?? translation.t('orgPositions.positionLevelUnassigned');
    identity.append(name, code, unitName, positionLevelName);
    const tags = container.ownerDocument.createElement('div');
    tags.className = 'fn-org-units__tags';
    const status = container.ownerDocument.createElement('span');
    status.textContent = translation.t(
      position.isActive ? 'orgPositions.active' : 'orgPositions.inactive'
    );
    tags.append(status);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-org-units__actions';
    if (position.isActive && canBindUnits) {
      const unitLabel = container.ownerDocument.createElement('label');
      const unitSelect = container.ownerDocument.createElement('select');
      unitSelect.className = 'layui-input';
      unitSelect.setAttribute('aria-label', translation.t('orgPositions.unit'));
      unitSelect.dataset.orgPositionsUnit = position.id;
      unitSelect.dataset.version = String(position.version ?? 0);
      const emptyOption = container.ownerDocument.createElement('option');
      emptyOption.value = '';
      emptyOption.textContent = translation.t('orgPositions.unitUnassigned');
      unitSelect.append(emptyOption);
      units.forEach(unit => {
        const option = container.ownerDocument.createElement('option');
        option.value = unit.id;
        option.textContent = `${unit.name} (${unit.code})`;
        unitSelect.append(option);
      });
      unitSelect.value = position.unitId ?? '';
      unitLabel.append(unitSelect);
      actions.append(unitLabel);
    }
    if (position.isActive && canBindPositionLevels) {
      const positionLevelLabel = container.ownerDocument.createElement('label');
      const positionLevelSelect = container.ownerDocument.createElement('select');
      positionLevelSelect.className = 'layui-input';
      positionLevelSelect.setAttribute(
        'aria-label',
        translation.t('orgPositions.positionLevel')
      );
      positionLevelSelect.dataset.orgPositionsPositionLevel = position.id;
      positionLevelSelect.dataset.version = String(position.version ?? 0);
      const emptyOption = container.ownerDocument.createElement('option');
      emptyOption.value = '';
      emptyOption.textContent = translation.t('orgPositions.positionLevelUnassigned');
      positionLevelSelect.append(emptyOption);
      positionLevels.forEach(positionLevel => {
        const option = container.ownerDocument.createElement('option');
        option.value = positionLevel.id;
        option.textContent = `${positionLevel.name} (${positionLevel.code})`;
        positionLevelSelect.append(option);
      });
      positionLevelSelect.value = position.positionLevelId ?? '';
      positionLevelLabel.append(positionLevelSelect);
      actions.append(positionLevelLabel);
    }
    if (position.isActive) {
      const edit = container.ownerDocument.createElement('button');
      edit.type = 'button';
      edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
      edit.dataset.orgPositionsEdit = position.id;
      edit.dataset.version = String(position.version ?? 0);
      edit.dataset.displayOrder = String(position.displayOrder ?? 0);
      edit.dataset.name = position.name ?? '';
      edit.textContent = translation.t('orgPositions.edit');
      actions.append(edit);
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-sm';
      disable.dataset.orgPositionsDisable = position.id;
      disable.dataset.code = position.code ?? '';
      disable.textContent = translation.t('orgPositions.disable');
      actions.append(disable);
    }
    article.append(mark, identity, tags, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-org-positions-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.error';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-org-positions-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  if (typeof layui !== 'undefined') {
    layui.layer.msg(message, { icon });
  }
}

function promptText(title, value, onConfirm) {
  if (typeof layui === 'undefined') return;
  layui.layer.prompt({ title, value, formType: 0 }, (nextValue, index) => {
    layui.layer.close(index);
    void onConfirm(nextValue);
  });
}

function confirmAction(message, onConfirm) {
  if (typeof layui === 'undefined') return;
  layui.layer.confirm(message, { btn: ['确认', '取消'] }, index => {
    layui.layer.close(index);
    void onConfirm();
  });
}
