import {
  createAdminNavigationCatalog,
  type NavigationNode
} from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';

export interface LocalNavigationDefinition {
  routeName: string;
  path: string;
  titleKey: MessageKey;
  captionKey: MessageKey;
}

const navigationCatalog = createAdminNavigationCatalog();

const messageKeys = new Map<string, Pick<LocalNavigationDefinition, 'titleKey' | 'captionKey'>>([
  ['overview', {
    titleKey: 'navigation.overview.title',
    captionKey: 'navigation.overview.caption'
  }],
  ['tenant-context', {
    titleKey: 'navigation.tenantContext.title',
    captionKey: 'navigation.tenantContext.caption'
  }],
  ['tenants', {
    titleKey: 'navigation.tenants.title',
    captionKey: 'navigation.tenants.caption'
  }],
  ['tenant-packages', {
    titleKey: 'navigation.tenantPackages.title',
    captionKey: 'navigation.tenantPackages.caption'
  }],
  ['users', {
    titleKey: 'navigation.users.title',
    captionKey: 'navigation.users.caption'
  }],
  ['online-sessions', {
    titleKey: 'navigation.onlineSessions.title',
    captionKey: 'navigation.onlineSessions.caption'
  }],
  ['api-keys', {
    titleKey: 'navigation.apiKeys.title',
    captionKey: 'navigation.apiKeys.caption'
  }],
  ['modules', {
    titleKey: 'navigation.modules.title',
    captionKey: 'navigation.modules.caption'
  }],
  ['roles', {
    titleKey: 'navigation.roles.title',
    captionKey: 'navigation.roles.caption'
  }],
  ['menus', {
    titleKey: 'navigation.menus.title',
    captionKey: 'navigation.menus.caption'
  }],
  ['org-units', {
    titleKey: 'navigation.orgUnits.title',
    captionKey: 'navigation.orgUnits.caption'
  }],
  ['org-user-units', {
    titleKey: 'navigation.orgUserUnits.title',
    captionKey: 'navigation.orgUserUnits.caption'
  }],
  ['org-positions', {
    titleKey: 'navigation.orgPositions.title',
    captionKey: 'navigation.orgPositions.caption'
  }],
  ['org-position-levels', {
    titleKey: 'navigation.orgPositionLevels.title',
    captionKey: 'navigation.orgPositionLevels.caption'
  }],
  ['org-user-positions', {
    titleKey: 'navigation.orgUserPositions.title',
    captionKey: 'navigation.orgUserPositions.caption'
  }],
  ['super-administrators', {
    titleKey: 'navigation.superAdministrators.title',
    captionKey: 'navigation.superAdministrators.caption'
  }],
  ['dict-types', {
    titleKey: 'navigation.dictTypes.title',
    captionKey: 'navigation.dictTypes.caption'
  }],
  ['tenant-dict-types', {
    titleKey: 'navigation.tenantDictTypes.title',
    captionKey: 'navigation.tenantDictTypes.caption'
  }],
  ['config-entries', {
    titleKey: 'navigation.configEntries.title',
    captionKey: 'navigation.configEntries.caption'
  }],
  ['diagnostic-policy', {
    titleKey: 'navigation.diagnosticPolicy.title',
    captionKey: 'navigation.diagnosticPolicy.caption'
  }],
  ['enum-catalogs', {
    titleKey: 'navigation.enumCatalogs.title',
    captionKey: 'navigation.enumCatalogs.caption'
  }],
  ['host-files', {
    titleKey: 'navigation.hostFiles.title',
    captionKey: 'navigation.hostFiles.caption'
  }],
  ['host-document-items', {
    titleKey: 'navigation.hostDocumentItems.title',
    captionKey: 'navigation.hostDocumentItems.caption'
  }],
  ['document-categories', {
    titleKey: 'navigation.documentCategories.title',
    captionKey: 'navigation.documentCategories.caption'
  }],
  ['document-tags', {
    titleKey: 'navigation.documentTags.title',
    captionKey: 'navigation.documentTags.caption'
  }],
  ['document-recycle-bin', {
    titleKey: 'navigation.documentRecycleBin.title',
    captionKey: 'navigation.documentRecycleBin.caption'
  }],
  ['document-shares', {
    titleKey: 'navigation.documentShares.title',
    captionKey: 'navigation.documentShares.caption'
  }],
  ['document-permissions', {
    titleKey: 'navigation.documentPermissions.title',
    captionKey: 'navigation.documentPermissions.caption'
  }],
  ['document-statistics', {
    titleKey: 'navigation.documentStatistics.title',
    captionKey: 'navigation.documentStatistics.caption'
  }],
  ['host-messaging-ops', {
    titleKey: 'navigation.hostMessagingOps.title',
    captionKey: 'navigation.hostMessagingOps.caption'
  }],
  ['host-announcements', {
    titleKey: 'navigation.hostAnnouncements.title',
    captionKey: 'navigation.hostAnnouncements.caption'
  }],
  ['inbox-messages', {
    titleKey: 'navigation.inboxMessages.title',
    captionKey: 'navigation.inboxMessages.caption'
  }],
  ['host-jobs', {
    titleKey: 'navigation.hostJobs.title',
    captionKey: 'navigation.hostJobs.caption'
  }],
  ['host-job-schedules', {
    titleKey: 'navigation.hostJobSchedules.title',
    captionKey: 'navigation.hostJobSchedules.caption'
  }],
  ['code-generation-templates', {
    titleKey: 'navigation.codeGenerationTemplates.title',
    captionKey: 'navigation.codeGenerationTemplates.caption'
  }],
  ['code-generation-previews', {
    titleKey: 'navigation.codeGenerationPreviews.title',
    captionKey: 'navigation.codeGenerationPreviews.caption'
  }],
  ['serial-number-rules', {
    titleKey: 'navigation.serialNumberRules.title',
    captionKey: 'navigation.serialNumberRules.caption'
  }],
  ['access-logs', {
    titleKey: 'navigation.accessLogs.title',
    captionKey: 'navigation.accessLogs.caption'
  }],
  ['operation-logs', {
    titleKey: 'navigation.operationLogs.title',
    captionKey: 'navigation.operationLogs.caption'
  }],
  ['exception-logs', {
    titleKey: 'navigation.exceptionLogs.title',
    captionKey: 'navigation.exceptionLogs.caption'
  }],
  ['outbound-call-logs', {
    titleKey: 'navigation.outboundCallLogs.title',
    captionKey: 'navigation.outboundCallLogs.caption'
  }]
]);

/** 返回组件键对应的本地可信导航定义，未知键始终拒绝。 */
export function localNavigationFor(
  componentKey: string
): Readonly<LocalNavigationDefinition> | undefined {
  const entry = navigationCatalog.localNavigationFor(componentKey);
  const keys = messageKeys.get(componentKey);
  if (entry === undefined || keys === undefined) {
    return undefined;
  }

  return {
    routeName: entry.routeName,
    path: entry.path,
    ...keys
  };
}

/** 判断服务端导航中的每个组件键是否已由当前 Vue 版本显式发布。 */
export function isSupportedNavigationTree(
  navigation: readonly NavigationNode[]
): boolean {
  return navigationCatalog.isSupportedNavigationTree(navigation);
}

/** 按服务端树顺序生成只读平铺视图，供侧栏和路由权限检查复用。 */
export function flattenNavigation(
  navigation: readonly NavigationNode[]
): NavigationNode[] {
  return navigationCatalog.flattenNavigation(navigation);
}
