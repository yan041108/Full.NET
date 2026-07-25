import {
  applyShellSettingsToDocument,
  patchShellSettings,
  readShellSettings
} from './shell-art-settings.js';
import {
  buildFlatNavigationItems,
  buildShellNavigationGroups,
  resolveActiveGroupId
} from './shell-navigation-groups.js';

function createNavLink(ownerDocument, item, activePath) {
  const link = ownerDocument.createElement('a');
  link.href = `#${item.path}`;
  link.dataset.route = item.path;
  link.classList.toggle('is-active', item.path === activePath);
  if (item.path === activePath) {
    link.setAttribute('aria-current', 'page');
  }

  const icon = ownerDocument.createElement('i');
  icon.className = `layui-icon ${item.iconClass}`;
  icon.setAttribute('aria-hidden', 'true');
  const title = ownerDocument.createElement('span');
  title.textContent = item.title;
  link.append(icon, title);
  return link;
}

function renderSidebarNavigation(container, items, activePath, t, options = {}) {
  const ownerDocument = container.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  if (options.showGroupLabel !== false) {
    const group = ownerDocument.createElement('span');
    group.className = 'fn-nav__group';
    group.textContent = t('shell.managementDomain');
    fragment.append(group);
  }

  items.forEach((item, index) => {
    const link = createNavLink(ownerDocument, item, activePath);
    const order = ownerDocument.createElement('em');
    order.textContent = String(index + 1).padStart(2, '0');
    link.append(order);
    fragment.append(link);
  });
  container.replaceChildren(fragment);
}

function renderHorizontalNavigation(container, items, activePath) {
  const ownerDocument = container.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  items.forEach(item => {
    fragment.append(createNavLink(ownerDocument, item, activePath));
  });
  container.replaceChildren(fragment);
}

function renderMixedNavigation(container, groups, activeGroupId, onSelectGroup) {
  const ownerDocument = container.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  groups.forEach(group => {
    const button = ownerDocument.createElement('button');
    button.type = 'button';
    button.className = 'fn-mixed-menu__item';
    button.classList.toggle('is-active', group.id === activeGroupId);
    button.dataset.groupId = group.id;
    button.addEventListener('click', () => onSelectGroup(group.id));

    const icon = ownerDocument.createElement('i');
    icon.className = `layui-icon ${group.iconClass}`;
    icon.setAttribute('aria-hidden', 'true');
    const title = ownerDocument.createElement('span');
    title.textContent = group.title;
    button.append(icon, title);
    fragment.append(button);
  });
  container.replaceChildren(fragment);
}

function renderDualRail(container, groups, activeGroupId, onSelectGroup) {
  const ownerDocument = container.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  groups.forEach(group => {
    const button = ownerDocument.createElement('button');
    button.type = 'button';
    button.className = 'fn-dual-rail__item';
    button.classList.toggle('is-active', group.id === activeGroupId);
    button.dataset.groupId = group.id;
    button.title = group.title;
    button.setAttribute('aria-label', group.title);
    button.addEventListener('click', () => onSelectGroup(group.id));

    const icon = ownerDocument.createElement('i');
    icon.className = `layui-icon ${group.iconClass}`;
    icon.setAttribute('aria-hidden', 'true');
    const title = ownerDocument.createElement('span');
    title.className = 'fn-dual-rail__text';
    title.textContent = group.title;
    button.append(icon, title);
    fragment.append(button);
  });
  container.replaceChildren(fragment);
}

/**
 * 管理 Layui 壳层菜单布局渲染与分组状态。
 * 移动端由 CSS 统一回退左侧抽屉式侧栏。
 */
export function createShellLayoutController(root, options = {}) {
  const shell = root.querySelector('[data-session-shell]');
  const sidebarNav = root.querySelector('[data-navigation]');
  const horizontalNav = root.querySelector('[data-horizontal-menu]');
  const mixedNav = root.querySelector('[data-mixed-menu]');
  const dualRail = root.querySelector('[data-dual-rail]');
  const dualRailNav = root.querySelector('[data-dual-rail-nav]');
  const primarySidebar = root.querySelector('[data-primary-sidebar]');
  let preferences = readShellSettings();
  let activeGroupId = '';
  let groups = [];
  let flatItems = [];
  let translate = null;

  function applyLayoutClass() {
    if (!shell) {
      return;
    }

    shell.classList.remove(
      'fn-shell--layout-left',
      'fn-shell--layout-top',
      'fn-shell--layout-top-left',
      'fn-shell--layout-dual-menu'
    );
    shell.classList.add(`fn-shell--layout-${preferences.menuLayout}`);
    shell.classList.toggle('fn-shell--menu-collapsed', preferences.menuCollapsed === true);
    applyShellSettingsToDocument(preferences);
  }

  function navigateTo(path) {
    if (typeof options.onNavigate === 'function') {
      options.onNavigate(path);
      return;
    }

    window.location.hash = path;
  }

  function selectGroup(groupId) {
    activeGroupId = groupId;
    const group = groups.find(item => item.id === groupId);
    if (!group) {
      return;
    }

    const currentPath = options.getActivePath?.() ?? '/';
    if (!group.items.some(item => item.path === currentPath)) {
      const nextPath = group.items[0]?.path;
      if (nextPath) {
        navigateTo(nextPath);
      }
    }

    render({
      navigation: options.getNavigation?.() ?? [],
      activePath: currentPath,
      t: translate
    });
  }

  function updatePreferences(partial) {
    preferences = patchShellSettings(partial);
    applyLayoutClass();
    render({
      navigation: options.getNavigation?.() ?? [],
      activePath: options.getActivePath?.() ?? '/',
      t: translate
    });
    if (typeof options.onSettingsChange === 'function') {
      options.onSettingsChange(preferences);
    }
  }

  function render({ navigation, activePath, t }) {
    if (t) {
      translate = t;
    }

    if (!translate || !sidebarNav) {
      return;
    }

    const activeTranslate = translate;
    flatItems = buildFlatNavigationItems(navigation, activeTranslate);
    groups = buildShellNavigationGroups(navigation, activeTranslate);
    activeGroupId = resolveActiveGroupId(groups, activePath);
    applyLayoutClass();

    const layout = preferences.menuLayout;
    const activeGroup = groups.find(group => group.id === activeGroupId);
    const sidebarItems = layout === 'left' ? flatItems : activeGroup?.items ?? flatItems;

    renderSidebarNavigation(sidebarNav, sidebarItems, activePath, activeTranslate, {
      showGroupLabel: layout === 'left'
    });

    if (horizontalNav) {
      horizontalNav.hidden = layout !== 'top';
      if (layout === 'top') {
        renderHorizontalNavigation(horizontalNav, flatItems, activePath);
      }
    }

    if (mixedNav) {
      mixedNav.hidden = layout !== 'top-left';
      if (layout === 'top-left') {
        renderMixedNavigation(mixedNav, groups, activeGroupId, selectGroup);
      }
    }

    if (dualRail && dualRailNav) {
      dualRail.hidden = layout !== 'dual-menu';
      if (layout === 'dual-menu') {
        renderDualRail(dualRailNav, groups, activeGroupId, selectGroup);
      }
    }

    if (primarySidebar) {
      primarySidebar.classList.toggle(
        'fn-sidebar--without-brand',
        layout === 'dual-menu'
      );
      primarySidebar.classList.toggle(
        'fn-sidebar--collapsed',
        preferences.menuCollapsed === true
      );
    }
  }

  applyLayoutClass();

  return {
    render,
    selectGroup,
    updatePreferences,
    getPreferences: () => ({ ...preferences }),
    getSettings: () => ({ ...preferences })
  };
}
