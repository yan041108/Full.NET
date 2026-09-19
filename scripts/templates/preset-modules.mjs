/**
 * Composition preset module closure.
 */
export const PRESET_MODULE_CLOSURE = {
  minimal: ['Identity', 'Tenancy', 'Settings', 'Organization'],
  platform: ['Identity', 'Tenancy', 'Settings', 'Organization', 'Auditing', 'Files', 'Notifications', 'Calendar', 'Platform', 'Regions', 'Jobs', 'Messaging', 'ObservabilityAdmin', 'Mqtt', 'Cryptography'],
  saas: ['Identity', 'Tenancy', 'Settings', 'Organization', 'Auditing', 'Files', 'Notifications', 'Calendar', 'Platform', 'Regions', 'Jobs', 'Messaging', 'ObservabilityAdmin', 'Mqtt', 'Cryptography', 'Payments', 'Webhooks'],
  enterprise: ['Identity', 'Tenancy', 'Settings', 'Organization', 'Auditing', 'Files', 'Notifications', 'Calendar', 'Platform', 'Regions', 'Jobs', 'Messaging', 'ObservabilityAdmin', 'Mqtt', 'Cryptography', 'Webhooks', 'Workflow', 'ImportExport', 'Reporting', 'Printing', 'EnterpriseRequest'],
};
export const VALID_PRESETS = Object.keys(PRESET_MODULE_CLOSURE);
export const OWNER_KEY_PATTERN = /^[a-z][a-z0-9-]{1,31}$/;
export const RESERVED_OWNER_KEYS = new Set(['fn', 'sys']);
export function resolvePresetModules(preset) {
  const key = preset.toLowerCase();
  if (!VALID_PRESETS.includes(key)) throw new Error('Unknown preset: ' + preset);
  return PRESET_MODULE_CLOSURE[key];
}
export function validateOwnerKey(ownerKey) {
  if (!OWNER_KEY_PATTERN.test(ownerKey)) throw new Error('Invalid owner-key');
  if (RESERVED_OWNER_KEYS.has(ownerKey)) throw new Error('Reserved owner-key');
}
