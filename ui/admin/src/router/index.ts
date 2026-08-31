import {
  createRouter,
  createWebHashHistory,
  type RouterHistory
} from 'vue-router';
import type { Pinia } from 'pinia';
import { useSessionStore } from '../auth/session';
import { flattenNavigation } from '../navigation/catalog';
import OverviewView from '../views/OverviewView.vue';

const statusPaths = new Set(['/403', '/404', '/500']);
const loadStatusView = () => import('../views/StatusView.vue');

export function createAppRouter(
  history: RouterHistory = createWebHashHistory(),
  pinia?: Pinia
) {
  const router = createRouter({
    history,
    routes: [
      { name: 'overview', path: '/', component: OverviewView },
      {
        name: 'tenant-context',
        path: '/tenant-context',
        component: () => import('../views/TenantContextView.vue')
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
        name: 'modules',
        path: '/identity/modules',
        component: () => import('../views/ModuleCatalogView.vue')
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
        name: 'host-announcements',
        path: '/notifications/host-announcements',
        component: () => import('../views/HostAnnouncementsView.vue')
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
        name: 'observability-log-files',
        path: '/observability/log-files',
        component: () => import('../views/ObservabilityLogFilesView.vue')
      },
      {
        name: 'access-logs',
        path: '/auditing/access-logs',
        component: () => import('../views/AccessLogsView.vue')
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
      { path: '/403', component: loadStatusView, props: { code: '403' } },
      { path: '/404', component: loadStatusView, props: { code: '404' } },
      { path: '/500', component: loadStatusView, props: { code: '500' } },
      { path: '/:pathMatch(.*)*', redirect: '/404' }
    ]
  });

  router.beforeEach(to => {
    const session = useSessionStore(pinia);
    if (!session.isAuthenticated || statusPaths.has(to.path)) {
      return true;
    }

    return flattenNavigation(session.navigation).some(node => node.path === to.path)
      ? true
      : '/403';
  });

  return router;
}
