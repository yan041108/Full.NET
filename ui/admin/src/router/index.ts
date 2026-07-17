import {
  createRouter,
  createWebHashHistory,
  type RouterHistory
} from 'vue-router';
import OverviewView from '../views/OverviewView.vue';
import StatusView from '../views/StatusView.vue';

export function createAppRouter(history: RouterHistory = createWebHashHistory()) {
  return createRouter({
    history,
    routes: [
      { path: '/', component: OverviewView },
      {
        path: '/identity',
        component: StatusView,
        props: { code: 'C2.1', title: '身份权限模块', description: '用户、角色、菜单和按钮权限将在首个双管理端纵向切片中交付。' }
      },
      {
        path: '/organization',
        component: StatusView,
        props: { code: 'C2.1', title: '组织架构模块', description: '组织、职位和数据范围将与后端契约同步实现。' }
      },
      {
        path: '/settings',
        component: StatusView,
        props: { code: 'C2.2', title: '系统设置模块', description: '字典、配置和元数据将在快速交付阶段接入。' }
      },
      { path: '/403', component: StatusView, props: { code: '403', title: '没有访问权限', description: '当前身份无权访问此资源，请联系管理员核对权限策略。' } },
      { path: '/404', component: StatusView, props: { code: '404', title: '页面不存在', description: '目标页面可能已移动，或当前菜单尚未发布。' } },
      { path: '/500', component: StatusView, props: { code: '500', title: '服务暂时不可用', description: '请使用 TraceId 联系运维人员，并稍后重试。' } },
      { path: '/:pathMatch(.*)*', redirect: '/404' }
    ]
  });
}
