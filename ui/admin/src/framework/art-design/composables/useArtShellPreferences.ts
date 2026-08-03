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

function persist(): void {
  sessionStorage.setItem(storageKey, JSON.stringify(settings.value));
}

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

  function applyTheme(mode: ArtThemeMode): void {
    patchSettings({ themeMode: mode });
  }

  function toggleTheme(): void {
    applyTheme(settings.value.themeMode === 'light' ? 'dark' : 'light');
  }

  function toggleMenuCollapsed(): void {
    patchSettings({ menuCollapsed: !settings.value.menuCollapsed });
  }

  function resetSettings(): void {
    patchSettings(createDefaultArtShellSettings());
  }

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
