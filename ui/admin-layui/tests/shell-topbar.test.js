import { describe, expect, it, beforeEach, vi } from 'vitest';
import { bindShellTopbar } from '../js/core/shell-topbar.js';
import { createDefaultShellSettings } from '../js/core/shell-art-settings.js';

describe('shell-topbar', () => {
  beforeEach(() => {
    document.body.innerHTML = `
      <div id="root">
        <button data-shell-menu-toggle><i class="layui-icon layui-icon-shrink-right"></i></button>
        <button data-shell-refresh></button>
        <button data-shell-fullscreen><i class="layui-icon layui-icon-screen-full"></i></button>
      </div>
    `;
  });

  it('切换侧栏折叠偏好', () => {
    const root = document.getElementById('root');
    const changes = [];
    const settings = { ...createDefaultShellSettings() };
    const topbar = bindShellTopbar(root, {
      getSettings: () => settings,
      onSettingsChange: partial => {
        Object.assign(settings, partial);
        changes.push(partial);
      }
    });
    const t = key => key;
    topbar.render(t, settings);

    root.querySelector('[data-shell-menu-toggle]').click();
    expect(changes).toEqual([{ menuCollapsed: true }]);
    expect(root.querySelector('[data-shell-menu-toggle]').getAttribute('aria-label')).toBe('shell.expandMenu');
  });

  it('触发刷新回调', () => {
    const root = document.getElementById('root');
    const onRefresh = vi.fn();
    bindShellTopbar(root, { onRefresh });
    root.querySelector('[data-shell-refresh]').click();
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it('切换主题模式偏好', () => {
    document.body.innerHTML = `
      <div id="root">
        <button data-shell-theme-toggle><i class="layui-icon layui-icon-moon"></i></button>
      </div>
    `;
    const root = document.getElementById('root');
    const settings = { themeMode: 'light' };
    const changes = [];
    const topbar = bindShellTopbar(root, {
      getSettings: () => settings,
      onSettingsChange: partial => {
        Object.assign(settings, partial);
        changes.push(partial);
      }
    });
    topbar.render(key => key, settings);
    root.querySelector('[data-shell-theme-toggle]').click();
    expect(changes).toEqual([{ themeMode: 'dark' }]);
  });
});

describe('shell-chrome topbar buttons', () => {
  beforeEach(() => {
    document.body.innerHTML = `
      <div id="root">
        <div data-session-shell>
          <button data-shell-chrome="menu-button"></button>
          <button data-shell-chrome="refresh-button"></button>
          <button data-shell-chrome="fullscreen"></button>
        </div>
      </div>
    `;
  });

  it('根据设置隐藏顶栏按钮', async () => {
    const { applyShellChrome } = await import('../js/core/shell-chrome.js');
    const root = document.getElementById('root');
    applyShellChrome(root, {
      ...createDefaultShellSettings(),
      showMenuButton: false,
      showRefreshButton: false,
      showFullscreen: false
    });

    expect(root.querySelector('[data-shell-chrome="menu-button"]').hidden).toBe(true);
    expect(root.querySelector('[data-shell-chrome="refresh-button"]').hidden).toBe(true);
    expect(root.querySelector('[data-shell-chrome="fullscreen"]').hidden).toBe(true);
  });
});
