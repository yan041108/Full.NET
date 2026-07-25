/**
 * 装配 Host 任务定义视图；支持创建、更新、手动触发与禁用。
 */
export function createHostJobsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-host-jobs-form]');
  const directory = root.querySelector('[data-host-jobs-directory]');
  const executions = root.querySelector('[data-host-jobs-executions]');
  let loading;
  let changing = false;
  let editingId;
  let selectedDefinitionId;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/jobs/host-definitions?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('hostJobs.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const loadExecutions = async definitionId => {
    selectedDefinitionId = definitionId;
    try {
      const page = await request(
        `/api/v1/jobs/host-executions?page=1&pageSize=20&jobDefinitionId=${encodeURIComponent(definitionId)}`
      );
      renderExecutions(
        executions,
        Array.isArray(page?.items) ? page.items : [],
        translation()
      );
    } catch (problem) {
      showProblem(root, problem, translation().t('hostJobs.loadFailed'));
    }
  };

  const resetForm = () => {
    editingId = undefined;
    form?.reset();
    const submitButton = form?.querySelector('[type="submit"]');
    if (submitButton) {
      submitButton.textContent = translation().t('hostJobs.create');
    }
  };

  const onSubmit = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const displayName = String(data.get('displayName') ?? '').trim();
    const description = String(data.get('description') ?? '').trim();
    if (!displayName) return;
    changing = true;
    try {
      if (editingId) {
        const version = Number(form.dataset.version ?? '0');
        await request(
          `/api/v1/jobs/host-definitions/${encodeURIComponent(editingId)}`,
          jsonRequest({ displayName, description: description || null, version })
        );
        notify(translation().t('hostJobs.updateSuccess'), 1);
      } else {
        const jobKey = String(data.get('jobKey') ?? 'jobs.ping');
        await request(
          '/api/v1/jobs/host-definitions',
          jsonRequest({ jobKey, displayName, description: description || null }, 'POST')
        );
        notify(translation().t('hostJobs.createSuccess'), 1);
      }
      resetForm();
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('hostJobs.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = async event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-host-jobs-edit]')
      : undefined;
    if (editButton && !changing) {
      editingId = editButton.dataset.hostJobsEdit;
      const displayNameInput = form?.querySelector('[name="displayName"]');
      const descriptionInput = form?.querySelector('[name="description"]');
      if (displayNameInput) displayNameInput.value = editButton.dataset.displayName ?? '';
      if (descriptionInput) descriptionInput.value = editButton.dataset.description ?? '';
      if (form) form.dataset.version = editButton.dataset.version ?? '0';
      const submitButton = form?.querySelector('[type="submit"]');
      if (submitButton) {
        submitButton.textContent = translation().t('hostJobs.save');
      }
      return;
    }

    const triggerButton = event.target instanceof Element
      ? event.target.closest('[data-host-jobs-trigger]')
      : undefined;
    if (triggerButton && !changing) {
      changing = true;
      try {
        await request(
          `/api/v1/jobs/host-definitions/${encodeURIComponent(triggerButton.dataset.hostJobsTrigger)}/trigger`,
          jsonRequest({}, 'POST')
        );
        notify(translation().t('hostJobs.triggerSuccess'), 1);
        await loadExecutions(triggerButton.dataset.hostJobsTrigger);
      } catch (problem) {
        showProblem(root, problem, translation().t('hostJobs.operationFailed'));
      } finally {
        changing = false;
      }
      return;
    }

    const executionsButton = event.target instanceof Element
      ? event.target.closest('[data-host-jobs-executions]')
      : undefined;
    if (executionsButton && !changing) {
      await loadExecutions(executionsButton.dataset.hostJobsExecutions);
    }
  };

  form?.addEventListener('submit', onSubmit);
  directory?.addEventListener('click', onDirectoryAction);

  return { load };
}

function renderDirectory(container, items, translation) {
  if (!container) return;
  if (!items.length) {
    container.innerHTML = `<p>${escapeHtml(translation.t('hostJobs.emptyList'))}</p>`;
    return;
  }
  container.innerHTML = items.map(item => `
    <article class="fn-tenants__item">
      <div>
        <strong>${escapeHtml(item.displayName)}</strong>
        <span>${escapeHtml(item.jobKey)}</span>
        <span>${item.isEnabled ? escapeHtml(translation.t('hostJobs.statusEnabled')) : escapeHtml(translation.t('hostJobs.statusDisabled'))}</span>
      </div>
      <div class="fn-tenants__actions">
        ${item.isEnabled ? `
          <button type="button" class="layui-btn layui-btn-primary layui-btn-sm"
            data-host-jobs-edit="${escapeHtml(item.id)}"
            data-display-name="${escapeHtml(item.displayName)}"
            data-description="${escapeHtml(item.description ?? '')}"
            data-version="${item.version}">${escapeHtml(translation.t('hostJobs.edit'))}</button>
          <button type="button" class="layui-btn layui-btn-sm"
            data-host-jobs-trigger="${escapeHtml(item.id)}">${escapeHtml(translation.t('hostJobs.trigger'))}</button>
        ` : ''}
        <button type="button" class="layui-btn layui-btn-primary layui-btn-sm"
          data-host-jobs-executions="${escapeHtml(item.id)}">${escapeHtml(translation.t('hostJobs.viewExecutions'))}</button>
      </div>
    </article>
  `).join('');
}

function renderExecutions(container, items, translation) {
  if (!container) return;
  if (!items.length) {
    container.innerHTML = `<p>${escapeHtml(translation.t('hostJobs.emptyExecutions'))}</p>`;
    return;
  }
  container.innerHTML = `
    <h3>${escapeHtml(translation.t('hostJobs.executionsTitle'))}</h3>
    <ul>${items.map(item => `
      <li><span>${escapeHtml(item.status)}</span> <span>${escapeHtml(item.createdAtUtc)}</span></li>
    `).join('')}</ul>
  `;
}

function jsonRequest(body, method = 'PUT') {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  };
}

function notify(message, icon) {
  if (window.layui?.layer) {
    window.layui.layer.msg(message, { icon });
  }
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-host-jobs-problem]');
  if (!panel) return;
  const title = problem?.title || fallback;
  const detail = problem?.detail || problem?.code || '';
  panel.hidden = false;
  panel.querySelector('strong').textContent = title;
  panel.querySelector('span').textContent = detail;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-host-jobs-problem]');
  if (panel) panel.hidden = true;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}
