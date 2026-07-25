/**
 * 装配租户职位管理视图；支持创建、名称更新与禁用。
 */
export function createOrgPositionsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-org-positions-create-form]');
  const directory = root.querySelector('[data-org-positions-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/organization/positions?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
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

function renderDirectory(container, positions, translation) {
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
    identity.append(name, code);
    const tags = container.ownerDocument.createElement('div');
    tags.className = 'fn-org-units__tags';
    const status = container.ownerDocument.createElement('span');
    status.textContent = translation.t(
      position.isActive ? 'orgPositions.active' : 'orgPositions.inactive'
    );
    tags.append(status);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-org-units__actions';
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
