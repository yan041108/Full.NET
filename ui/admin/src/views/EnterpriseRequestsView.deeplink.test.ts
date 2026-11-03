import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import View from './EnterpriseRequestsView.vue';
import { useSessionStore } from '../auth/session';
import { defaultEnterpriseRequestQuery } from './enterprise-requests/enterprise-request-query';

const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('../api/enterprise-requests', async original => ({ ...await original<typeof import('../api/enterprise-requests')>(), enterpriseRequestsHttp: { request } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const permissions = ['enterprise_request.enterprise_requests.read', 'enterprise_request.enterprise_request_lines.read'];
const wrappers: VueWrapper[] = [];
async function render(requestId: string, allowed = permissions) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id, tenantId: id, sessionId: id, permissions: allowed } as NonNullable<typeof session.currentUser>;
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/enterprise-requests', name: 'enterprise-requests', component: { template: '<div />' } }] });
  await router.push({ name: 'enterprise-requests', query: { requestId } }); await router.isReady();
  const wrapper = mount(View, { global: { plugins: [pinia, router], stubs: { EnterpriseRequestDelivery: true,
    QuerySchemePanel: { emits: ['apply', 'ready'], template: '<section data-testid="query-panel-stub"/>' },
    EnterpriseRequestReadDetail: { props: ['modelValue', 'requestId'], emits: ['update:modelValue'],
      template: '<section v-if="modelValue" data-testid="read-document">{{ requestId }}<button @click="$emit(\'update:modelValue\', false)">Close</button></section>' },
    EnterpriseRequestAggregateEditor: { props: ['modelValue', 'requestId'], emits: ['update:modelValue'],
      template: '<section v-if="modelValue" data-testid="business-document">{{ requestId }}<button @click="$emit(\'update:modelValue\', false)">Close</button></section>' }
  } } }); wrappers.push(wrapper); await flushPromises(); return { wrapper, session, router };
}
beforeEach(() => { request.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.clearAllMocks(); });
it('可信业务 UUID 自动打开详情，关闭后移除深链查询避免重复打开', async () => {
  const { wrapper, router } = await render(id); expect(wrapper.get('[data-testid="business-document"]').text()).toContain(id);
  await wrapper.get('[data-testid="business-document"] button').trigger('click'); await flushPromises();
  expect(router.currentRoute.value.query.requestId).toBeUndefined(); expect(wrapper.find('[data-testid="business-document"]').exists()).toBe(false);
});
it('非法标识与缺主单读取权限均不自动打开单据', async () => {
  const invalid = await render('https://example.com'); expect(invalid.wrapper.find('[data-testid="business-document"]').exists()).toBe(false);
  const forbidden = await render(id, []); expect(forbidden.wrapper.find('[data-testid="business-document"]').exists()).toBe(false);
  expect(forbidden.wrapper.find('[data-testid="read-document"]').exists()).toBe(false);
});
it('只有主单读取权时打开只读主单，关闭后清理目标，不隐式读取明细', async () => {
  const { wrapper, router } = await render(id, [permissions[0]!]);
  expect(wrapper.find('[data-testid="business-document"]').exists()).toBe(false);
  expect(wrapper.get('[data-testid="read-document"]').text()).toContain(id);
  await wrapper.get('[data-testid="read-document"] button').trigger('click'); await flushPromises();
  expect(router.currentRoute.value.query.requestId).toBeUndefined();
});
it('会话切换立即关闭旧单据并清除深链', async () => {
  const { wrapper, session, router } = await render(id); session.switching = true; await flushPromises();
  expect(wrapper.find('[data-testid="business-document"]').exists()).toBe(false); expect(router.currentRoute.value.query.requestId).toBeUndefined();
});
it('深链打开期间恢复的默认方案在详情关闭后应用到真实列表条件', async () => {
  const { wrapper } = await render(id, [permissions[0]!, 'settings.query_schemes.read']);
  const configuration = defaultEnterpriseRequestQuery(); configuration.filters = [{ fieldKey: 'title', operatorKey: 'contains', value: 'Saved condition' }];
  const panel = wrapper.findComponent('[data-testid="query-panel-stub"]') as VueWrapper; panel.vm.$emit('apply', configuration); panel.vm.$emit('ready'); await flushPromises();
  expect(request).not.toHaveBeenCalled(); await wrapper.get('[data-testid="read-document"] button').trigger('click'); await flushPromises();
  expect(request).toHaveBeenCalled(); expect(new URL(request.mock.calls.at(-1)![0], 'https://example.test').searchParams.get('filterTitle')).toBe('Saved condition');
});
it('已打开的深链改为非法目标时关闭旧对象，不保留与地址不符的详情', async () => {
  const { wrapper, router } = await render(id, [permissions[0]!]);
  expect(wrapper.find('[data-testid="read-document"]').exists()).toBe(true);
  await router.replace({ query: { requestId: 'https://example.com' } }); await flushPromises();
  expect(wrapper.find('[data-testid="read-document"]').exists()).toBe(false);
});
