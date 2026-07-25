/**
 * 装配 Host 租户管理视图；支持开通、名称更新与禁用。
 */
export function createTenantsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-tenants-create-form]');
  const createPackageSelect = root.querySelector('[data-tenants-create-package]');
  const directory = root.querySelector('[data-tenants-directory]');
  let loading;
  let changing = false;
  let activePackages = [];

  const load = async () => {
    if (loading) return await loading;
    loading = Promise.all([
      request('/api/v1/tenancy/tenant-packages?page=1&pageSize=100'),
      request('/api/v1/tenancy/tenants?page=1&pageSize=20')
    ])
      .then(([packagePage, tenantPage]) => {
        activePackages = (Array.isArray(packagePage?.items) ? packagePage.items : [])
          .filter(pkg => pkg.isActive);
        refreshCreatePackageSelect(createPackageSelect, activePackages, translation());
        renderDirectory(
          directory,
          Array.isArray(tenantPage?.items) ? tenantPage.items : [],
          activePackages,
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('tenants.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const identifier = String(data.get('identifier') ?? '').trim().toLowerCase();
    const name = String(data.get('name') ?? '').trim();
    const domain = String(data.get('domain') ?? '').trim().toLowerCase();
    const tenantPackageId = String(data.get('tenantPackageId') ?? '').trim() || null;
    if (!identifier || !name || !domain) return;
    changing = true;
    try {
      const payload = { identifier, name, domain };
      if (tenantPackageId) {
        payload.tenantPackageId = tenantPackageId;
      }
      await request('/api/v1/tenancy/tenants', jsonRequest(payload));
      form.reset();
      notify(translation().t('tenants.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('tenants.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const packageSelect = event.target instanceof Element
      ? event.target.closest('[data-tenants-package]')
      : undefined;
    if (packageSelect && !changing) {
      const tenantId = packageSelect.dataset.tenantsPackage;
      const version = Number(packageSelect.dataset.version ?? '0');
      const tenantPackageId = packageSelect.value || null;
      changing = true;
      void request(
        `/api/v1/tenancy/tenants/${encodeURIComponent(tenantId)}/package`,
        {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ tenantPackageId, version })
        }
      )
        .then(async () => {
          notify(translation().t('tenants.packageAssignSuccess'), 1);
          await load();
        })
        .catch(problem => {
          showProblem(root, problem, translation().t('tenants.operationFailed'));
        })
        .finally(() => { changing = false; });
      return;
    }

    const editButton = event.target instanceof Element
      ? event.target.closest('[data-tenants-edit]')
      : undefined;
    if (editButton && !changing) {
      const tenantId = editButton.dataset.tenantsEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const currentName = editButton.dataset.name ?? '';
      promptText(translation().t('tenants.editTitle'), currentName, async nextName => {
        if (!nextName.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/tenancy/tenants/${encodeURIComponent(tenantId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ name: nextName.trim(), version })
            }
          );
          notify(translation().t('tenants.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('tenants.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-tenants-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const tenantId = disableButton.dataset.tenantsDisable;
    const identifier = disableButton.dataset.identifier ?? '';
    const message = translation().t('tenants.confirmDisable', { name: identifier });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/tenancy/tenants/${encodeURIComponent(tenantId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('tenants.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('tenants.operationFailed'));
      } finally {
        changing = false;
      }
    });
  };

  form?.addEventListener('submit', onCreate);
  directory?.addEventListener('click', onDirectoryAction);
  directory?.addEventListener('change', onDirectoryAction);
  return {
    load,
    dispose() {
      form?.removeEventListener('submit', onCreate);
      directory?.removeEventListener('click', onDirectoryAction);
      directory?.removeEventListener('change', onDirectoryAction);
    }
  };
}

function refreshCreatePackageSelect(select, packages, translation) {
  if (!select) return;
  const selected = select.value;
  const fragment = select.ownerDocument.createDocumentFragment();
  const emptyOption = select.ownerDocument.createElement('option');
  emptyOption.value = '';
  emptyOption.textContent = translation.t('tenants.packageUnassigned');
  fragment.append(emptyOption);
  packages.forEach(pkg => {
    const option = select.ownerDocument.createElement('option');
    option.value = pkg.id;
    option.textContent = pkg.name;
    fragment.append(option);
  });
  select.replaceChildren(fragment);
  if (selected && packages.some(pkg => pkg.id === selected)) {
    select.value = selected;
  }
}

function jsonRequest(body) {
  return {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}

function renderDirectory(container, tenants, packages, translation) {
  if (!container) return;
  if (tenants.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('tenants.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  tenants.forEach(tenant => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-tenants__mark';
    mark.textContent = String(tenant.identifier ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = tenant.name ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = `${tenant.identifier ?? ''} · ${tenant.domain ?? ''}`;
    identity.append(name, meta);
    if (tenant.tenantPackageName) {
      const packageLabel = container.ownerDocument.createElement('small');
      packageLabel.textContent = `${translation.t('tenants.packageLabel')}: ${tenant.tenantPackageName}`;
      identity.append(packageLabel);
    }
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(tenant.isActive ? 'tenants.active' : 'tenants.inactive');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-tenants__actions';
    const packageSelect = container.ownerDocument.createElement('select');
    packageSelect.className = 'layui-input';
    packageSelect.dataset.tenantsPackage = tenant.id;
    packageSelect.dataset.version = String(tenant.version ?? 0);
    const emptyOption = container.ownerDocument.createElement('option');
    emptyOption.value = '';
    emptyOption.textContent = translation.t('tenants.packageUnassigned');
    packageSelect.append(emptyOption);
    packages.forEach(pkg => {
      const option = container.ownerDocument.createElement('option');
      option.value = pkg.id;
      option.textContent = pkg.name;
      option.selected = tenant.tenantPackageId === pkg.id;
      packageSelect.append(option);
    });
    actions.append(packageSelect);
    const edit = container.ownerDocument.createElement('button');
    edit.type = 'button';
    edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
    edit.dataset.tenantsEdit = tenant.id;
    edit.dataset.version = String(tenant.version ?? 0);
    edit.dataset.name = tenant.name ?? '';
    edit.textContent = translation.t('tenants.edit');
    actions.append(edit);
    if (tenant.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.tenantsDisable = tenant.id;
      disable.dataset.identifier = tenant.identifier;
      disable.textContent = translation.t('tenants.disable');
      actions.append(disable);
    }
    article.append(mark, identity, state, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function confirmAction(message, confirm) {
  if (globalThis.layui?.layer?.confirm) {
    globalThis.layui.layer.confirm(message, { icon: 3 }, (index) => {
      globalThis.layui.layer.close(index);
      void confirm();
    });
    return;
  }
  if (globalThis.confirm(message)) {
    void confirm();
  }
}

function promptText(title, value, confirm) {
  if (globalThis.layui?.layer?.prompt) {
    globalThis.layui.layer.prompt({ title, value, formType: 0 }, (input, index) => {
      globalThis.layui.layer.close(index);
      if (input) void confirm(input);
    });
    return;
  }
  const input = globalThis.prompt(title, value);
  if (input) void confirm(input);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-tenants-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.host_tenant_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-tenants-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
