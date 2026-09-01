import { computed, ref } from 'vue';
import {
  applyArtShellSettingsToDocument,
  createDefaultArtShellSettings,
  type ArtShellSettings,
  type ArtThemeMode
} from './artShellSettingsDefaults';

export type { ArtThemeMode, ArtShellSettings };

const storageKey = 'fullnet.admin.artShellSettings';
const legacyThemeKey = 'fullnet.admin.artTheme';
const legacyMenuKey = 'fullnet.admin.artMenuCollapsed';

const settings = ref<ArtShellSettings>(createDefaultArtShellSettings());
let hydrated = false;

/** 将当前壳层偏好写回 sessionStorage，保持刷新后仍能沿用本次会话设置。 */
function persist(): void {
  sessionStorage.setItem(storageKey, JSON.stringify(settings.value));
}

/** 兼容早期仅保存主题和菜单折叠状态的旧键。 */
function loadLegacySettings(): Partial<ArtShellSettings> {
  const partial: Partial<ArtShellSettings> = {};
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

/** 首次使用时装载壳层偏好，并对白名单字段做失败关闭校验和默认值修正。 */
function hydrateSettings(): void {
  if (hydrated || typeof window === 'undefined') {
    return;
  }

  hydrated = true;
  const raw = sessionStorage.getItem(storageKey);
  const validMenuLayouts = new Set<ArtShellSettings['menuLayout']>([
    'left',
    'top',
    'top-left',
    'dual-menu'
  ]);
  if (raw) {
    try {
      const parsed = JSON.parse(raw) as Partial<ArtShellSettings>;
      settings.value = {
        ...createDefaultArtShellSettings(),
        ...parsed
      };
    } catch {
      settings.value = {
        ...createDefaultArtShellSettings(),
        ...loadLegacySettings()
      };
    }
  } else {
    settings.value = {
      ...createDefaultArtShellSettings(),
      ...loadLegacySettings()
    };
  }

  if (!validMenuLayouts.has(settings.value.menuLayout)) {
    settings.value.menuLayout = 'left';
  }

  const validMenuStyles = new Set<ArtShellSettings['menuStyle']>([
    'design',
    'light',
    'dark'
  ]);
  if (!validMenuStyles.has(settings.value.menuStyle)) {
    settings.value.menuStyle = 'design';
  }

  if (
    !Number.isFinite(settings.value.menuOpenWidth)
    || settings.value.menuOpenWidth < 180
    || settings.value.menuOpenWidth > 320
  ) {
    settings.value.menuOpenWidth = createDefaultArtShellSettings().menuOpenWidth;
  }

  if (typeof settings.value.menuCollapsed !== 'boolean') {
    settings.value.menuCollapsed = createDefaultArtShellSettings().menuCollapsed;
  }

  applyArtShellSettingsToDocument(settings.value);
  persist();
}

/** 合并局部壳层偏好，同时立即同步到文档变量与会话存储。 */
function patchSettings(partial: Partial<ArtShellSettings>): void {
  hydrateSettings();
  settings.value = {
    ...settings.value,
    ...partial
  };
  persist();
  applyArtShellSettingsToDocument(settings.value);
}

/** 壳层偏好持久化在 sessionStorage；禁止写入 Token 或认证状态。 */
export function useArtShellPreferences() {
  hydrateSettings();

  const themeMode = computed(() => settings.value.themeMode);
  const menuCollapsed = computed(() => settings.value.menuCollapsed);

  /** 显式应用亮色或暗色主题。 */
  function applyTheme(mode: ArtThemeMode): void {
    patchSettings({ themeMode: mode });
  }

  /** 在亮色与暗色之间切换主题。 */
  function toggleTheme(): void {
    applyTheme(settings.value.themeMode === 'light' ? 'dark' : 'light');
  }

  /** 切换侧栏折叠状态。 */
  function toggleMenuCollapsed(): void {
    patchSettings({ menuCollapsed: !settings.value.menuCollapsed });
  }

  /** 恢复到默认壳层设置。 */
  function resetSettings(): void {
    patchSettings(createDefaultArtShellSettings());
  }

  /** 导出当前壳层设置 JSON，供调试或偏好迁移使用。 */
  function exportSettingsJson(): string {
    return JSON.stringify(settings.value, null, 2);
  }

  return {
    settings,
    themeMode,
    menuCollapsed,
    applyTheme,
    toggleTheme,
    toggleMenuCollapsed,
    patchSettings,
    resetSettings,
    exportSettingsJson
  };
}
