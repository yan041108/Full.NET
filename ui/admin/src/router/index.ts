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
        name: 'host-jobs',
        path: '/jobs/host-definitions',
        component: () => import('../views/HostJobsView.vue')
      },
      {
        name: 'code-generation-previews',
        path: '/code-generation/previews',
        component: () => import('../views/CodeGenerationPreviewsView.vue')
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
