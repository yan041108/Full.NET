import type { NavigationNode } from './authorization.js';

/** 管理端本地导航目录项；componentKey 必须与服务端导航树一致。 */
export interface AdminNavigationCatalogEntry {
  componentKey: string;
  routeName: string;
  path: string;
}

/** 当前双管理端共同发布的导航白名单。 */
export const ADMIN_NAVIGATION_CATALOG: readonly AdminNavigationCatalogEntry[] = [
  {
    componentKey: 'overview',
    routeName: 'overview',
    path: '/'
  },
  {
    componentKey: 'tenant-context',
    routeName: 'tenant-context',
    path: '/tenant-context'
  },
  {
    componentKey: 'tenants',
    routeName: 'tenant-management',
    path: '/tenants'
  },
  {
    componentKey: 'tenant-packages',
    routeName: 'tenant-packages',
    path: '/tenant-packages'
  },
  {
    componentKey: 'users',
    routeName: 'users',
    path: '/identity/users'
  },
  {
    componentKey: 'online-sessions',
    routeName: 'online-sessions',
    path: '/identity/online-sessions'
  },
  {
    componentKey: 'api-keys',
    routeName: 'api-keys',
    path: '/identity/api-keys'
  },
  {
    componentKey: 'open-access-clients',
    routeName: 'open-access-clients',
    path: '/identity/open-access-clients'
  },
  {
    componentKey: 'oidc-clients',
    routeName: 'oidc-clients',
    path: '/identity/oidc-clients'
  },
  {
    componentKey: 'oidc-authorizations',
    routeName: 'oidc-authorizations',
    path: '/identity/oidc-authorizations'
  },
  {
    componentKey: 'oidc-signing-keys',
    routeName: 'oidc-signing-keys',
    path: '/identity/oidc-signing-keys'
  },
  {
    componentKey: 'registration-ways',
    routeName: 'registration-ways',
    path: '/identity/registration-ways'
  },
  {
    componentKey: 'ldap-connections',
    routeName: 'ldap-connections',
    path: '/identity/ldap-connections'
  },
  {
    componentKey: 'oauth-providers',
    routeName: 'oauth-providers',
    path: '/identity/oauth-providers'
  },
  {
    componentKey: 'modules',
    routeName: 'modules',
    path: '/identity/modules'
  },
  {
    componentKey: 'module-selection',
    routeName: 'module-selection',
    path: '/identity/module-selection'
  },
  {
    componentKey: 'roles',
    routeName: 'roles',
    path: '/identity/roles'
  },
  {
    componentKey: 'menus',
    routeName: 'menus',
    path: '/identity/menus'
  },
  {
    componentKey: 'org-units',
    routeName: 'org-units',
    path: '/organization/units'
  },
  {
    componentKey: 'org-user-units',
    routeName: 'org-user-units',
    path: '/organization/user-units'
  },
  {
    componentKey: 'org-positions',
    routeName: 'org-positions',
    path: '/organization/positions'
  },
  {
    componentKey: 'org-position-levels',
    routeName: 'org-position-levels',
    path: '/organization/position-levels'
  },
  {
    componentKey: 'org-user-positions',
    routeName: 'org-user-positions',
    path: '/organization/user-positions'
  },
  {
    componentKey: 'super-administrators',
    routeName: 'super-administrators',
    path: '/identity/super-administrators'
  },
  {
    componentKey: 'dict-types',
    routeName: 'dict-types',
    path: '/settings/dict-types'
  },
  {
    componentKey: 'tenant-dict-types',
    routeName: 'tenant-dict-types',
    path: '/settings/tenant-dict-types'
  },
  {
    componentKey: 'config-entries',
    routeName: 'config-entries',
    path: '/settings/config-entries'
  },
  {
    componentKey: 'diagnostic-policy',
    routeName: 'diagnostic-policy',
    path: '/settings/diagnostic-policy'
  },
  {
    componentKey: 'enum-catalogs',
    routeName: 'enum-catalogs',
    path: '/settings/enum-catalogs'
  },
  {
    componentKey: 'host-files',
    routeName: 'host-files',
    path: '/files/host-files'
  },
  {
    componentKey: 'import-export-tasks',
    routeName: 'import-export-tasks',
    path: '/import-export/tasks'
  },
  {
    componentKey: 'storage-providers',
    routeName: 'storage-providers',
    path: '/files/storage-providers'
  },
  {
    componentKey: 'host-document-items',
    routeName: 'host-document-items',
    path: '/document/host-items'
  },
  {
    componentKey: 'document-categories',
    routeName: 'document-categories',
    path: '/document/categories'
  },
  {
    componentKey: 'document-tags',
    routeName: 'document-tags',
    path: '/document/tags'
  },
  {
    componentKey: 'document-recycle-bin',
    routeName: 'document-recycle-bin',
    path: '/document/recycle-bin'
  },
  {
    componentKey: 'document-shares',
    routeName: 'document-shares',
    path: '/document/shares'
  },
  {
    componentKey: 'document-permissions',
    routeName: 'document-permissions',
    path: '/document/permissions'
  },
  {
    componentKey: 'document-statistics',
    routeName: 'document-statistics',
    path: '/document/statistics'
  },
  {
    componentKey: 'host-messaging-ops',
    routeName: 'host-messaging-ops',
    path: '/messaging/operations'
  },
  {
    componentKey: 'host-announcements',
    routeName: 'host-announcements',
    path: '/notifications/host-announcements'
  },
  {
    componentKey: 'inbox-messages',
    routeName: 'inbox-messages',
    path: '/notifications/inbox-messages'
  },
  {
    componentKey: 'notification-templates',
    routeName: 'notification-templates',
    path: '/notifications/templates'
  },
  {
    componentKey: 'notification-provider-profiles',
    routeName: 'notification-provider-profiles',
    path: '/notifications/provider-profiles'
  },
  {
    componentKey: 'notification-bindings',
    routeName: 'notification-bindings',
    path: '/notifications/bindings'
  },
  {
    componentKey: 'notification-deliveries',
    routeName: 'notification-deliveries',
    path: '/notifications/deliveries'
  },
  {
    componentKey: 'notification-preferences',
    routeName: 'notification-preferences',
    path: '/notifications/preferences'
  },
  {
    componentKey: 'host-jobs',
    routeName: 'host-jobs',
    path: '/jobs/host-definitions'
  },
  {
    componentKey: 'host-job-schedules',
    routeName: 'host-job-schedules',
    path: '/jobs/host-schedules'
  },
  {
    componentKey: 'host-job-executions',
    routeName: 'host-job-executions',
    path: '/jobs/host-executions'
  },
  {
    componentKey: 'host-job-health',
    routeName: 'host-job-health',
    path: '/jobs/host-health'
  },
  {
    componentKey: 'personal-schedules',
    routeName: 'personal-schedules',
    path: '/calendar/personal-schedules'
  },
  {
    componentKey: 'host-release-notes',
    routeName: 'host-release-notes',
    path: '/platform/host-release-notes'
  },
  {
    componentKey: 'my-release-notes',
    routeName: 'my-release-notes',
    path: '/platform/my-release-notes'
  },
  {
    componentKey: 'backup-executor',
    routeName: 'backup-executor',
    path: '/platform/backup-executor'
  },
  {
    componentKey: 'administrative-regions',
    routeName: 'administrative-regions',
    path: '/regions/administrative-regions'
  },
  {
    componentKey: 'code-generation-catalog',
    routeName: 'code-generation-catalog',
    path: '/code-generation/catalog'
  },
  {
    componentKey: 'code-generation-templates',
    routeName: 'code-generation-templates',
    path: '/code-generation/templates'
  },
  {
    componentKey: 'code-generation-previews',
    routeName: 'code-generation-previews',
    path: '/code-generation/previews'
  },
  {
    componentKey: 'serial-number-rules',
    routeName: 'serial-number-rules',
    path: '/serial-numbers/rules'
  },
  {
    componentKey: 'access-logs',
    routeName: 'access-logs',
    path: '/auditing/access-logs'
  },
  {
    componentKey: 'authentication-events',
    routeName: 'authentication-events',
    path: '/auditing/authentication-events'
  },
  {
    componentKey: 'observability-log-files',
    routeName: 'observability-log-files',
    path: '/observability/log-files'
  },
  {
    componentKey: 'observability-server-monitor',
    routeName: 'observability-server-monitor',
    path: '/observability/server-monitor'
  },
  {
    componentKey: 'observability-cache-policies',
    routeName: 'observability-cache-policies',
    path: '/observability/cache-policies'
  },
  {
    componentKey: 'observability-elasticsearch-health',
    routeName: 'observability-elasticsearch-health',
    path: '/observability/elasticsearch-health'
  },
  {
    componentKey: 'operation-logs',
    routeName: 'operation-logs',
    path: '/auditing/operation-logs'
  },
  {
    componentKey: 'exception-logs',
    routeName: 'exception-logs',
    path: '/auditing/exception-logs'
  },
  {
    componentKey: 'outbound-call-logs',
    routeName: 'outbound-call-logs',
    path: '/auditing/outbound-call-logs'
  },
  {
    componentKey: 'workflow-todos',
    routeName: 'workflow-todos',
    path: '/workflow/todos'
  },
  {
    componentKey: 'workflow-cc',
    routeName: 'workflow-cc',
    path: '/workflow/cc'
  },
  {
    componentKey: 'workflow-definitions',
    routeName: 'workflow-definitions',
    path: '/workflow/definitions'
  },
  {
    componentKey: 'workflow-forms',
    routeName: 'workflow-forms',
    path: '/workflow/forms'
  },
  {
    componentKey: 'workflow-instances',
    routeName: 'workflow-instances',
    path: '/workflow/instances'
  },
  {
    componentKey: 'workflow-recovery-tasks',
    routeName: 'workflow-recovery-tasks',
    path: '/workflow/recovery-tasks'
  },
  {
    componentKey: 'ai-model-configs',
    routeName: 'ai-model-configs',
    path: '/ai/model-configs'
  },
  {
    componentKey: 'ai-chat',
    routeName: 'ai-chat',
    path: '/ai/chat'
  },
  {
    componentKey: 'ai-agent-tools',
    routeName: 'ai-agent-tools',
    path: '/ai/agent-tools'
  },
  {
    componentKey: 'ai-mcp-remote-connections',
    routeName: 'ai-mcp-remote-connections',
    path: '/ai/mcp-remote-connections'
  },
  {
    componentKey: 'ai-agent-runs',
    routeName: 'ai-agent-runs',
    path: '/ai/agent-runs'
  },
  {
    componentKey: 'goview-projects',
    routeName: 'goview-projects',
    path: '/goview/projects'
  },
  {
    componentKey: 'k3cloud-connection-configs',
    routeName: 'k3cloud-connection-configs',
    path: '/k3cloud/connection-configs'
  },
  {
    componentKey: 'k3cloud-document-syncs',
    routeName: 'k3cloud-document-syncs',
    path: '/k3cloud/document-syncs'
  },
  {
    componentKey: 'ocr-provider-config',
    routeName: 'ocr-provider-config',
    path: '/ocr/provider-config'
  },
  {
    componentKey: 'ocr-id-card-tasks',
    routeName: 'ocr-id-card-tasks',
    path: '/ocr/id-card-tasks'
  },
  {
    componentKey: 'payments-merchant-configs',
    routeName: 'payments-merchant-configs',
    path: '/payments/merchant-configs'
  },
  {
    componentKey: 'payments-orders',
    routeName: 'payments-orders',
    path: '/payments/orders'
  },
  {
    componentKey: 'payments-refunds',
    routeName: 'payments-refunds',
    path: '/payments/refunds'
  },
  {
    componentKey: 'printing-preview',
    routeName: 'printing-preview',
    path: '/printing/preview'
  },
  {
    componentKey: 'reporting-data-sources',
    routeName: 'reporting-data-sources',
    path: '/reporting/data-sources'
  },
  {
    componentKey: 'reporting-definitions',
    routeName: 'reporting-definitions',
    path: '/reporting/definitions'
  },
  {
    componentKey: 'reporting-execute',
    routeName: 'reporting-execute',
    path: '/reporting/execute'
  },
  {
    componentKey: 'reporting-export-tasks',
    routeName: 'reporting-export-tasks',
    path: '/reporting/export-tasks'
  },
  {
    componentKey: 'tenant-branding',
    routeName: 'tenant-branding',
    path: '/settings/tenant-branding'
  },
  {
    componentKey: 'tenant-onboarding',
    routeName: 'tenant-onboarding',
    path: '/tenant-onboarding'
  },
  {
    componentKey: 'tenant-members',
    routeName: 'tenant-members',
    path: '/identity/tenant-members'
  },
  {
    componentKey: 'my-host-announcements',
    routeName: 'my-host-announcements',
    path: '/notifications/my-host-announcements'
  },
  {
    componentKey: 'notification-dingtalk-approval-sync',
    routeName: 'notification-dingtalk-approval-sync',
    path: '/notifications/dingtalk-approval-sync'
  },
  {
    componentKey: 'notification-wechat-miniprogram-bindings',
    routeName: 'notification-wechat-miniprogram-bindings',
    path: '/notifications/wechat-miniprogram-bindings'
  },
  {
    componentKey: 'cryptography-gm-keys',
    routeName: 'cryptography-gm-keys',
    path: '/cryptography/gm-keys'
  },
  {
    componentKey: 'mqtt-control-plane',
    routeName: 'mqtt-control-plane',
    path: '/mqtt/control-plane'
  },
  {
    componentKey: 'document-preview-tasks',
    routeName: 'document-preview-tasks',
    path: '/document/preview-tasks'
  },
  {
    componentKey: 'enterprise-requests',
    routeName: 'enterprise-requests',
    path: '/enterprise-requests'
  },
  {
    componentKey: 'data-approval-requests',
    routeName: 'data-approval-requests',
    path: '/data-approvals/requests'
  },
  {
    componentKey: 'data-approval-scenarios',
    routeName: 'data-approval-scenarios',
    path: '/data-approvals/scenarios'
  },
  {
    componentKey: 'layout',
    routeName: 'layout',
    path: '/'
  }
] as const;

export interface AdminNavigationCatalog {
  localNavigationFor(
    componentKey: string
  ): Readonly<AdminNavigationCatalogEntry> | undefined;
  isSupportedNavigationTree(
    navigation: readonly NavigationNode[]
  ): boolean;
  flattenNavigation(navigation: readonly NavigationNode[]): NavigationNode[];
}

/** 基于固定目录创建导航白名单校验器，供 Vue/Layui 共享同一组 route/path 规则。 */
export function createAdminNavigationCatalog(
  catalog: readonly AdminNavigationCatalogEntry[] = ADMIN_NAVIGATION_CATALOG
): AdminNavigationCatalog {
  const supportedComponents = new Map(
    catalog.map(entry => [entry.componentKey, entry])
  );

  function localNavigationFor(
    componentKey: string
  ): Readonly<AdminNavigationCatalogEntry> | undefined {
    return supportedComponents.get(componentKey);
  }

  function isModuleDirectoryNode(node: NavigationNode): boolean {
    return node.componentKey === 'layout'
      && node.routeName.startsWith('module-')
      && node.path.startsWith('/modules/');
  }

  function isDomainDirectoryNode(node: NavigationNode): boolean {
    return node.componentKey === 'layout'
      && node.routeName.startsWith('domain-')
      && node.path.startsWith('/domains/');
  }

  function isContainerDirectoryNode(node: NavigationNode): boolean {
    return isDomainDirectoryNode(node) || isModuleDirectoryNode(node);
  }

  function isSupportedNavigationTree(
    navigation: readonly NavigationNode[]
  ): boolean {
    return navigation.every(node => {
      if (isContainerDirectoryNode(node)) {
        return isSupportedNavigationTree(node.children);
      }

      const local = supportedComponents.get(node.componentKey);
      return local !== undefined
        && local.routeName === node.routeName
        && local.path === node.path
        && isSupportedNavigationTree(node.children);
    });
  }

  function flattenNavigation(
    navigation: readonly NavigationNode[]
  ): NavigationNode[] {
    const result: NavigationNode[] = [];
    for (const node of navigation) {
      result.push(node, ...flattenNavigation(node.children));
    }

    return result;
  }

  return {
    localNavigationFor,
    isSupportedNavigationTree,
    flattenNavigation
  };
}
