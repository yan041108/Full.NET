/**
 * 装配 Host 枚举/常量目录只读视图。
 */
export function createEnumCatalogsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const directory = root.querySelector('[data-enum-catalogs-directory]');
  const members = root.querySelector('[data-enum-catalogs-members]');
  let loading;

  const load = async () => {
    if (loading) return await loading;
    loading = request('/api/v1/settings/enum-catalogs')
      .then(items => {
        renderDirectory(
          directory,
          Array.isArray(items) ? items : [],
          translation()
        );
        if (members) {
          members.replaceChildren();
          const empty = members.ownerDocument.createElement('p');
          empty.className = 'fn-tenants__empty';
          empty.textContent = translation().t('enumCatalogs.emptyMembers');
          members.append(empty);
        }
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('enumCatalogs.loadFailed'));
      })
      .finally(() => { loading = undefined; });
    return await loading;
  };

  const onDirectoryAction = event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-enum-catalogs-select]')
      : undefined;
    if (!button) return;
    const catalogKey = button.dataset.enumCatalogsSelect;
    void request(`/api/v1/settings/enum-catalogs/${encodeURIComponent(catalogKey)}`)
      .then(detail => {
        renderMembers(members, detail, translation());
        hideProblem(root);
      })
      .catch(problem => {
        showProblem(root, problem, translation().t('enumCatalogs.loadFailed'));
      });
  };

  directory?.addEventListener('click', onDirectoryAction);
  return {
    load,
    dispose() {
      directory?.removeEventListener('click', onDirectoryAction);
    }
  };
}

function renderDirectory(container, catalogs, translation) {
  if (!container) return;
  if (catalogs.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('enumCatalogs.emptyDirectory');
    container.replaceChildren(empty);
    return;
  }

  const fragment = container.ownerDocument.createDocumentFragment();
  catalogs.forEach(catalog => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const name = container.ownerDocument.createElement('strong');
    name.textContent = catalog.displayName ?? '';
    const meta = container.ownerDocument.createElement('code');
    meta.textContent = catalog.key ?? '';
    const count = container.ownerDocument.createElement('small');
    count.textContent = `${translation.t('enumCatalogs.memberCount')}: ${catalog.memberCount ?? 0}`;
    identity.append(name, meta, count);
    const actions = container.ownerDocument.createElement('div');
    actions.className = 'fn-tenants__actions';
    const select = container.ownerDocument.createElement('button');
    select.type = 'button';
    select.className = 'layui-btn layui-btn-primary layui-btn-sm';
    select.dataset.enumCatalogsSelect = catalog.key;
    select.textContent = translation.t('enumCatalogs.select');
    actions.append(select);
    article.append(identity, actions);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function renderMembers(container, detail, translation) {
  if (!container) return;
  const title = container.ownerDocument.createElement('h3');
  title.textContent = translation.t('enumCatalogs.membersTitle', {
    name: detail?.displayName ?? ''
  });
  const fragment = container.ownerDocument.createDocumentFragment();
  fragment.append(title);
  const items = Array.isArray(detail?.members) ? detail.members : [];
  if (items.length === 0) {
    const empty = container.ownerDocument.createElement('p');
    empty.className = 'fn-tenants__empty';
    empty.textContent = translation.t('enumCatalogs.emptyMembers');
    fragment.append(empty);
    container.replaceChildren(fragment);
    return;
  }

  items.forEach(member => {
    const article = container.ownerDocument.createElement('article');
    const identity = container.ownerDocument.createElement('div');
    const label = container.ownerDocument.createElement('strong');
    label.textContent = member.label ?? '';
    const code = container.ownerDocument.createElement('code');
    code.textContent = member.code ?? '';
    const order = container.ownerDocument.createElement('small');
    order.textContent = `${translation.t('enumCatalogs.displayOrder')}: ${member.displayOrder ?? 0}`;
    identity.append(label, code, order);
    article.append(identity);
    fragment.append(article);
  });
  container.replaceChildren(fragment);
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-enum-catalogs-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.code ?? 'client.settings_enum_catalog_failed';
  panel.querySelector('span').textContent = problem?.title ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-enum-catalogs-problem]');
  if (panel) panel.hidden = true;
}
