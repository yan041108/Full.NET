import * as feedback from '../feedback/fullNetMessage';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import HostDocumentItemsView from './HostDocumentItemsView.vue';
import DocumentShareCreateDialog from '../components/DocumentShareCreateDialog.vue';
import { createShareSession, documentShare as share, shareDocument as doc } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn(async () => ({ items: [doc], page: 1, pageSize: 100, total: 1 })), createDocumentItem: vi.fn(), deleteDocumentItem: vi.fn(), downloadDocumentContent: vi.fn(), listDocumentVersions: vi.fn(), openDocumentBlob: vi.fn(), previewDocumentContent: vi.fn(), restoreDocumentItem: vi.fn(), rollbackDocumentVersion: vi.fn(), deleteDocumentVersion: vi.fn(), updateDocumentItem: vi.fn(), uploadDocumentVersion: vi.fn() }));
vi.mock('../api/host-document-categories', () => ({ listDocumentCategories: vi.fn(async () => []) }));
vi.mock('../api/host-document-tags', () => ({ listDocumentTags: vi.fn(async () => []) }));
vi.mock('../api/document-shares', () => ({ createDocumentShare: vi.fn(), batchCreateDocumentShares: vi.fn() }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }));
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
beforeEach(() => Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: vi.fn() } }));
afterEach(() => { if (clipboardDescriptor)
    Object.defineProperty(navigator, 'clipboard', clipboardDescriptor);
else
    Reflect.deleteProperty(navigator, 'clipboard'); });
let wrapper: VueWrapper | undefined;
beforeEach(() => vi.clearAllMocks());
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
describe('文档库分享消费者边界', () => {
    it('批量入口只选一项时仍锁定已选文档', async () => { const context = createShareSession(['document.host_documents.read', 'document.host_shares.create']); wrapper = mount(HostDocumentItemsView, { global: { plugins: [context.pinia] } }); await flushPromises(); wrapper.getComponent({ name: 'ElTable' }).vm.$emit('selection-change', [doc]); await flushPromises(); await wrapper.get('[data-testid="host-document-item-batch-share"]').trigger('click'); await flushPromises(); expect(wrapper.getComponent(DocumentShareCreateDialog).props('presetDocuments')).toEqual([doc]); });
    it('真实行分享点击不会被同轮预设属性更新关闭', async () => { const context = createShareSession(['document.host_documents.read', 'document.host_shares.create']); wrapper = mount(HostDocumentItemsView, { global: { plugins: [context.pinia] } }); await flushPromises(); await wrapper.get('[data-testid="host-document-item-share"]').trigger('click'); await flushPromises(); expect(wrapper.getComponent(DocumentShareCreateDialog).props('open')).toBe(true); expect(wrapper.getComponent(DocumentShareCreateDialog).getComponent(ArtFormDialog).props('open')).toBe(true); });
    it('真实批量选择与点击保持弹窗打开且保留两个预设对象', async () => { const context = createShareSession(['document.host_documents.read', 'document.host_shares.create']); wrapper = mount(HostDocumentItemsView, { global: { plugins: [context.pinia] } }); await flushPromises(); wrapper.getComponent({ name: 'ElTable' }).vm.$emit('selection-change', [doc, { ...doc, id: share.id }]); await flushPromises(); await wrapper.get('[data-testid="host-document-item-batch-share"]').trigger('click'); await flushPromises(); const dialog = wrapper.getComponent(DocumentShareCreateDialog); expect(dialog.props('open')).toBe(true); expect(dialog.props('presetDocuments')).toHaveLength(2); expect(dialog.getComponent(ArtFormDialog).props('open')).toBe(true); });
    it('文档库父权限传给分享弹窗，不依赖分享列表read', async () => { const context = createShareSession(['document.host_documents.read', 'document.host_shares.create']); wrapper = mount(HostDocumentItemsView, { global: { plugins: [context.pinia] } }); await flushPromises(); expect(wrapper.getComponent(DocumentShareCreateDialog).props('parentReadPermission')).toBe('document.host_documents.read'); });
    it('创建后的剪贴板完成在撤权后不反馈或清理新对象', async () => { const context = createShareSession(['document.host_documents.read', 'document.host_shares.create']); wrapper = mount(HostDocumentItemsView, { global: { plugins: [context.pinia] } }); await flushPromises(); const pending = deferred<void>(); vi.spyOn(navigator.clipboard, 'writeText').mockReturnValue(pending.promise); const success = vi.spyOn(feedback, 'showSuccess'); wrapper.getComponent(DocumentShareCreateDialog).vm.$emit('created', share, 'https://example.test/share'); context.session.currentUser!.permissions = ['document.host_documents.read']; pending.resolve(); await flushPromises(); expect(success).not.toHaveBeenCalled(); });
});
