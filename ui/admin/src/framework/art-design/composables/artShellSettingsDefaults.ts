export type ArtThemeMode = 'light' | 'dark';
export type ArtMenuLayout = 'left' | 'top' | 'top-left' | 'dual-menu';
export type ArtMenuStyle = 'design' | 'light' | 'dark';
export type ArtBoxStyle = 'border' | 'shadow';
export type ArtContainerWidth = 'full' | 'boxed';
export type ArtTabStyle = 'default' | 'card' | 'google';
export type ArtCustomRadius = '0' | '0.25' | '0.5' | '0.75' | '1';

/** Art 壳层可持久化的外观与布局设置。 */
export interface ArtShellSettings {
  themeMode: ArtThemeMode;
  menuCollapsed: boolean;
  menuLayout: ArtMenuLayout;
  menuStyle: ArtMenuStyle;
  primaryColor: string;
  boxStyle: ArtBoxStyle;
  containerWidth: ArtContainerWidth;
  tabStyle: ArtTabStyle;
  customRadius: ArtCustomRadius;
  menuOpenWidth: number;
  showMenuButton: boolean;
  showRefreshButton: boolean;
  showBreadcrumb: boolean;
  showPageTabs: boolean;
  showFullscreen: boolean;
  showLanguage: boolean;
  uniqueOpened: boolean;
  dualMenuShowText: boolean;
}

/** 壳层主题主色候选目录，供设置面板和文档变量同步复用。 */
export const ART_SHELL_MAIN_COLORS = [
  '#409eff',
  '#0d47a1',
  '#67c23a',
  '#e6a23c',
  '#f56c6c',
  '#9c27b0',
  '#00bcd4',
  '#ff9800'
] as const;

/** 将枚举化圆角档位映射为真正写入 CSS 变量的像素值。 */
const radiusMap: Record<ArtCustomRadius, string> = {
  '0': '0px',
  '0.25': '4px',
  '0.5': '8px',
  '0.75': '12px',
  '1': '16px'
};

/** 创建一份稳定的壳层默认设置，供首启、重置和存储损坏回退共用。 */
export function createDefaultArtShellSettings(): ArtShellSettings {
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

/** 将圆角档位解析为 CSS 长度；未知值时回退到默认中等圆角。 */
export function resolveCustomRadius(value: ArtCustomRadius): string {
  return radiusMap[value] ?? radiusMap['0.5'];
}

/** 把壳层设置投影到 document，统一驱动主题类名、数据属性和 CSS 变量。 */
export function applyArtShellSettingsToDocument(settings: ArtShellSettings): void {
  if (typeof document === 'undefined') {
    return;
  }

  const root = document.documentElement;
  root.dataset.artTheme = settings.themeMode;
  root.classList.toggle('dark', settings.themeMode === 'dark');
  root.dataset.artMenuStyle = settings.menuStyle;
  root.dataset.artBoxStyle = settings.boxStyle;
  root.dataset.artContainerWidth = settings.containerWidth;
  root.dataset.artTabStyle = settings.tabStyle;
  root.dataset.artMenuLayout = settings.menuLayout;
  root.dataset.artDualMenuShowText = settings.dualMenuShowText ? 'true' : 'false';
  root.style.setProperty('--art-theme-color', settings.primaryColor);
  root.style.setProperty('--el-color-primary', settings.primaryColor);
  root.style.setProperty('--art-menu-open-width', `${settings.menuOpenWidth}px`);
  root.style.setProperty('--art-custom-radius', resolveCustomRadius(settings.customRadius));
}
