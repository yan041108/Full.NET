/**
 * 装配 Host 官方模块清单只读视图。
 */
export function createModuleCatalogController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-module-catalog-directory]');
  const detail = root.querySelector('[data-module-catalog-detail]');
  let loading;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/identity/modules')
      .then(items => {
        renderDirectory(
          directory,
          Array.isArray(items) ? items : [],
          translation()
        );
        if (detail) {
          detail.replaceChildren();
          const empty = detail.ownerDocument.createElement('p');
          empty.className = 'fn-tenants__empty';
          empty.textContent = translation().t('moduleCatalog.emptyDirectory');
          detail.append(empty);
        }
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('moduleCatalog.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onDirectoryAction = event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-module-catalog-select]')
      : undefined;
    if (!button) return;
    const moduleKey = button.getAttribute('data-module-catalog-select');
    if (!moduleKey) return;
    request(`/api/v1/identity/modules/${encodeURIComponent(moduleKey)}`)
      .then(item => {
        renderDetail(detail, item, translation());
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('moduleCatalog.loadFailed'));
      });
  };

  directory?.addEventListener('click', onDirectoryAction);

  return {
    activate: () => load(),
    destroy: () => {
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(container, items, i18n) {
  if (!container) return;
  container.replaceChildren();
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = i18n.t('moduleCatalog.emptyDirectory');
    container.append(empty);
    return;
  }

  for (const item of items) {
    const row = container.ownerDocument.createElement('article');
    row.className = 'fn-tenants__row';
    row.innerHTML = `
      <div>
        <strong></strong>
        <code></code>
        <small></small>
      </div>
      <button type="button" class="layui-btn layui-btn-primary layui-btn-sm" data-module-catalog-select></button>
    `;
    row.querySelector('strong').textContent = item.displayName ?? item.moduleKey;
    row.querySelector('code').textContent = item.moduleKey ?? '';
    row.querySelector('small').textContent =
      `${i18n.t('moduleCatalog.version')}: ${item.version ?? ''}`;
    const button = row.querySelector('[data-module-catalog-select]');
    button.setAttribute('data-module-catalog-select', item.moduleKey ?? '');
    button.textContent = i18n.t('moduleCatalog.select');
    container.append(row);
  }
}

function renderDetail(container, item, i18n) {
  if (!container) return;
  container.replaceChildren();
  const block = container.ownerDocument.createElement('div');
  block.className = 'fn-tenants__row';
  const dependencies = Array.isArray(item.dependencies) && item.dependencies.length > 0
    ? item.dependencies.join(', ')
    : i18n.t('moduleCatalog.noDependencies');
  const profiles = Array.isArray(item.hostProfiles)
    ? item.hostProfiles.join(', ')
    : '';
  block.innerHTML = `
    <p><strong></strong> <span data-host-profiles></span></p>
    <p><strong data-dep-label></strong> <span data-dependencies></span></p>
    <p><strong data-health-label></strong> <span data-health></span></p>
  `;
  block.querySelector('strong').textContent = `${i18n.t('moduleCatalog.hostProfiles')}:`;
  block.querySelector('[data-host-profiles]').textContent = profiles;
  block.querySelector('[data-dep-label]').textContent = `${i18n.t('moduleCatalog.dependencies')}:`;
  block.querySelector('[data-dependencies]').textContent = dependencies;
  block.querySelector('[data-health-label]').textContent =
    `${i18n.t('moduleCatalog.healthCapability')}:`;
  block.querySelector('[data-health]').textContent = item.healthCapability ?? '';
  container.append(block);
}

function showProblem(root, problem, fallbackTitle) {
  const panel = root.querySelector('[data-module-catalog-problem]');
  if (!panel) return;
  panel.hidden = false;
  const code = panel.querySelector('strong');
  const title = panel.querySelector('span');
  if (code) code.textContent = problem?.code ?? 'error';
  if (title) title.textContent = problem?.title ?? fallbackTitle;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-module-catalog-problem]');
  if (panel) panel.hidden = true;
}
