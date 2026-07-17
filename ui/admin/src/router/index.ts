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
      { path: '/403', component: StatusView, props: { code: '403', title: '没有访问权限', description: '当前身份无权访问此资源，请联系管理员核对权限策略。' } },
      { path: '/404', component: StatusView, props: { code: '404', title: '页面不存在', description: '目标页面可能已移动，或当前菜单尚未发布。' } },
      { path: '/500', component: StatusView, props: { code: '500', title: '服务暂时不可用', description: '请使用 TraceId 联系运维人员，并稍后重试。' } },
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
