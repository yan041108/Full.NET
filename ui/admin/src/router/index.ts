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
import UsersView from '../views/UsersView.vue';
import RolesView from '../views/RolesView.vue';
import MenusView from '../views/MenusView.vue';
import OrgUnitsView from '../views/OrgUnitsView.vue';

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
        name: 'users',
        path: '/identity/users',
        component: UsersView
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
        name: 'super-administrators',
        path: '/identity/super-administrators',
        component: SuperAdministratorsView
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
