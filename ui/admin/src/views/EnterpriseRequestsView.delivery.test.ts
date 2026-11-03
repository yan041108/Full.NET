import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import View from './EnterpriseRequestsView.vue';
import { useSessionStore } from '../auth/session';

const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('../api/enterprise-requests', async original => ({ ...await original<typeof import('../api/enterprise-requests')>(), enterpriseRequestsHttp: { request } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const row = { id, tenantId: id, organizationUnitId: id, requestNumber: 'DELIVERY', title: '持续交付', status: 'Draft',
  totalAmount: '1.00', applicantUserId: id, createdAtUtc: '2026-11-03T00:00:00Z', createdById: id,
  updatedAtUtc: null, updatedById: null, deletedAtUtc: null, deletedById: null, isDeleted: false, version: '1' };
const wrappers: VueWrapper[] = [];
let mounts = 0;
beforeEach(() => { mounts = 0; request.mockReset().mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 }); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
async function render() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id, tenantId: id, sessionId: id, permissions: ['enterprise_request.enterprise_requests.read',
    'enterprise_request.attachments.manage'] } as NonNullable<typeof session.currentUser>;
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', name: 'enterprise-requests', component: { template: '<div />' } }] });
  await router.push('/'); await router.isReady();
  const wrapper = mount(View, { global: { plugins: [pinia, router], stubs: { EnterpriseRequestAggregateEditor: true,
    EnterpriseRequestDelivery: { props: ['modelValue', 'requestId'], emits: ['updated'], mounted() { mounts++; },
      template: '<section v-if="modelValue" data-testid="delivery-dialog">{{ requestId }}<button @click="$emit(\'updated\')">Uploaded</button></section>' }
  } } }); wrappers.push(wrapper); await flushPromises();
  await wrapper.findAll('button').find(button => button.text() === '附件与文档')!.trigger('click'); await flushPromises();
  return { wrapper, session };
}
it('附件更新刷新并替换表格行后，文档弹窗继续保持同一实例', async () => {
  const { wrapper } = await render(); expect(mounts).toBe(1);
  request.mockResolvedValue({ items: [{ ...row, version: '2' }], page: 1, pageSize: 20, total: 1 });
  await wrapper.get('[data-testid="delivery-dialog"] button').trigger('click'); await flushPromises();
  expect(request).toHaveBeenCalledTimes(2); expect(mounts).toBe(1);
  expect(wrapper.get('[data-testid="delivery-dialog"]').text()).toContain(id);
});
it('会话切换关闭文档弹窗并移除旧单据身份', async () => {
  const { wrapper, session } = await render(); session.switching = true; await flushPromises();
  expect(wrapper.find('[data-testid="delivery-dialog"]').exists()).toBe(false);
  expect(wrapper.findComponent({ name: 'EnterpriseRequestDelivery' }).exists()).toBe(false);
});
