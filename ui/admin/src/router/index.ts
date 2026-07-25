import {
  createRouter,
  createWebHashHistory,
  type RouterHistory
} from 'vue-router';
import type { Pinia } from 'pinia';
import { useSessionStore } from '../auth/session';
import { flattenNavigation } from '../navigation/catalog';
import OverviewView from '../views/OverviewView.vue';
import StatusView from '../views/StatusView.vue';
import TenantContextView from '../views/TenantContextView.vue';
import SuperAdministratorsView from '../views/SuperAdministratorsView.vue';
import TenantsView from '../views/TenantsView.vue';
import TenantPackagesView from '../views/TenantPackagesView.vue';
import UsersView from '../views/UsersView.vue';
import OnlineSessionsView from '../views/OnlineSessionsView.vue';
import RolesView from '../views/RolesView.vue';
import MenusView from '../views/MenusView.vue';
import OrgUnitsView from '../views/OrgUnitsView.vue';
import OrgUserUnitsView from '../views/OrgUserUnitsView.vue';
import OrgPositionsView from '../views/OrgPositionsView.vue';
import OrgUserPositionsView from '../views/OrgUserPositionsView.vue';
import DictTypesView from '../views/DictTypesView.vue';
import ConfigEntriesView from '../views/ConfigEntriesView.vue';
import EnumCatalogsView from '../views/EnumCatalogsView.vue';
import HostFilesView from '../views/HostFilesView.vue';
import HostAnnouncementsView from '../views/HostAnnouncementsView.vue';
import InboxMessagesView from '../views/InboxMessagesView.vue';
import HostJobsView from '../views/HostJobsView.vue';
import AccessLogsView from '../views/AccessLogsView.vue';
import OperationLogsView from '../views/OperationLogsView.vue';
import ExceptionLogsView from '../views/ExceptionLogsView.vue';

const statusPaths = new Set(['/403', '/404', '/500']);

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
        component: TenantContextView
      },
      {
        name: 'tenant-management',
        path: '/tenants',
        component: TenantsView
      },
      {
        name: 'tenant-packages',
        path: '/tenant-packages',
        component: TenantPackagesView
      },
      {
        name: 'users',
        path: '/identity/users',
        component: UsersView
      },
      {
        name: 'online-sessions',
        path: '/identity/online-sessions',
        component: OnlineSessionsView
      },
      {
        name: 'roles',
        path: '/identity/roles',
        component: RolesView
      },
      {
        name: 'menus',
        path: '/identity/menus',
        component: MenusView
      },
      {
        name: 'org-units',
        path: '/organization/units',
        component: OrgUnitsView
      },
      {
        name: 'org-user-units',
        path: '/organization/user-units',
        component: OrgUserUnitsView
      },
      {
        name: 'org-positions',
        path: '/organization/positions',
        component: OrgPositionsView
      },
      {
        name: 'org-user-positions',
        path: '/organization/user-positions',
        component: OrgUserPositionsView
      },
      {
        name: 'super-administrators',
        path: '/identity/super-administrators',
        component: SuperAdministratorsView
      },
      {
        name: 'dict-types',
        path: '/settings/dict-types',
        component: DictTypesView
      },
      {
        name: 'config-entries',
        path: '/settings/config-entries',
        component: ConfigEntriesView
      },
      {
        name: 'enum-catalogs',
        path: '/settings/enum-catalogs',
        component: EnumCatalogsView
      },
      {
        name: 'host-files',
        path: '/files/host-files',
        component: HostFilesView
      },
      {
        name: 'host-announcements',
        path: '/notifications/host-announcements',
        component: HostAnnouncementsView
      },
      {
        name: 'inbox-messages',
        path: '/notifications/inbox-messages',
        component: InboxMessagesView
      },
      {
        name: 'host-jobs',
        path: '/jobs/host-definitions',
        component: HostJobsView
      },
      {
        name: 'access-logs',
        path: '/auditing/access-logs',
        component: AccessLogsView
      },
      {
        name: 'operation-logs',
        path: '/auditing/operation-logs',
        component: OperationLogsView
      },
      {
        name: 'exception-logs',
        path: '/auditing/exception-logs',
        component: ExceptionLogsView
      },
      { path: '/403', component: StatusView, props: { code: '403' } },
      { path: '/404', component: StatusView, props: { code: '404' } },
      { path: '/500', component: StatusView, props: { code: '500' } },
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
