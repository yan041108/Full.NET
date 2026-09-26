/**
 * 迁移脚本与模块归属推断（F01 preset 闭包）。Shared 脚本对所有预设执行。
 */
import { PRESET_MODULE_CLOSURE } from './preset-modules.mjs';

/** 官方模块名，长名优先匹配脚本 stem。 */
export const OFFICIAL_MODULE_NAMES = [...new Set(Object.values(PRESET_MODULE_CLOSURE).flat())]
  .sort((left, right) => right.length - left.length);

/** 不绑定单一业务模块、所有预设都必须执行的脚本 stem。 */
const SHARED_STEMS = new Set([
  'Foundation',
  'AuthorizationContext',
  'LocalizationPreferences',
  'SuperAdministrator',
  'SuperAdministratorAuditActor',
  'SeedExecutionAudit',
  'UuidBinaryExpand',
  'UuidBinaryContract',
  'NamingExpand',
  'NamingContract',
  'OutboxDeadLetter',
  'MessagingOutboxInboxExpand',
  'MessagingDomainAudit',
  'MessagingStreamOwnership',
  'MessagingStreamOwnershipConvergence',
  'MessagingDomainAuditRequestedOutcome',
  'PublishedModuleUuidStorage',
  'ExternalSideEffectUnknownState',
  'TaskExecutionLease',
  'HostScopedManagementNavigationPermissions',
  'HostNavigationMenuPermissionScope',
  'AuthenticationAuditContext',
  'SuperAdministratorGrantRevokePermissions',
]);

const STEM_MODULE_OVERRIDES = new Map([
  ['HostRoleDataScope', 'Identity'],
  ['IdentityHostUserProfileAuthority', 'Identity'],
  ['DemoEnterpriseRequest', 'EnterpriseRequest'],
  ['CodeGenerationTemplate', 'Platform'],
  ['CodeGenerationRun', 'Platform'],
  ['CodeGenerationApply', 'Platform'],
  ['CodeGenerationRollback', 'Platform'],
  ['CodeGenerationTemplateActionPermissions', 'Platform'],
  ['DocumentHostFoundation', 'Platform'],
  ['DocumentAdminNetParity', 'Platform'],
  ['DocumentHostDownloadPermission', 'Platform'],
  ['DocumentItemActionPermissions', 'Platform'],
  ['DocumentCategoryActionPermissions', 'Platform'],
  ['DocumentTagActionPermissions', 'Platform'],
  ['DocumentHostRollbackVersionPermission', 'Platform'],
  ['DocumentVersionDeletionAudit', 'Platform'],
  ['DocumentHostDeleteVersionPermission', 'Platform'],
  ['DocumentAccessLog', 'Platform'],
  ['DocumentHostAccessLogPermission', 'Platform'],
  ['DocumentPreviewTask', 'Platform'],
  ['DocumentHostPreviewTaskPermission', 'Platform'],
  ['DocumentTagHotRecommended', 'Platform'],
  ['DocumentVersionRetentionSetting', 'Platform'],
  ['DataApprovalRequest', 'Workflow'],
  ['DataApprovalScenario', 'Workflow'],
  ['DataApprovalRequestRecovery', 'Workflow'],
  ['DataApprovalRequestApplication', 'Workflow'],
  ['SerialNumberRuleDisableApprovalPermission', 'Platform'],
  ['SerialNumberRuleActionPermissions', 'Platform'],
  ['SerialNumbers', 'Platform'],
  ['PaymentMerchantConfig', 'Payments'],
  ['PaymentPermissions', 'Payments'],
  ['PaymentNotifyRefund', 'Payments'],
  ['PaymentRefundPermissions', 'Payments'],
  ['PaymentAlipayPagePay', 'Payments'],
  ['GoViewProject', 'Platform'],
  ['GoViewProjectPermissions', 'Platform'],
  ['K3CloudFoundation', 'Platform'],
  ['K3CloudPermissions', 'Platform'],
  ['OcrFoundation', 'Platform'],
  ['OcrPermissions', 'Platform'],
  ['DingTalkApprovalSync', 'Platform'],
  ['DingTalkApprovalSyncActionPermissions', 'Platform'],
  ['WeChatMiniProgramBinding', 'Platform'],
  ['WeChatMiniProgramBindingPermissions', 'Platform'],
  ['TenantResourceFile', 'Files'],
  ['AiModelConfig', 'Ai'],
  ['AiModelConfigPermission', 'Ai'],
  ['AiChatSession', 'Ai'],
  ['AiChatSessionPermission', 'Ai'],
  ['AiAgentToolCall', 'Ai'],
  ['AiAgentToolPermission', 'Ai'],
  ['AiQuotaReservation', 'Ai'],
  ['AiChatGenerationLease', 'Ai'],
  ['AiToolExecutionStatus', 'Ai'],
  ['AiOperationBudget', 'Ai'],
  ['AiAgentRuntime', 'Ai'],
  ['AiAgentWorkerHeartbeat', 'Ai'],
  ['AiAgentApproval', 'Ai'],
  ['AiMcpRemoteConnection', 'Ai'],
  ['AiAgentRunSessionKind', 'Ai'],
]);

export function migrationScriptStem(scriptName) {
  const match = /^(\d+)_(.+)\.sql$/.exec(scriptName);
  if (!match) {
    throw new Error('Unexpected migration script name: ' + scriptName);
  }
  return match[2];
}

/**
 * @returns {'Shared'|string} 模块名；Shared 表示跨预设基础设施。
 */
export function inferMigrationModuleOwner(scriptName) {
  const stem = migrationScriptStem(scriptName);
  if (SHARED_STEMS.has(stem)) {
    return 'Shared';
  }
  const override = STEM_MODULE_OVERRIDES.get(stem);
  if (override) {
    return override;
  }
  for (const module of OFFICIAL_MODULE_NAMES) {
    if (stem === module || stem.startsWith(module)) {
      return module;
    }
  }
  if (stem.startsWith('Identity')) return 'Identity';
  if (stem.startsWith('Organization')) return 'Organization';
  if (stem.startsWith('Tenancy')) return 'Tenancy';
  if (stem.startsWith('Settings')) return 'Settings';
  if (stem.startsWith('Workflow')) return 'Workflow';
  if (stem.startsWith('Notifications')) return 'Notifications';
  if (stem.startsWith('ImportExport')) return 'ImportExport';
  if (stem.startsWith('Reporting')) return 'Reporting';
  if (stem.startsWith('Printing')) return 'Printing';
  if (stem.startsWith('Observability')) return 'ObservabilityAdmin';
  if (stem.startsWith('Mqtt')) return 'Mqtt';
  if (stem.startsWith('Cryptography')) return 'Cryptography';
  if (stem.startsWith('Webhooks')) return 'Webhooks';
  if (stem.startsWith('Jobs')) return 'Jobs';
  if (stem.startsWith('Files')) return 'Files';
  if (stem.startsWith('Auditing')) return 'Auditing';
  if (stem.startsWith('Calendar')) return 'Calendar';
  if (stem.startsWith('Regions')) return 'Regions';
  if (stem.startsWith('Platform')) return 'Platform';
  if (stem.startsWith('Host')) return 'Identity';
  throw new Error('Unable to infer migration module owner for script: ' + scriptName);
}

export function buildPresetMigrationInventory(fullInventory, preset, modules) {
  const moduleSet = new Set(modules);
  const selected = fullInventory.scripts.filter(({ name }) => {
    const owner = inferMigrationModuleOwner(name);
    if (owner === 'Shared') return true;
    if (owner === 'Ai') return false;
    return moduleSet.has(owner);
  });
  if (selected.length === 0) {
    throw new Error('Preset migration inventory is empty for preset: ' + preset);
  }
  return {
    selectionStatus: `preset-${preset}`,
    preset,
    scripts: selected,
  };
}
