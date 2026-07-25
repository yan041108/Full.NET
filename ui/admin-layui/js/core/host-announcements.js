/**
 * 装配 Host 公告目录视图；支持创建、更新草稿与发布。
 */
export function createHostAnnouncementsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-host-announcements-form]');
  const directory = root.querySelector('[data-host-announcements-directory]');
  let loading;
  let changing = false;
  let editingId;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/notifications/host-announcements?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('hostAnnouncements.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const resetForm = () => {
    editingId = undefined;
    form?.reset();
    const submitButton = form?.querySelector('[type="submit"]');
    if (submitButton) {
      submitButton.textContent = translation().t('hostAnnouncements.create');
    }
  };

  const onSubmit = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const title = String(data.get('title') ?? '').trim();
    const content = String(data.get('content') ?? '').trim();
    if (!title || !content) return;
    changing = true;
    try {
      if (editingId) {
        const version = Number(form.dataset.version ?? '0');
        await request(
          `/api/v1/notifications/host-announcements/${encodeURIComponent(editingId)}`,
          jsonRequest({ title, content, version })
        );
        notify(translation().t('hostAnnouncements.updateSuccess'), 1);
      } else {
        await request(
          '/api/v1/notifications/host-announcements',
          jsonRequest({ title, content }, 'POST')
        );
        notify(translation().t('hostAnnouncements.createSuccess'), 1);
      }
      resetForm();
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('hostAnnouncements.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-host-announcements-edit]')
      : undefined;
    if (editButton && !changing) {
      editingId = editButton.dataset.hostAnnouncementsEdit;
      const titleInput = form?.querySelector('[name="title"]');
      const contentInput = form?.querySelector('[name="content"]');
      if (titleInput) titleInput.value = editButton.dataset.title ?? '';
      if (contentInput) contentInput.value = editButton.dataset.content ?? '';
      if (form) form.dataset.version = editButton.dataset.version ?? '0';
      const submitButton = form?.querySelector('[type="submit"]');
      if (submitButton) {
        submitButton.textContent = translation().t('hostAnnouncements.save');
      }
      return;
    }

    const publishButton = event.target instanceof Element
      ? event.target.closest('[data-host-announcements-publish]')
      : undefined;
    if (!publishButton || changing) return;
    const announcementId = publishButton.dataset.hostAnnouncementsPublish;
    const version = Number(publishButton.dataset.version ?? '0');
    const title = publishButton.dataset.title ?? '';
    const message = translation().t('hostAnnouncements.confirmPublish', { title });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/notifications/host-announcements/${encodeURIComponent(announcementId)}/publish`,
          jsonRequest({ version }, 'POST')
        );
        notify(translation().t('hostAnnouncements.publishSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('hostAnnouncements.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onSubmit);
  directory?.addEventListener('click', onDirectoryAction);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onSubmit);
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(directory, items, translation) {
  if (!directory) return;
  if (items.length === 0) {
    directory.innerHTML = `<p class="fn-empty">${escapeHtml(translation.t('hostAnnouncements.emptyList'))}</p>`;
    return;
  }

  directory.innerHTML = items.map(item => {
    const statusLabel = item.status === 'published'
      ? translation.t('hostAnnouncements.statusPublished')
      : translation.t('hostAnnouncements.statusDraft');
    const draftActions = item.status === 'draft'
      ? `<button type="button" class="layui-btn layui-btn-primary layui-btn-sm"
            data-host-announcements-edit="${escapeHtml(item.id)}"
            data-title="${escapeHtml(item.title)}"
            data-content="${escapeHtml(item.content)}"
            data-version="${item.version}">${escapeHtml(translation.t('hostAnnouncements.edit'))}</button>
         <button type="button" class="layui-btn layui-btn-sm"
            data-host-announcements-publish="${escapeHtml(item.id)}"
            data-title="${escapeHtml(item.title)}"
            data-version="${item.version}">${escapeHtml(translation.t('hostAnnouncements.publish'))}</button>`
      : '';
    return `<article class="fn-data-row">
      <div>
        <strong translate="no">${escapeHtml(item.title)}</strong>
        <span class="layui-badge">${escapeHtml(statusLabel)}</span>
        <p>${escapeHtml(item.content)}</p>
        <small>${escapeHtml(translation.t('hostAnnouncements.createdAt'))}: ${escapeHtml(item.createdAtUtc)}</small>
      </div>
      <div class="fn-data-row__actions">${draftActions}</div>
    </article>`;
  }).join('');
}

function jsonRequest(body, method = 'PUT') {
  return {
    method,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-host-announcements-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.error';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-host-announcements-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  if (typeof window.layui?.layer?.msg === 'function') {
    window.layui.layer.msg(message, { icon });
  }
}

function confirmAction(message, onConfirm) {
  if (typeof window.layui?.layer?.confirm === 'function') {
    window.layui.layer.confirm(message, { icon: 3, title: false }, index => {
      window.layui.layer.close(index);
      void onConfirm();
    });
    return;
  }
  if (window.confirm(message)) {
    void onConfirm();
  }
}
