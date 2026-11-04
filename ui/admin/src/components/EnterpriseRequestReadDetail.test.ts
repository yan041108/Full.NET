import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import EnterpriseRequestReadDetail from './EnterpriseRequestReadDetail.vue';
import { useSessionStore } from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../api/enterprise-requests', () => ({ enterpriseRequestsHttp: {}, createEnterpriseRequestsApi: () => ({ get }) }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330', wrappers: VueWrapper[] = [];
function render() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);session.state='authenticated';
  session.currentUser = { id, tenantId: id, sessionId: id, permissions: ['enterprise_request.enterprise_requests.read'] } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(EnterpriseRequestReadDetail, { props: { modelValue: true, requestId: id }, global: { plugins: [pinia],
    stubs: { ElDrawer: { props: ['modelValue'], template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>' } } } }); wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { get.mockReset(); get.mockResolvedValue({ id, title: 'Read-only request', requestNumber: 'REQ-1', status:'Draft',totalAmount: '999.00' }); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });
it('仅主单读取权可重新读取摘要详情，不加载明细或金额展示', async () => {
  const { wrapper } = render(); await flushPromises(); expect(get).toHaveBeenCalledExactlyOnceWith(id);
  expect(wrapper.text()).toContain('Read-only request'); expect(wrapper.text()).not.toContain('999.00'); expect(wrapper.find('input').exists()).toBe(false);
});
it('权限变化关闭详情，迟到结果及不匹配的对象身份均失败关闭', async () => {
  let resolve!: (value: unknown) => void; get.mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session } = render(); session.currentUser = { ...session.currentUser!, permissions: [] }; await flushPromises();
  resolve({ id, title: 'Late request' }); await flushPromises(); expect(wrapper.text()).not.toContain('Late request'); expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([false]);
  get.mockResolvedValue({ id: id.slice(0, -1) + '1', title: 'Wrong object', requestNumber: 'REQ-2' });
  const next = render(); await flushPromises(); expect(next.wrapper.text()).toContain('无法读取此对象'); expect(next.wrapper.text()).not.toContain('Wrong object');
});
it('已知状态随语言显示，未知机器状态保留原值且不显示金额',async()=>{
  const {wrapper}=render();try{
    await flushPromises();expect(wrapper.text()).toContain('草稿');useAdminI18n().setLocale('en-US');await flushPromises();expect(wrapper.text()).toContain('Draft');expect(wrapper.text()).toContain('REQ-1');expect(wrapper.text()).not.toContain('999.00');
    get.mockResolvedValue({id,title:'Unknown state',requestNumber:'REQ-2',status:'FutureStage',totalAmount:'123.00'});await wrapper.setProps({modelValue:false});await wrapper.setProps({modelValue:true});await flushPromises();expect(wrapper.text()).toContain('FutureStage');expect(wrapper.text()).not.toContain('123.00');
  }finally{useAdminI18n().setLocale('zh-CN');}
});
