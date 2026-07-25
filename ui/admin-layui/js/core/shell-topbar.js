/**
 * 绑定 Layui 顶栏折叠、刷新与全屏按钮。
 */
export function bindShellTopbar(root, options = {}) {
  const menuButton = root.querySelector('[data-shell-menu-toggle]');
  const refreshButton = root.querySelector('[data-shell-refresh]');
  const fullscreenButton = root.querySelector('[data-shell-fullscreen]');
  const themeButton = root.querySelector('[data-shell-theme-toggle]');
  let translate = key => key;
  let isFullscreen = Boolean(document.fullscreenElement);

  function updateMenuButton(settings) {
    if (!menuButton) {
      return;
    }

    const collapsed = settings.menuCollapsed === true;
    menuButton.setAttribute(
      'aria-label',
      translate(collapsed ? 'shell.expandMenu' : 'shell.collapseMenu')
    );
    const icon = menuButton.querySelector('.layui-icon');
    if (icon) {
      icon.className = `layui-icon ${collapsed ? 'layui-icon-spread-left' : 'layui-icon-shrink-right'}`;
    }
  }

  function updateFullscreenButton() {
    if (!fullscreenButton) {
      return;
    }

    fullscreenButton.setAttribute(
      'aria-label',
      translate(isFullscreen ? 'shell.fullscreenExit' : 'shell.fullscreenEnter')
    );
    const icon = fullscreenButton.querySelector('.layui-icon');
    if (icon) {
      icon.className = `layui-icon ${isFullscreen ? 'layui-icon-screen-restore' : 'layui-icon-screen-full'}`;
    }
  }

  function toggleMenu() {
    const settings = options.getSettings?.() ?? {};
    const next = { menuCollapsed: !settings.menuCollapsed };
    options.onSettingsChange?.(next);
    updateMenuButton({ ...settings, ...next });
  }

  function updateThemeButton(settings) {
    if (!themeButton) {
      return;
    }

    const isDark = settings.themeMode === 'dark';
    themeButton.setAttribute(
      'aria-label',
      translate(isDark ? 'shell.themeLight' : 'shell.themeDark')
    );
    const icon = themeButton.querySelector('.layui-icon');
    if (icon) {
      icon.className = `layui-icon ${isDark ? 'layui-icon-light' : 'layui-icon-moon'}`;
    }
  }

  function toggleTheme() {
    const settings = options.getSettings?.() ?? {};
    const themeMode = settings.themeMode === 'dark' ? 'light' : 'dark';
    options.onSettingsChange?.({ themeMode });
    updateThemeButton({ ...settings, themeMode });
  }

  async function toggleFullscreen() {
    try {
      if (!document.fullscreenElement) {
        await document.documentElement.requestFullscreen();
      } else {
        await document.exitFullscreen();
      }
    } catch {
      // 全屏不可用时静默忽略，避免阻断顶栏交互。
    }
  }

  function onFullscreenChange() {
    isFullscreen = Boolean(document.fullscreenElement);
    updateFullscreenButton();
  }

  const onMenuClick = () => toggleMenu();
  const onRefreshClick = () => options.onRefresh?.();
  const onThemeClick = () => toggleTheme();
  const onFullscreenClick = () => {
    void toggleFullscreen();
  };

  menuButton?.addEventListener('click', onMenuClick);
  refreshButton?.addEventListener('click', onRefreshClick);
  themeButton?.addEventListener('click', onThemeClick);
  fullscreenButton?.addEventListener('click', onFullscreenClick);
  document.addEventListener('fullscreenchange', onFullscreenChange);

  return {
    render(t, settings) {
      if (t) {
        translate = t;
      }

      const activeSettings = settings ?? options.getSettings?.() ?? {};
      updateMenuButton(activeSettings);
      updateThemeButton(activeSettings);
      if (refreshButton) {
        refreshButton.setAttribute('aria-label', translate('shell.refresh'));
      }
      updateFullscreenButton();
    },
    dispose() {
      menuButton?.removeEventListener('click', onMenuClick);
      refreshButton?.removeEventListener('click', onRefreshClick);
      themeButton?.removeEventListener('click', onThemeClick);
      fullscreenButton?.removeEventListener('click', onFullscreenClick);
      document.removeEventListener('fullscreenchange', onFullscreenChange);
    }
  };
}
