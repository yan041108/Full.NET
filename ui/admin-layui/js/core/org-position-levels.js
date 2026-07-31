/**
 * 装配租户职级管理视图；支持创建、名称更新与禁用。
 */
export function createOrgPositionLevelsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-org-position-levels-create-form]');
  const directory = root.querySelector('[data-org-position-levels-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/organization/position-levels?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('orgPositionLevels.loadFailed'));
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
      await request('/api/v1/organization/position-levels', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ code, name, displayOrder: 10 })
      });
      form.reset();
      notify(translation().t('orgPositionLevels.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('orgPositionLevels.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-org-position-levels-edit]')
      : undefined;
    if (editButton && !changing) {
      promptText(
        translation().t('orgPositionLevels.editTitle'),
        editButton.dataset.name ?? '',
        async nextName => {
          if (!nextName.trim()) return;
          changing = true;
          try {
            await request(
              `/api/v1/organization/position-levels/${encodeURIComponent(editButton.dataset.orgPositionLevelsEdit)}`,
              {
                method: 'PUT',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({
                  name: nextName.trim(),
                  displayOrder: Number(editButton.dataset.displayOrder ?? '0'),
                  version: Number(editButton.dataset.version ?? '0')
                })
              }
            );
            notify(translation().t('orgPositionLevels.updateSuccess'), 1);
            await load();
          } catch (problem) {
            showProblem(root, problem, translation().t('orgPositionLevels.operationFailed'));
          } finally {
            changing = false;
          }
        }
      );
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-org-position-levels-disable]')
      : undefined;
    if (!disableButton || changing) return;
    confirmAction(
      translation().t('orgPositionLevels.confirmDisable', {
        name: disableButton.dataset.code ?? ''
      }),
      async () => {
        changing = true;
        try {
          await request(
            `/api/v1/organization/position-levels/${encodeURIComponent(disableButton.dataset.orgPositionLevelsDisable)}/disable`,
            { method: 'POST' }
          );
          notify(translation().t('orgPositionLevels.disableSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('orgPositionLevels.operationFailed'));
        } finally {
          changing = false;
        }
      }
    );
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

function renderDirectory(container, levels, translation) {
  if (!container) return;
  if (levels.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-org-units__empty';
    empty.textContent = translation.t('orgPositionLevels.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  levels.forEach(level => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-org-units__mark';
    mark.textContent = String(level.code ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = level.name ?? '';
    const code = container.ownerDocument.createElement('code');
    code.textContent = level.code ?? '';
    identity.append(name, code);
    const status = container.ownerDocument.createElement('span');
    status.textContent = translation.t(
      level.isActive ? 'orgPositionLevels.active' : 'orgPositionLevels.inactive'
    );
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-org-units__actions';
    if (level.isActive) {
      const edit = container.ownerDocument.createElement('button');
      edit.type = 'button';
      edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
      edit.dataset.orgPositionLevelsEdit = level.id;
      edit.dataset.version = String(level.version ?? 0);
      edit.dataset.displayOrder = String(level.displayOrder ?? 0);
      edit.dataset.name = level.name ?? '';
      edit.textContent = translation.t('orgPositionLevels.edit');
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-sm';
      disable.dataset.orgPositionLevelsDisable = level.id;
      disable.dataset.code = level.code ?? '';
      disable.textContent = translation.t('orgPositionLevels.disable');
      actions.append(edit, disable);
    }
    article.append(mark, identity, status, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-org-position-levels-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.error';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-org-position-levels-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  if (typeof layui !== 'undefined') layui.layer.msg(message, { icon });
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
