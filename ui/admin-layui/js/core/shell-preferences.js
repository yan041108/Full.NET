export {
  ART_SHELL_MAIN_COLORS,
  applyShellSettingsToDocument,
  createDefaultShellSettings,
  exportShellSettingsJson,
  patchShellSettings,
  readShellSettings,
  resetShellSettings,
  resolveCustomRadius
} from './shell-art-settings.js';

/** @deprecated 使用 readShellSettings */
export { readShellSettings as readShellPreferences } from './shell-art-settings.js';

/** @deprecated 使用 patchShellSettings */
export { patchShellSettings as patchShellPreferences } from './shell-art-settings.js';

/** @deprecated 使用 applyShellSettingsToDocument */
export { applyShellSettingsToDocument as applyShellPreferencesToDocument } from './shell-art-settings.js';

/** @deprecated 使用 createDefaultShellSettings */
export { createDefaultShellSettings as createDefaultShellPreferences } from './shell-art-settings.js';
