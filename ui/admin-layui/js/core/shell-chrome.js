/**
 * 根据壳层偏好切换 Layui 顶栏等已存在控件的可见性。 */
export function applyShellChrome(root, settings) {
  const shell = root.querySelector('[data-session-shell]');
  if (!shell) {
    return;
  }

  shell.dataset.fnShowLanguage = settings.showLanguage ? 'true' : 'false';
  shell.dataset.fnShowPageTabs = settings.showPageTabs ? 'true' : 'false';
  shell.dataset.fnShowBreadcrumb = settings.showBreadcrumb ? 'true' : 'false';

  root.querySelectorAll('[data-shell-chrome]').forEach(node => {
    const key = node.dataset.shellChrome;
    const visible = resolveChromeVisibility(key, settings);
    node.hidden = !visible;
    node.classList.toggle('is-shell-hidden', !visible);
  });
}

function resolveChromeVisibility(key, settings) {
  switch (key) {
    case 'language':
      return settings.showLanguage;
    case 'breadcrumb':
      return settings.showBreadcrumb;
    case 'menu-button':
      return settings.showMenuButton;
    case 'refresh-button':
      return settings.showRefreshButton;
    case 'fullscreen':
      return settings.showFullscreen;
    case 'search':
      return true;
    case 'notifications':
      return true;
    default:
      return true;
  }
}
