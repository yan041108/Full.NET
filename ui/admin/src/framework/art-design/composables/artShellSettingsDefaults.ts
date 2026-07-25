export type ArtThemeMode = 'light' | 'dark';
export type ArtMenuLayout = 'left' | 'top' | 'top-left' | 'dual-menu';
export type ArtMenuStyle = 'design' | 'light' | 'dark';
export type ArtBoxStyle = 'border' | 'shadow';
export type ArtContainerWidth = 'full' | 'boxed';
export type ArtTabStyle = 'default' | 'card' | 'google';
export type ArtCustomRadius = '0' | '0.25' | '0.5' | '0.75' | '1';

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

const radiusMap: Record<ArtCustomRadius, string> = {
  '0': '0px',
  '0.25': '4px',
  '0.5': '8px',
  '0.75': '12px',
  '1': '16px'
};

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

export function resolveCustomRadius(value: ArtCustomRadius): string {
  return radiusMap[value] ?? radiusMap['0.5'];
}

export function applyArtShellSettingsToDocument(settings: ArtShellSettings): void {
  if (typeof document === 'undefined') {
    return;
  }

  const root = document.documentElement;
  root.dataset.artTheme = settings.themeMode;
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
