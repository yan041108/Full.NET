import { describe, expect, it, beforeEach } from 'vitest';
import {
  applyShellSettingsToDocument,
  createDefaultShellSettings,
  patchShellSettings,
  readShellSettings,
  resetShellSettings
} from '../js/core/shell-art-settings.js';

describe('shell-art-settings', () => {
  beforeEach(() => {
    sessionStorage.clear();
    document.documentElement.removeAttribute('data-art-theme');
    document.documentElement.removeAttribute('data-fn-menu-layout');
    document.documentElement.style.removeProperty('--art-theme-color');
  });

  it('读写完整壳层偏好并与 Vue 共用 storage 键', () => {
    const next = patchShellSettings({
      themeMode: 'dark',
      menuLayout: 'top-left',
      primaryColor: '#67c23a',
      showLanguage: false
    });

    expect(readShellSettings().themeMode).toBe('dark');
    expect(readShellSettings().menuLayout).toBe('top-left');
    expect(readShellSettings().primaryColor).toBe('#67c23a');
    expect(readShellSettings().showLanguage).toBe(false);
    expect(sessionStorage.getItem('fullnet.admin.artShellSettings')).toContain('"themeMode":"dark"');
    expect(next.menuOpenWidth).toBe(createDefaultShellSettings().menuOpenWidth);
  });

  it('将主题与布局同步到 document 根节点', () => {
    const settings = patchShellSettings({
      themeMode: 'dark',
      menuLayout: 'dual-menu',
      dualMenuShowText: true,
      primaryColor: '#409eff',
      menuOpenWidth: 260,
      customRadius: '0.75'
    });
    applyShellSettingsToDocument(settings);

    expect(document.documentElement.dataset.artTheme).toBe('dark');
    expect(document.documentElement.dataset.fnMenuLayout).toBe('dual-menu');
    expect(document.documentElement.dataset.fnDualMenuShowText).toBe('true');
    expect(document.documentElement.style.getPropertyValue('--art-theme-color')).toBe('#409eff');
    expect(document.documentElement.style.getPropertyValue('--fn-menu-open-width')).toBe('260px');
    expect(document.documentElement.style.getPropertyValue('--art-custom-radius')).toBe('12px');
  });

  it('重置配置恢复默认值', () => {
    patchShellSettings({ themeMode: 'dark', menuLayout: 'top' });
    const next = resetShellSettings();
    expect(next).toEqual(createDefaultShellSettings());
    expect(readShellSettings().themeMode).toBe('light');
  });
});
