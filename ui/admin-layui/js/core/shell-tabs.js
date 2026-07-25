import { buildFlatNavigationItems } from './shell-navigation-groups.js';

/** 根据当前路径与授权导航生成标签页集合。 */
export function upsertShellTab(tabs, navigation, activePath) {
  const active = navigation.find(item => item.path === activePath);
  if (!active) {
    return tabs;
  }

  if (tabs.some(tab => tab.path === active.path)) {
    return tabs;
  }

  return [...tabs, { path: active.path, title: active.title }];
}

/** 关闭标签页后返回应激活的路径。 */
export function closeShellTab(tabs, closingPath, activePath) {
  if (tabs.length <= 1) {
    return { tabs, nextPath: activePath };
  }

  const index = tabs.findIndex(tab => tab.path === closingPath);
  if (index < 0) {
    return { tabs, nextPath: activePath };
  }

  const nextTabs = tabs.filter(tab => tab.path !== closingPath);
  if (closingPath !== activePath) {
    return { tabs: nextTabs, nextPath: activePath };
  }

  const fallback = nextTabs[Math.max(0, index - 1)] ?? nextTabs[0];
  return { tabs: nextTabs, nextPath: fallback?.path ?? '/' };
}

/**
 * 管理 Layui 壳层多标签页渲染与关闭逻辑。
 */
export function createShellTabsController(root, options = {}) {
  const container = root.querySelector('[data-page-tabs]');
  let tabs = [];
  let translate = key => key;

  function navigateTo(path) {
    if (typeof options.onNavigate === 'function') {
      options.onNavigate(path);
      return;
    }

    window.location.hash = path;
  }

  function render({ navigation, activePath, t, settings }) {
    if (t) {
      translate = t;
    }

    if (!container) {
      return;
    }

    const showPageTabs = settings?.showPageTabs !== false;
    const flatNavigation = buildFlatNavigationItems(navigation ?? [], translate);
    tabs = upsertShellTab(tabs, flatNavigation, activePath);

    if (!showPageTabs || tabs.length === 0) {
      container.hidden = true;
      container.replaceChildren();
      return;
    }

    const tabStyle = settings?.tabStyle ?? 'default';
    container.hidden = false;
    container.className = `fn-page-tabs fn-page-tabs--${tabStyle}`;
    container.setAttribute('role', 'tablist');
    container.setAttribute('aria-label', translate('shell.pageTabs'));

    const ownerDocument = container.ownerDocument;
    const fragment = ownerDocument.createDocumentFragment();
    tabs.forEach(tab => {
      const button = ownerDocument.createElement('button');
      button.type = 'button';
      button.className = 'fn-page-tabs__item';
      button.classList.toggle('is-active', tab.path === activePath);
      button.setAttribute('role', 'tab');
      button.setAttribute('aria-selected', tab.path === activePath ? 'true' : 'false');
      button.dataset.route = tab.path;
      button.addEventListener('click', () => navigateTo(tab.path));

      const title = ownerDocument.createElement('span');
      title.textContent = tab.title;
      button.append(title);

      if (tabs.length > 1) {
        const close = ownerDocument.createElement('span');
        close.className = 'fn-page-tabs__close';
        close.setAttribute('aria-hidden', 'true');
        close.title = translate('shell.closeTab', { title: tab.title });
        close.textContent = '×';
        close.addEventListener('click', event => {
          event.stopPropagation();
          const result = closeShellTab(tabs, tab.path, activePath);
          tabs = result.tabs;
          if (result.nextPath !== activePath) {
            navigateTo(result.nextPath);
            return;
          }

          render({ navigation, activePath, t: translate, settings });
        });
        button.append(close);
      }

      fragment.append(button);
    });

    container.replaceChildren(fragment);
  }

  return {
    render,
    reset() {
      tabs = [];
      if (container) {
        container.hidden = true;
        container.replaceChildren();
      }
    }
  };
}
