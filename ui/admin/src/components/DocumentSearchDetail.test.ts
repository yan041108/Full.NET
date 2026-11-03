import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import DocumentSearchDetail from './DocumentSearchDetail.vue';
import { useSessionStore } from '../auth/session';
const { get } = vi.hoisted(() => ({ get: vi.fn() })); vi.mock('../api/host-document-items', () => ({ getDocumentItem: get }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330', wrappers: VueWrapper[] = [];
async function render(target = id) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id, tenantId: null, sessionId: id, permissions: ['document.host_documents.read'] } as NonNullable<typeof session.currentUser>;
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/documents', name: 'host-document-items', component: { template: '<div />' } }] });
  await router.push({ name: 'host-document-items', query: { documentId: target } }); await router.isReady();
  const wrapper = mount(DocumentSearchDetail, { global: { plugins: [pinia, router], stubs: { ElDrawer: { props: ['modelValue'], template: '<section v-if="modelValue"><slot /><slot name="footer"/></section>' } } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, router, session };
}
beforeEach(() => { get.mockReset(); get.mockResolvedValue({ id, title: 'Authorized title', documentNo: 'DOC-1', description: '<script>literal</script>' }); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });
it('固定 UUID 详情重新调用来源，正文按文本展示，关闭清理深链', async () => {
  const { wrapper, router } = await render(); expect(get).toHaveBeenCalledWith(id, expect.any(AbortSignal)); expect(wrapper.text()).toContain('Authorized title');
  expect(wrapper.text()).toContain('<script>literal</script>'); expect(wrapper.find('script').exists()).toBe(false);
  await wrapper.get('button').trigger('click'); await flushPromises(); expect(router.currentRoute.value.query.documentId).toBeUndefined();
});
it('非法目标不请求；来源拒绝时明确失败，不展示上次对象', async () => {
  await render('https://example.com'); expect(get).not.toHaveBeenCalled(); get.mockRejectedValue(new Error('403'));
  const { wrapper } = await render(); expect(wrapper.text()).toContain('无法读取此对象'); expect(wrapper.text()).not.toContain('Authorized title');
});
it('撤权立即取消详情，清理旧目标并忽略迟到正文', async () => {
  let resolve!: (value: unknown) => void; get.mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session, router } = await render(); const signal = get.mock.calls[0]![1] as AbortSignal;
  session.currentUser = { ...session.currentUser!, permissions: [] }; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve({ id, title: 'Late secret' }); await flushPromises(); expect(wrapper.text()).not.toContain('Late secret'); expect(router.currentRoute.value.query.documentId).toBeUndefined();
});
