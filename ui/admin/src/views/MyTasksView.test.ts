import { afterEach, describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import MyTasksView from './MyTasksView.vue';
import { useSessionStore } from '../auth/session';
const wrappers: VueWrapper[] = [];
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
function render(permissions: string[], extra = {}) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id: 'user', tenantId: 'tenant', permissions, ...extra } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(MyTasksView, { global: { plugins: [pinia], stubs: { MyTaskSourcePanel: { props: ['source'], template: '<div :data-source="source" />' } } } });
  wrappers.push(wrapper); return wrapper;
}
describe('我的任务入口', () => {
  it('只装配当前权限目录里的来源，超级管理员通配不猜测安装状态', () => {
    const wrapper = render(['platform.my_tasks.read', 'reporting.export_tasks.read'], { isSuperAdministrator: true });
    expect(wrapper.findAll('[data-source]').map(item => item.attributes('data-source'))).toEqual(['reports']);
  });
  it('缺入口资格或处于 Host 时不读取来源', () => {
    expect(render(['reporting.export_tasks.read']).find('[data-source]').exists()).toBe(false);
    expect(render(['platform.my_tasks.read', 'reporting.export_tasks.read'], { tenantId: undefined }).find('[data-source]').exists()).toBe(false);
  });
});
