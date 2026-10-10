import * as feedback from '../feedback/fullNetMessage';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import DocumentSharesView from './DocumentSharesView.vue';
import * as api from '../api/document-shares';
import { listDocumentItems } from '../api/host-document-items';
import { createShareSession, documentShare as share, shareDocument as doc } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/document-shares', () => ({ listDocumentShares: vi.fn(), createDocumentShare: vi.fn(), batchCreateDocumentShares: vi.fn(), updateDocumentShareStatus: vi.fn() }));
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
beforeEach(() => Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: vi.fn() } }));
afterEach(() => { if (clipboardDescriptor)
    Object.defineProperty(navigator, 'clipboard', clipboardDescriptor);
else
    Reflect.deleteProperty(navigator, 'clipboard'); });
let wrapper: VueWrapper | undefined;
const read = 'document.host_shares.read', write = 'document.host_shares.update_status', docs = 'document.host_documents.read';
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.listDocumentShares).mockResolvedValue({ items: [share], page: 1, pageSize: 20, total: 1 }); vi.mocked(listDocumentItems).mockResolvedValue({ items: [doc], page: 1, pageSize: 100, total: 1 }); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const setup = async (permissions = [read, write, docs], tenant = false) => { const context = createShareSession(permissions); if (tenant)
    Object.assign(context.session.currentUser!, { tenantId: share.id, scope: 'tenant:' + share.id.replaceAll('-', '') }); wrapper = mount(DocumentSharesView, { global: { plugins: [context.pinia] } }); await flushPromises(); return context.session; };
describe('文档分享管理页请求归属', () => {
    it.each([{ permissions: [] }, { permissions: [write] }])('无父读取权限不加载目录或分享 %j', async ({ permissions }) => { await setup(permissions); expect(api.listDocumentShares).not.toHaveBeenCalled(); expect(listDocumentItems).not.toHaveBeenCalled(); });
    it('租户作用域不能读取Host分享目录', async () => { await setup([read, write, docs], true); expect(api.listDocumentShares).not.toHaveBeenCalled(); expect(listDocumentItems).not.toHaveBeenCalled(); });
    it('会话替换取消旧列表并仅接入新列表', async () => { const pending = deferred<{
        items: typeof share[];
        page: number;
        pageSize: number;
        total: number;
    }>(); vi.mocked(api.listDocumentShares).mockReturnValueOnce(pending.promise).mockResolvedValue({ items: [{ ...share, shareCode: 'SHARE-NEW' }], page: 1, pageSize: 20, total: 1 }); const session = await setup(); const signal = vi.mocked(api.listDocumentShares).mock.calls[0]?.[3]; session.currentUser!.sessionId = 'replacement'; expect(signal?.aborted).toBe(true); await flushPromises(); pending.resolve({ items: [share], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(wrapper!.text()).toContain('SHARE-NEW'); expect(wrapper!.text()).not.toContain('SHARE-OLD'); });
    it('撤销文档目录权限取消目录，旧标签不继续渲染', async () => { const pending = deferred<{
        items: typeof doc[];
        page: number;
        pageSize: number;
        total: number;
    }>(); vi.mocked(listDocumentItems).mockReturnValue(pending.promise); const session = await setup(); const signal = vi.mocked(listDocumentItems).mock.calls[0]?.[3]; session.currentUser!.permissions = [read, write]; expect(signal?.aborted).toBe(true); pending.resolve({ items: [doc], page: 1, pageSize: 100, total: 1 }); await flushPromises(); expect(wrapper!.text()).not.toContain(doc.title); });
    it('更新防重复，撤权取消且迟到成功不反馈或刷新', async () => { const pending = deferred<typeof share>(); vi.mocked(api.updateDocumentShareStatus).mockReturnValue(pending.promise); const success = vi.spyOn(feedback, 'showSuccess'); const session = await setup(); const toggle = wrapper!.get('[data-testid="document-share-toggle"]'); await toggle.trigger('click'); await toggle.trigger('click'); expect(api.updateDocumentShareStatus).toHaveBeenCalledOnce(); const signal = vi.mocked(api.updateDocumentShareStatus).mock.calls[0]?.[2]; session.currentUser!.permissions = [read, docs]; expect(signal?.aborted).toBe(true); await flushPromises(); const count = vi.mocked(api.listDocumentShares).mock.calls.length; pending.resolve({ ...share, isEnabled: false, version: 2 }); await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(api.listDocumentShares).toHaveBeenCalledTimes(count); });
    it('剪贴板迟到完成在注销后不显示成功', async () => { const pending = deferred<void>(); vi.spyOn(navigator.clipboard, 'writeText').mockReturnValue(pending.promise); const success = vi.spyOn(feedback, 'showSuccess'); const session = await setup(); await wrapper!.get('[data-testid="document-share-copy-link"]').trigger('click'); session.state = 'anonymous'; pending.resolve(); await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(wrapper!.text()).not.toContain('SHARE-OLD'); });
});
