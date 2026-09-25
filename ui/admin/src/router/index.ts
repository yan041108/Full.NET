import {
  createRouter,
  createWebHashHistory,
  type RouteLocationNormalized,
  type RouterHistory
} from 'vue-router';
import type { Pinia } from 'pinia';
import { useSessionStore } from '../auth/session';
import { flattenNavigation } from '../navigation/catalog';
import OverviewView from '../views/OverviewView.vue';

/** 状态页始终允许直接进入，避免未认证或无导航目录时被守卫二次拦截。 */
const statusPaths = new Set(['/403', '/404', '/500']);

/** 自助账户页不依赖导航目录下发，已认证用户可直接访问。 */
export const selfServicePaths = new Set([
  '/account/security',
  '/account/profile',
  '/oauth/callback',
  '/identity/oidc/callback'
]);

/** 匿名用户可访问的公开认证页。 */
const publicAuthPaths = new Set([
  '/login',
  '/register',
  '/recover-password'
]);

/** 文档外部分享页：不在导航目录中，但需独立直达（匿名可访问、已登录不重定向）。 */
export function isDocumentPublicShareRoute(
  route: Pick<RouteLocationNormalized, 'name' | 'path'>
): boolean {
  if (route.name === 'document-public-share') {
    return true;
  }
  return route.path.startsWith('/document/share/');
}

/** Hash 路由在 router 就绪前即可判定分享页，避免首帧误显示会话恢复屏。 */
export function isDocumentPublicShareHash(): boolean {
  const hash = window.location.hash.replace(/^#/, '');
  return hash.startsWith('/document/share/');
}

/** 延迟加载状态页，避免普通业务路由首次渲染时额外拉取错误页代码。 */
const loadStatusView = () => import('../views/StatusView.vue');

/** 创建管理端路由，并用当前会话导航目录对受保护页面做失败关闭校验。 */
export function createAppRouter(
  history: RouterHistory = createWebHashHistory(),
  pinia?: Pinia
) {
  const router = createRouter({
    history,
    routes: [
      { name: 'overview', path: '/', component: OverviewView },
      {
        name: 'login',
        path: '/login',
        component: () => import('../views/LoginView.vue')
      },
      {
        name: 'register',
        path: '/register',
        component: () => import('../views/RegisterView.vue')
      },
      {
        name: 'recover-password',
        path: '/recover-password',
        component: () => import('../views/RecoverPasswordView.vue')
      },
      {
        name: 'tenant-context',
        path: '/tenant-context',
        component: () => import('../views/TenantContextView.vue')
      },
      {
        name: 'account-security',
        path: '/account/security',
        component: () => import('../views/SecuritySettingsView.vue')
      },
      {
        name: 'account-profile',
        path: '/account/profile',
        component: () => import('../views/ProfileSettingsView.vue')
      },
      {
        name: 'tenant-management',
        path: '/tenants',
        component: () => import('../views/TenantsView.vue')
      },
      {
        name: 'tenant-packages',
        path: '/tenant-packages',
        component: () => import('../views/TenantPackagesView.vue')
      },
      {
        name: 'tenant-branding',
        path: '/settings/tenant-branding',
        component: () => import('../views/TenantBrandingView.vue')
      },
      {
        name: 'tenant-onboarding',
        path: '/tenant-onboarding',
        component: () => import('../views/TenantOnboardingView.vue')
      },
      {
        name: 'tenant-members',
        path: '/identity/tenant-members',
        component: () => import('../views/TenantMembersView.vue')
      },
      {
        name: 'enterprise-requests',
        path: '/enterprise-requests',
        component: () => import('../views/EnterpriseRequestsView.vue')
      },
      {
        name: 'tenant-subscription',
        path: '/tenancy/tenant-subscriptions',
        component: () => import('../views/TenantSubscriptionView.vue')
      },
      {
        name: 'users',
        path: '/identity/users',
        component: () => import('../views/UsersView.vue')
      },
      {
        name: 'online-sessions',
        path: '/identity/online-sessions',
        component: () => import('../views/OnlineSessionsView.vue')
      },
      {
        name: 'api-keys',
        path: '/identity/api-keys',
        component: () => import('../views/ApiKeysView.vue')
      },
      {
        name: 'open-access-clients',
        path: '/identity/open-access-clients',
        component: () => import('../views/OpenAccessClientsView.vue')
      },
      {
        name: 'oidc-clients',
        path: '/identity/oidc-clients',
        component: () => import('../views/OidcClientsView.vue')
      },
      {
        name: 'oidc-authorizations',
        path: '/identity/oidc-authorizations',
        component: () => import('../views/OidcAuthorizationsView.vue')
      },
      {
        name: 'oidc-signing-keys',
        path: '/identity/oidc-signing-keys',
        component: () => import('../views/OidcSigningKeysView.vue')
      },
      {
        name: 'registration-ways',
        path: '/identity/registration-ways',
        component: () => import('../views/RegistrationWaysView.vue')
      },
      {
        name: 'ldap-connections',
        path: '/identity/ldap-connections',
        component: () => import('../views/LdapConnectionsView.vue')
      },
      {
        name: 'oauth-providers',
        path: '/identity/oauth-providers',
        component: () => import('../views/OAuthProvidersView.vue')
      },
      {
        name: 'oauth-callback',
        path: '/oauth/callback',
        component: () => import('../views/OAuthCallbackView.vue')
      },
      {
        name: 'oidc-callback',
        path: '/identity/oidc/callback',
        component: () => import('../views/OidcCallbackView.vue')
      },
      {
        name: 'modules',
        path: '/identity/modules',
        component: () => import('../views/ModuleCatalogView.vue')
      },
      {
        name: 'module-selection',
        path: '/identity/module-selection',
        component: () => import('../views/ModuleSelectionPreviewView.vue')
      },
      {
        name: 'roles',
        path: '/identity/roles',
        component: () => import('../views/RolesView.vue')
      },
      {
        name: 'menus',
        path: '/identity/menus',
        component: () => import('../views/MenusView.vue')
      },
      {
        name: 'org-units',
        path: '/organization/units',
        component: () => import('../views/OrgUnitsView.vue')
      },
      {
        name: 'org-user-units',
        path: '/organization/user-units',
        component: () => import('../views/OrgUserUnitsView.vue')
      },
      {
        name: 'org-positions',
        path: '/organization/positions',
        component: () => import('../views/OrgPositionsView.vue')
      },
      {
        name: 'org-position-levels',
        path: '/organization/position-levels',
        component: () => import('../views/OrgPositionLevelsView.vue')
      },
      {
        name: 'org-user-positions',
        path: '/organization/user-positions',
        component: () => import('../views/OrgUserPositionsView.vue')
      },
      {
        name: 'super-administrators',
        path: '/identity/super-administrators',
        component: () => import('../views/SuperAdministratorsView.vue')
      },
      {
        name: 'dict-types',
        path: '/settings/dict-types',
        component: () => import('../views/DictTypesView.vue')
      },
      {
        name: 'tenant-dict-types',
        path: '/settings/tenant-dict-types',
        component: () => import('../views/TenantDictTypesView.vue')
      },
      {
        name: 'diagnostic-policy',
        path: '/settings/diagnostic-policy',
        component: () => import('../views/DiagnosticPolicyView.vue')
      },
      {
        name: 'config-entries',
        path: '/settings/config-entries',
        component: () => import('../views/ConfigEntriesView.vue')
      },
      {
        name: 'enum-catalogs',
        path: '/settings/enum-catalogs',
        component: () => import('../views/EnumCatalogsView.vue')
      },
      {
        name: 'host-files',
        path: '/files/host-files',
        component: () => import('../views/HostFilesView.vue')
      },
      {
        name: 'storage-providers',
        path: '/files/storage-providers',
        component: () => import('../views/StorageProvidersView.vue')
      },
      {
        name: 'host-document-items',
        path: '/document/host-items',
        component: () => import('../views/HostDocumentItemsView.vue')
      },
      {
        name: 'document-categories',
        path: '/document/categories',
        component: () => import('../views/DocumentCategoriesView.vue')
      },
      {
        name: 'document-tags',
        path: '/document/tags',
        component: () => import('../views/DocumentTagsView.vue')
      },
      {
        name: 'document-recycle-bin',
        path: '/document/recycle-bin',
        component: () => import('../views/DocumentRecycleBinView.vue')
      },
      {
        name: 'document-shares',
        path: '/document/shares',
        component: () => import('../views/DocumentSharesView.vue')
      },
      {
        name: 'document-public-share',
        path: '/document/share/:shareCode',
        component: () => import('../views/DocumentPublicShareView.vue')
      },
      {
        name: 'document-permissions',
        path: '/document/permissions',
        component: () => import('../views/DocumentPermissionsView.vue')
      },
      {
        name: 'document-statistics',
        path: '/document/statistics',
        component: () => import('../views/DocumentStatisticsView.vue')
      },
      {
        name: 'document-preview-tasks',
        path: '/document/preview-tasks',
        component: () => import('../views/DocumentPreviewTasksView.vue')
      },
      {
        name: 'import-export-tasks',
        path: '/import-export/tasks',
        component: () => import('../views/ImportExportTasksView.vue')
      },
      {
        name: 'reporting-data-sources',
        path: '/reporting/data-sources',
        component: () => import('../views/ReportingDataSourcesView.vue')
      },
      {
        name: 'reporting-definitions',
        path: '/reporting/definitions',
        component: () => import('../views/ReportingDefinitionsView.vue')
      },
      {
        name: 'reporting-execute',
        path: '/reporting/execute',
        component: () => import('../views/ReportingExecuteView.vue')
      },
      {
        name: 'reporting-export-tasks',
        path: '/reporting/export-tasks',
        component: () => import('../views/ReportingExportTasksView.vue')
      },
      {
        name: 'printing-preview',
        path: '/printing/preview',
        component: () => import('../views/PrintingPreviewView.vue')
      },
      {
        name: 'ai-model-configs',
        path: '/ai/model-configs',
        component: () => import('../views/AiModelConfigsView.vue')
      },
      {
        name: 'ai-chat',
        path: '/ai/chat',
        component: () => import('../views/AiChatView.vue')
      },
      {
        name: 'ai-agent-tools',
        path: '/ai/agent-tools',
        component: () => import('../views/AiAgentToolsView.vue')
      },
      {
        name: 'ai-mcp-remote-connections',
        path: '/ai/mcp-remote-connections',
        component: () => import('../views/AiMcpRemoteConnectionsView.vue')
      },
      {
        name: 'ai-agent-runs',
        path: '/ai/agent-runs',
        component: () => import('../views/AiAgentRunsView.vue')
      },
      {
        name: 'payment-merchant-configs',
        path: '/payments/merchant-configs',
        component: () => import('../views/PaymentMerchantConfigsView.vue')
      },
      {
        name: 'payment-orders',
        path: '/payments/orders',
        component: () => import('../views/PaymentOrdersView.vue')
      },
      {
        name: 'payment-refunds',
        path: '/payments/refunds',
        component: () => import('../views/PaymentRefundsView.vue')
      },
      {
        name: 'goview-projects',
        path: '/goview/projects',
        component: () => import('../views/GoViewProjectsView.vue')
      },
      {
        name: 'goview-editor',
        path: '/goview/projects/:projectId/edit',
        component: () => import('../views/GoViewEditorView.vue')
      },
      {
        name: 'goview-preview',
        path: '/goview/projects/:projectId/preview',
        component: () => import('../views/GoViewPreviewView.vue')
      },
      {
        name: 'k3cloud-connection-configs',
        path: '/k3cloud/connection-configs',
        component: () => import('../views/K3CloudConnectionConfigsView.vue')
      },
      {
        name: 'k3cloud-document-syncs',
        path: '/k3cloud/document-syncs',
        component: () => import('../views/K3CloudDocumentSyncsView.vue')
      },
      {
        name: 'ocr-provider-config',
        path: '/ocr/provider-config',
        component: () => import('../views/OcrProviderConfigView.vue')
      },
      {
        name: 'ocr-id-card-tasks',
        path: '/ocr/id-card-tasks',
        component: () => import('../views/OcrIdCardTasksView.vue')
      },
      {
        name: 'host-announcements',
        path: '/notifications/host-announcements',
        component: () => import('../views/HostAnnouncementsView.vue')
      },
      {
        name: 'my-host-announcements',
        path: '/notifications/my-host-announcements',
        component: () => import('../views/MyHostAnnouncementsView.vue')
      },
      {
        name: 'inbox-messages',
        path: '/notifications/inbox-messages',
        component: () => import('../views/InboxMessagesView.vue')
      },
      {
        name: 'notification-templates',
        path: '/notifications/templates',
        component: () => import('../views/NotificationTemplatesView.vue')
      },
      {
        name: 'notification-provider-profiles',
        path: '/notifications/provider-profiles',
        component: () => import('../views/NotificationProviderProfilesView.vue')
      },
      {
        name: 'notification-bindings',
        path: '/notifications/bindings',
        component: () => import('../views/NotificationBindingsView.vue')
      },
      {
        name: 'notification-deliveries',
        path: '/notifications/deliveries',
        component: () => import('../views/NotificationDeliveriesView.vue')
      },
      {
        name: 'notification-preferences',
        path: '/notifications/preferences',
        component: () => import('../views/NotificationPreferencesView.vue')
      },
      {
        name: 'notification-dingtalk-approval-sync',
        path: '/notifications/dingtalk-approval-sync',
        component: () => import('../views/DingTalkApprovalSyncView.vue')
      },
      {
        name: 'notification-wechat-miniprogram-bindings',
        path: '/notifications/wechat-miniprogram-bindings',
        component: () => import('../views/WeChatMiniProgramBindingsView.vue')
      },
      {
        name: 'host-jobs',
        path: '/jobs/host-definitions',
        component: () => import('../views/HostJobsView.vue')
      },
      {
        name: 'host-job-schedules',
        path: '/jobs/host-schedules',
        component: () => import('../views/HostJobSchedulesView.vue')
      },
      {
        name: 'host-job-executions',
        path: '/jobs/host-executions',
        component: () => import('../views/HostJobExecutionsView.vue')
      },
      {
        name: 'host-job-health',
        path: '/jobs/host-health',
        component: () => import('../views/HostJobHealthView.vue')
      },
      {
        name: 'personal-schedules',
        path: '/calendar/personal-schedules',
        component: () => import('../views/PersonalSchedulesView.vue')
      },
      {
        name: 'host-release-notes',
        path: '/platform/host-release-notes',
        component: () => import('../views/HostReleaseNotesView.vue')
      },
      {
        name: 'my-release-notes',
        path: '/platform/my-release-notes',
        component: () => import('../views/MyReleaseNotesView.vue')
      },
      {
        name: 'backup-executor',
        path: '/platform/backup-executor',
        component: () => import('../views/BackupExecutorView.vue')
      },
      {
        name: 'administrative-regions',
        path: '/regions/administrative-regions',
        component: () => import('../views/AdministrativeRegionsView.vue')
      },
      {
        name: 'code-generation-catalog',
        path: '/code-generation/catalog',
        component: () => import('../views/CodeGenerationCatalogView.vue')
      },
      {
        name: 'code-generation-templates',
        path: '/code-generation/templates',
        component: () => import('../views/CodeGenerationTemplatesView.vue')
      },
      {
        name: 'code-generation-previews',
        path: '/code-generation/previews',
        component: () => import('../views/CodeGenerationPreviewsView.vue')
      },
      {
        name: 'serial-number-rules',
        path: '/serial-numbers/rules',
        component: () => import('../views/SerialNumberRulesView.vue')
      },
      {
        name: 'data-approval-requests',
        path: '/data-approvals/requests',
        component: () => import('../views/DataApprovalRequestsView.vue')
      },
      {
        name: 'data-approval-scenarios',
        path: '/data-approvals/scenarios',
        component: () => import('../views/DataApprovalScenariosView.vue')
      },
      {
        name: 'observability-log-files',
        path: '/observability/log-files',
        component: () => import('../views/ObservabilityLogFilesView.vue')
      },
      {
        name: 'observability-server-monitor',
        path: '/observability/server-monitor',
        component: () => import('../views/ObservabilityServerMonitorView.vue')
      },
      {
        name: 'observability-cache-policies',
        path: '/observability/cache-policies',
        component: () => import('../views/ObservabilityCachePoliciesView.vue')
      },
      {
        name: 'observability-elasticsearch-health',
        path: '/observability/elasticsearch-health',
        component: () => import('../views/ObservabilityElasticsearchHealthView.vue')
      },
      {
        name: 'mqtt-control-plane',
        path: '/mqtt/control-plane',
        component: () => import('../views/MqttControlPlaneView.vue')
      },
      {
        name: 'host-messaging-ops',
        path: '/messaging/operations',
        component: () => import('../views/HostMessagingOpsView.vue')
      },
      {
        name: 'cryptography-gm-keys',
        path: '/cryptography/gm-keys',
        component: () => import('../views/CryptographyGmKeysView.vue')
      },
      {
        name: 'access-logs',
        path: '/auditing/access-logs',
        component: () => import('../views/AccessLogsView.vue')
      },
      {
        name: 'authentication-events',
        path: '/auditing/authentication-events',
        component: () => import('../views/AuthenticationEventsView.vue')
      },
      {
        name: 'operation-logs',
        path: '/auditing/operation-logs',
        component: () => import('../views/OperationLogsView.vue')
      },
      {
        name: 'exception-logs',
        path: '/auditing/exception-logs',
        component: () => import('../views/ExceptionLogsView.vue')
      },
      {
        name: 'outbound-call-logs',
        path: '/auditing/outbound-call-logs',
        component: () => import('../views/OutboundCallLogsView.vue')
      },
      {
        name: 'workflow-todos',
        path: '/workflow/todos',
        component: () => import('../views/WorkflowTodosView.vue')
      },
      {
        name: 'workflow-cc',
        path: '/workflow/cc',
        component: () => import('../views/WorkflowCcView.vue')
      },
      {
        name: 'workflow-definitions',
        path: '/workflow/definitions',
        component: () => import('../views/WorkflowDefinitionsView.vue')
      },
      {
        name: 'workflow-forms',
        path: '/workflow/forms',
        component: () => import('../views/WorkflowFormsView.vue')
      },
      {
        name: 'workflow-instances',
        path: '/workflow/instances',
        component: () => import('../views/WorkflowInstancesView.vue')
      },
      {
        name: 'workflow-recovery-tasks',
        path: '/workflow/recovery-tasks',
        component: () => import('../views/WorkflowRecoveryTasksView.vue')
      },
      { path: '/403', component: loadStatusView, props: { code: '403' } },
      { path: '/404', component: loadStatusView, props: { code: '404' } },
      { path: '/500', component: loadStatusView, props: { code: '500' } },
      { path: '/:pathMatch(.*)*', redirect: '/404' }
    ]
  });

  /** 已认证用户只能访问服务端已下发并被本地白名单认可的导航路径。 */
  router.beforeEach(to => {
    const session = useSessionStore(pinia);
    if (session.isAuthenticated
      && session.currentUser?.passwordChangeRequired
      && to.path !== '/account/security') {
      return '/account/security?forced=1';
    }

    if (!session.isAuthenticated
      || statusPaths.has(to.path)
      || selfServicePaths.has(to.path)
      || publicAuthPaths.has(to.path)
      || isDocumentPublicShareRoute(to)) {
      return true;
    }

    return flattenNavigation(session.navigation).some(node => node.path === to.path)
      ? true
      : '/403';
  });

  return router;
}
