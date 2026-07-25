/**
 * 装配 Host 租户套餐目录视图；支持创建、名称更新与禁用。
 */
export function createTenantPackagesController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-tenant-packages-create-form]');
  const directory = root.querySelector('[data-tenant-packages-directory]');
  let loading;
  let changing = false;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/tenancy/tenant-packages?page=1&pageSize=20')
      .then(page => {
        renderDirectory(
          directory,
          Array.isArray(page?.items) ? page.items : [],
          translation()
        );
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('tenantPackages.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onCreate = async event => {
    event.preventDefault();
    if (changing || !form) return;
    const data = new FormData(form);
    const code = String(data.get('code') ?? '').trim().toLowerCase();
    const name = String(data.get('name') ?? '').trim();
    const description = String(data.get('description') ?? '').trim();
    if (!code || !name) return;
    changing = true;
    try {
      await request(
        '/api/v1/tenancy/tenant-packages',
        jsonRequest({
          code,
          name,
          description: description || null
        })
      );
      form.reset();
      notify(translation().t('tenantPackages.createSuccess'), 1);
      await load();
    } catch (problem) {
      showProblem(root, problem, translation().t('tenantPackages.operationFailed'));
    } finally {
      changing = false;
    }
  };

  const onDirectoryAction = event => {
    const editButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-packages-edit]')
      : undefined;
    if (editButton && !changing) {
      const packageId = editButton.dataset.tenantPackagesEdit;
      const version = Number(editButton.dataset.version ?? '0');
      const currentName = editButton.dataset.name ?? '';
      const currentDescription = editButton.dataset.description ?? '';
      promptText(translation().t('tenantPackages.editTitle'), currentName, async nextName => {
        if (!nextName.trim()) return;
        changing = true;
        try {
          await request(
            `/api/v1/tenancy/tenant-packages/${encodeURIComponent(packageId)}`,
            {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                name: nextName.trim(),
                description: currentDescription || null,
                version
              })
            }
          );
          notify(translation().t('tenantPackages.updateSuccess'), 1);
          await load();
        } catch (problem) {
          showProblem(root, problem, translation().t('tenantPackages.operationFailed'));
        } finally {
          changing = false;
        }
      });
      return;
    }

    const disableButton = event.target instanceof Element
      ? event.target.closest('[data-tenant-packages-disable]')
      : undefined;
    if (!disableButton || changing) return;
    const packageId = disableButton.dataset.tenantPackagesDisable;
    const code = disableButton.dataset.code ?? '';
    const message = translation().t('tenantPackages.confirmDisable', { name: code });
    confirmAction(message, async () => {
      changing = true;
      try {
        await request(
          `/api/v1/tenancy/tenant-packages/${encodeURIComponent(packageId)}/disable`,
          { method: 'POST' }
        );
        notify(translation().t('tenantPackages.disableSuccess'), 1);
        await load();
      } catch (problem) {
        showProblem(root, problem, translation().t('tenantPackages.operationFailed'));
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

function renderDirectory(container, packages, translation) {
  if (!container) return;
  if (packages.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('tenantPackages.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  packages.forEach(pkg => {
    const article = container.ownerDocument.createElement('article');
    const mark = container.ownerDocument.createElement('span');
    mark.className = 'fn-tenants__mark';
    mark.textContent = String(pkg.code ?? '').slice(0, 2).toUpperCase();
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = pkg.name ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = pkg.code ?? '';
    identity.append(name, meta);
    const assigned = container.ownerDocument.createElement('small');
    assigned.textContent = `${translation.t('tenantPackages.assignedTenantCount')}: ${pkg.assignedTenantCount ?? 0}`;
    identity.append(assigned);
    if (pkg.description) {
      const description = container.ownerDocument.createElement('small');
      description.textContent = pkg.description;
      identity.append(description);
    }
    const state = container.ownerDocument.createElement('em');
    state.textContent = translation.t(pkg.isActive ? 'tenantPackages.active' : 'tenantPackages.inactive');
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-tenants__actions';
    const edit = container.ownerDocument.createElement('button');
    edit.type = 'button';
    edit.className = 'layui-btn layui-btn-primary layui-btn-sm';
    edit.dataset.tenantPackagesEdit = pkg.id;
    edit.dataset.version = String(pkg.version ?? 0);
    edit.dataset.name = pkg.name ?? '';
    edit.dataset.description = pkg.description ?? '';
    edit.textContent = translation.t('tenantPackages.edit');
    actions.append(edit);
    if (pkg.isActive) {
      const disable = container.ownerDocument.createElement('button');
      disable.type = 'button';
      disable.className = 'layui-btn layui-btn-danger layui-btn-primary layui-btn-sm';
      disable.dataset.tenantPackagesDisable = pkg.id;
      disable.dataset.code = pkg.code;
      disable.textContent = translation.t('tenantPackages.disable');
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
  const panel = root.querySelector('[data-tenant-packages-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.host_tenant_package_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-tenant-packages-problem]');
  if (panel) panel.hidden = true;
}

function notify(message, icon) {
  globalThis.layui?.layer?.msg?.(message, { icon });
}
