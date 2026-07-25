/**
 * 装配 Host 文件目录视图；支持上传与删除。
 */
export function createHostFilesController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-host-files-upload-form]');
  const directory = root.querySelector('[data-host-files-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/files/host-files?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('hostFiles.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onUpload = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const fileInput = form.querySelector('input[type="file"]');
    const file = fileInput?.files?.[0];
    if (!file) return;
    changing = true;
    try {
      const body = new FormData();
      body.append('file', file);
      await request('/api/v1/files/host-files', { method: 'POST', body });
      form.reset();
      notify(translation().t('hostFiles.uploadSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('hostFiles.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const deleteButton = event.target instanceof Element
      ? event.target.closest('[data-host-files-delete]')
      : undefined;
    if (!deleteButton || changing) return;
    const fileId = deleteButton.dataset.hostFilesDelete;
    const fileName = deleteButton.dataset.fileName ?? '';
    const message = translation().t('hostFiles.confirmDelete', { name: fileName });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/files/host-files/${encodeURIComponent(fileId)}/delete`,
          { method: 'POST' }
        );
        notify(translation().t('hostFiles.deleteSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('hostFiles.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onUpload);
  directory?.addEventListener('click', onDirectoryAction);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onUpload);
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(container, items, translation) {
  if (!container) return;
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('hostFiles.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  items.forEach(item => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = item.originalFileName ?? '';
    const contentType = container.ownerDocument.createElement('code');
    contentType.textContent = item.contentType ?? '';
    const size = container.ownerDocument.createElement('small');
    size.textContent = `${translation.t('hostFiles.sizeBytes')}: ${item.sizeBytes ?? 0}`;
    const created = container.ownerDocument.createElement('small');
    created.textContent = `${translation.t('hostFiles.createdAt')}: ${item.createdAtUtc ?? ''}`;
    identity.append(name, contentType, size, created);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-users__actions';
    const remove = container.ownerDocument.createElement('button');
    remove.type = 'button';
    remove.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
    remove.dataset.hostFilesDelete = item.id;
    remove.dataset.fileName = item.originalFileName ?? '';
    remove.textContent = translation.t('hostFiles.delete');
    actions.append(remove);
    article.append(identity, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function confirmAction(message, confirm) {
  if (globalThis.layui?.layer?.confirm) {
    globalThis.layui.layer.confirm(message, { icon: 3 }, index => {
      globalThis.layui.layer.close(index);
      void confirm();
    });
    return;
  }
  if (globalThis.confirm(message)) {
    void confirm();
  }
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-host-files-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'error';
  panel.querySelector('span').textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-host-files-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
