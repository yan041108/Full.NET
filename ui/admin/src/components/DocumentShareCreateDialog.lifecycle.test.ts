import * as feedback from '../feedback/fullNetMessage';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElInput } from 'element-plus';
import DocumentShareCreateDialog from './DocumentShareCreateDialog.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as shares from '../api/document-shares';
import { listDocumentItems } from '../api/host-document-items';
import { createShareSession, documentShare as share, shareDocument as doc } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/document-shares', () => ({ createDocumentShare: vi.fn(), batchCreateDocumentShares: vi.fn() }));
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
let wrapper: VueWrapper | undefined;
const read = 'document.host_shares.read', create = 'document.host_shares.create', docs = 'document.host_documents.read';
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listDocumentItems).mockResolvedValue({ items: [doc], page: 1, pageSize: 100, total: 1 }); vi.mocked(shares.createDocumentShare).mockResolvedValue(share); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const setup = async (preset = true, permissions = [read, create, docs], initialOpen = false) => { const context = createShareSession(permissions); wrapper = mount(DocumentShareCreateDialog, { props: { open: initialOpen, presetDocument: preset ? doc : null }, global: { plugins: [context.pinia], stubs: { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } }); if (!initialOpen)
    await wrapper!.setProps({ open: true }); await flushPromises(); return context.session; };
const submit = () => wrapper!.getComponent(ArtFormDialog).vm.$emit('confirm');
describe('文档分享创建请求归属', () => {
    it('关闭后父open尚未回写也不能重新提交', async () => { await setup(); wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open', false); submit(); await flushPromises(); expect(shares.createDocumentShare).not.toHaveBeenCalled(); });
    it('单项数组预设优先于另一个单项预设', async () => { await setup(); await wrapper!.setProps({ open: false, presetDocuments: [{ ...doc, id: share.id }] }); await wrapper!.setProps({ open: true }); await flushPromises(); submit(); expect(vi.mocked(shares.createDocumentShare).mock.calls[0]?.[0].documentId).toBe(share.id); });
    it.each(['resolve', 'reject'] as const)('批量关闭后迟到%s不发事件和反馈', async (outcome) => { const pending = deferred<{
        succeededCount: number;
        results: never[];
    }>(); vi.mocked(shares.batchCreateDocumentShares).mockReturnValue(pending.promise); const error = vi.spyOn(feedback, 'showError'); await setup(); await wrapper!.setProps({ open: false, presetDocuments: [doc, { ...doc, id: share.id }] }); await wrapper!.setProps({ open: true }); await flushPromises(); submit(); submit(); expect(shares.batchCreateDocumentShares).toHaveBeenCalledOnce(); const signal = vi.mocked(shares.batchCreateDocumentShares).mock.calls[0]?.[1]; wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open', false); expect(signal?.aborted).toBe(true); if (outcome === 'resolve')
        pending.resolve({ succeededCount: 2, results: [] });
    else
        pending.reject(new Error('late')); await flushPromises(); expect(wrapper!.emitted('batchCreated')).toBeUndefined(); expect(error).not.toHaveBeenCalled(); });
    it('初始打开的预设文档无需手填即可提交且防重复', async () => { const pending = deferred<typeof share>(); vi.mocked(shares.createDocumentShare).mockReturnValue(pending.promise); await setup(true, [read, create, docs], true); submit(); submit(); expect(shares.createDocumentShare).toHaveBeenCalledOnce(); expect(vi.mocked(shares.createDocumentShare).mock.calls[0]?.[0].documentId).toBe(doc.id); pending.resolve(share); await flushPromises(); expect(wrapper!.emitted('created')).toHaveLength(1); });
    it.each(['resolve', 'reject'] as const)('关闭意图立即取消，迟到%s无事件、反馈或密码残留', async (outcome) => { const pending = deferred<typeof share>(); vi.mocked(shares.createDocumentShare).mockReturnValue(pending.promise); const error = vi.spyOn(feedback, 'showError'); await setup(); await wrapper!.findAllComponents(ElInput)[1]!.setValue('Password@2026'); submit(); const signal = vi.mocked(shares.createDocumentShare).mock.calls[0]?.[1]; wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open', false); expect(signal?.aborted).toBe(true); if (outcome === 'resolve')
        pending.resolve(share);
    else
        pending.reject(new Error('late')); await flushPromises(); expect(wrapper!.emitted('created')).toBeUndefined(); expect(error).not.toHaveBeenCalled(); await wrapper!.setProps({ open: false }); await wrapper!.setProps({ open: true }); await flushPromises(); expect(wrapper!.findAllComponents(ElInput)[1]!.props('modelValue')).toBe(''); });
    it('撤销创建权限同步取消并清理弹窗DOM', async () => { const pending = deferred<typeof share>(); vi.mocked(shares.createDocumentShare).mockReturnValue(pending.promise); const session = await setup(); submit(); const signal = vi.mocked(shares.createDocumentShare).mock.calls[0]?.[1]; session.currentUser!.permissions = [read, docs]; expect(signal?.aborted).toBe(true); pending.resolve(share); await flushPromises(); expect(wrapper!.find('[data-testid="document-share-editor-submit"]').exists()).toBe(false); expect(wrapper!.emitted('created')).toBeUndefined(); });
    it('没有文档目录权限不调用目录API，没有创建权限不显示提交', async () => { await setup(false, [read]); expect(listDocumentItems).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="document-share-editor-submit"]').exists()).toBe(false); });
    it('预设对象替换同步取消，不将旧分享发给新对象', async () => { const pending = deferred<typeof share>(); vi.mocked(shares.createDocumentShare).mockReturnValue(pending.promise); await setup(); submit(); const signal = vi.mocked(shares.createDocumentShare).mock.calls[0]?.[1]; await wrapper!.setProps({ presetDocument: { ...doc, id: share.id, title: '新对象' } }); expect(signal?.aborted).toBe(true); pending.resolve(share); await flushPromises(); expect(wrapper!.emitted('created')).toBeUndefined(); });
    it('仅有文档库read与share create可通过显式父权限分享', async () => { const context = createShareSession([docs, create]); wrapper = mount(DocumentShareCreateDialog, { props: { open: true, presetDocument: doc, parentReadPermission: docs }, global: { plugins: [context.pinia] } }); await flushPromises(); submit(); await flushPromises(); expect(shares.createDocumentShare).toHaveBeenCalledOnce(); });
    it.each(['abc', '0', '-1', '1.5', '2147483648'])('非法访问上限%s拒绝，不变成无限次', async (value) => { await setup(); await wrapper!.findAllComponents(ElInput)[2]!.setValue(value); submit(); await flushPromises(); expect(shares.createDocumentShare).not.toHaveBeenCalled(); });
    it.each(['1.5', '366'])('有效天数%s拒绝', async (value) => { await setup(); await wrapper!.findAllComponents(ElInput)[0]!.setValue(value); submit(); await flushPromises(); expect(shares.createDocumentShare).not.toHaveBeenCalled(); });
});
