const storageKey = 'fullnet.admin.artShellSettings';
const legacyThemeKey = 'fullnet.admin.artTheme';
const legacyMenuKey = 'fullnet.admin.artMenuCollapsed';

export const ART_SHELL_MAIN_COLORS = [
  '#409eff',
  '#0d47a1',
  '#67c23a',
  '#e6a23c',
  '#f56c6c',
  '#9c27b0',
  '#00bcd4',
  '#ff9800'
];

const radiusMap = {
  0: '0px',
  0.25: '4px',
  0.5: '8px',
  0.75: '12px',
  1: '16px'
};

/** 与 Vue `createDefaultArtShellSettings` 对齐的 Layui 壳层偏好默认值。 */
export function createDefaultShellSettings() {
  return {
    themeMode: 'light',
    menuCollapsed: false,
    menuLayout: 'left',
    menuStyle: 'design',
    primaryColor: '#0d47a1',
    boxStyle: 'border',
    containerWidth: 'full',
    tabStyle: 'default',
    customRadius: '0.5',
    menuOpenWidth: 230,
    showMenuButton: true,
    showRefreshButton: true,
    showBreadcrumb: true,
    showPageTabs: true,
    showFullscreen: true,
    showLanguage: true,
    uniqueOpened: false,
    dualMenuShowText: false
  };
}

function loadLegacySettings() {
  const partial = {};
  const storedTheme = sessionStorage.getItem(legacyThemeKey);
  if (storedTheme === 'light' || storedTheme === 'dark') {
    partial.themeMode = storedTheme;
  }

  const storedMenu = sessionStorage.getItem(legacyMenuKey);
  if (storedMenu === '1' || storedMenu === '0') {
    partial.menuCollapsed = storedMenu === '1';
  }

  return partial;
}

export function resolveCustomRadius(value) {
  return radiusMap[value] ?? radiusMap['0.5'];
}

/** 从 sessionStorage 合并读取壳层偏好；与 Vue 共用 `fullnet.admin.artShellSettings`。 */
export function readShellSettings() {
  const defaults = createDefaultShellSettings();
  if (typeof sessionStorage === 'undefined') {
    return defaults;
  }

  const raw = sessionStorage.getItem(storageKey);
  if (!raw) {
    return { ...defaults, ...loadLegacySettings() };
  }

  try {
    const parsed = JSON.parse(raw);
    return { ...defaults, ...parsed };
  } catch {
    return { ...defaults, ...loadLegacySettings() };
  }
}

/** 局部更新壳层偏好并写回 sessionStorage。 */
export function patchShellSettings(partial) {
  const next = { ...readShellSettings(), ...partial };
  if (typeof sessionStorage !== 'undefined') {
    sessionStorage.setItem(storageKey, JSON.stringify(next));
  }

  return next;
}

export function resetShellSettings() {
  const next = createDefaultShellSettings();
  if (typeof sessionStorage !== 'undefined') {
    sessionStorage.setItem(storageKey, JSON.stringify(next));
  }

  return next;
}

export function exportShellSettingsJson() {
  return JSON.stringify(readShellSettings(), null, 2);
}

/** 将壳层偏好同步到文档根节点，供 Layui 与 Vue 共用 data-art-* 选择器。 */
export function applyShellSettingsToDocument(settings, root = document.documentElement) {
  root.dataset.artTheme = settings.themeMode;
  root.dataset.artMenuStyle = settings.menuStyle;
  root.dataset.artBoxStyle = settings.boxStyle;
  root.dataset.artContainerWidth = settings.containerWidth;
  root.dataset.artTabStyle = settings.tabStyle;
  root.dataset.artMenuLayout = settings.menuLayout;
  root.dataset.artDualMenuShowText = settings.dualMenuShowText ? 'true' : 'false';
  root.dataset.artMenuCollapsed = settings.menuCollapsed ? 'true' : 'false';
  root.dataset.fnMenuLayout = settings.menuLayout;
  root.dataset.fnDualMenuShowText = settings.dualMenuShowText ? 'true' : 'false';
  root.style.setProperty('--art-theme-color', settings.primaryColor);
  root.style.setProperty('--fullnet-color-accent', settings.primaryColor);
  root.style.setProperty('--fn-menu-open-width', `${settings.menuOpenWidth}px`);
  root.style.setProperty('--art-custom-radius', resolveCustomRadius(settings.customRadius));
}
