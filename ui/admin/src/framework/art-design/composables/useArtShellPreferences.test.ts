import { beforeEach, describe, expect, it } from 'vitest';
import {
  applyArtShellSettingsToDocument,
  createDefaultArtShellSettings
} from './artShellSettingsDefaults';
import { useArtShellPreferences } from './useArtShellPreferences';

describe('useArtShellPreferences', () => {
  beforeEach(() => {
    sessionStorage.clear();
    document.documentElement.removeAttribute('data-art-theme');
    document.documentElement.classList.remove('dark');
    document.documentElement.style.removeProperty('--el-color-primary');
    useArtShellPreferences().resetSettings();
  });

  it('持久化主题色与菜单宽度变更', () => {
    const { patchSettings, settings } = useArtShellPreferences();

    patchSettings({
      primaryColor: '#67c23a',
      menuOpenWidth: 260
    });

    expect(settings.value.primaryColor).toBe('#67c23a');
    expect(settings.value.menuOpenWidth).toBe(260);
    expect(sessionStorage.getItem('fullnet.admin.artShellSettings')).toContain('#67c23a');
    expect(document.documentElement.style.getPropertyValue('--el-color-primary')).toBe('#67c23a');
    expect(document.documentElement.style.getPropertyValue('--art-menu-open-width')).toBe('260px');
  });

  it('重置配置恢复默认值', () => {
    const { patchSettings, resetSettings, settings } = useArtShellPreferences();

    patchSettings({
      themeMode: 'dark',
      boxStyle: 'shadow',
      showPageTabs: false
    });
    resetSettings();

    expect(settings.value).toEqual(createDefaultArtShellSettings());
  });

  it('导出配置 JSON 包含全部键', () => {
    const { exportSettingsJson } = useArtShellPreferences();
    const parsed = JSON.parse(exportSettingsJson()) as Record<string, unknown>;

    expect(parsed.menuLayout).toBe('left');
    expect(parsed.tabStyle).toBe('default');
    expect(parsed.showLanguage).toBe(true);
    expect(parsed.dualMenuShowText).toBe(false);
  });
});

describe('applyArtShellSettingsToDocument', () => {
  it('写入 data 属性与圆角变量', () => {
    applyArtShellSettingsToDocument({
      ...createDefaultArtShellSettings(),
      customRadius: '0.75',
      containerWidth: 'boxed'
    });

    expect(document.documentElement.dataset.artContainerWidth).toBe('boxed');
    expect(document.documentElement.style.getPropertyValue('--art-custom-radius')).toBe('12px');
  });

  it('写入双栏菜单文字显示状态', () => {
    applyArtShellSettingsToDocument({
      ...createDefaultArtShellSettings(),
      dualMenuShowText: true
    });

    expect(document.documentElement.dataset.artDualMenuShowText).toBe('true');
  });

  it('暗色主题同步 html.dark 以启用 Element Plus 暗色变量', () => {
    applyArtShellSettingsToDocument({
      ...createDefaultArtShellSettings(),
      themeMode: 'dark'
    });

    expect(document.documentElement.dataset.artTheme).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);

    applyArtShellSettingsToDocument(createDefaultArtShellSettings());

    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });
});
