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
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); sessionStorage.removeItem('documentShares.recentBatch'); });
const setup = async (permissions = [read, write, docs], tenant = false) => { const context = createShareSession(permissions); if (tenant)
    Object.assign(context.session.currentUser!, { tenantId: share.id, scope: 'tenant:' + share.id.replaceAll('-', '') }); wrapper = mount(DocumentSharesView, { global: { plugins: [context.pinia] } }); await flushPromises(); return context.session; };
describe('文档分享管理页请求归属', () => {
    it.each(['getItem', 'removeItem'] as const)('临时存储 %s 失败不阻断目录读取', async method => {
      sessionStorage.setItem('documentShares.recentBatch', JSON.stringify({ succeeded: 1, total: 2, userId: share.id, sessionId: share.id }));
      vi.spyOn(Storage.prototype, method).mockImplementation(() => { throw new Error('storage blocked'); });
      await setup(); expect(api.listDocumentShares).toHaveBeenCalledOnce(); expect(listDocumentItems).toHaveBeenCalledOnce();
    });
    it('当前账号与会话的批次提示可消费且只消费一次', async () => {
      sessionStorage.setItem('documentShares.recentBatch', JSON.stringify({ succeeded: 1, total: 2, userId: share.id, sessionId: share.id }));
      await setup(); expect((wrapper!.vm as unknown as { batchShareHint: unknown }).batchShareHint).toEqual({ succeeded: 1, total: 2 });
      expect(sessionStorage.getItem('documentShares.recentBatch')).toBeNull();
    });
    it.each(['legacy', 'other-session', 'other-user'] as const)('不消费 %s 的批次提示', async kind => {
      sessionStorage.setItem('documentShares.recentBatch', JSON.stringify({ succeeded: 1, total: 2,
        userId: kind === 'legacy' ? undefined : kind === 'other-user' ? doc.id : share.id,
        sessionId: kind === 'legacy' ? undefined : kind === 'other-session' ? doc.id : share.id }));
      await setup(); expect((wrapper!.vm as unknown as { batchShareHint: unknown }).batchShareHint).toBeNull();
    });
    it.each([{ succeeded: -1, total: 2 }, { succeeded: 3, total: 2 }, { succeeded: 1, total: 0 }, { succeeded: 1, total: 2.5 }])('畸形批次计数 %j 不展示', async count => {
      sessionStorage.setItem('documentShares.recentBatch', JSON.stringify({ ...count, userId: share.id, sessionId: share.id }));
      await setup(); expect((wrapper!.vm as unknown as { batchShareHint: unknown }).batchShareHint).toBeNull();
    });
    it('畸形 Host 上下文带租户标识不能读取或修改', async () => {
      const session = await setup(); const count = vi.mocked(api.listDocumentShares).mock.calls.length;
      session.currentUser!.tenantId = share.id; await flushPromises();
      expect(api.listDocumentShares).toHaveBeenCalledTimes(count); expect(wrapper!.find('[data-testid="document-share-toggle"]').exists()).toBe(false);
    });
    it('旧修订或不在目录中的分享不能直接切换状态', async () => {
      await setup(); const vm = wrapper!.vm as unknown as { toggleStatus: (row: typeof share) => Promise<void> };
      await vm.toggleStatus({ ...share, version: 99 }); await vm.toggleStatus({ ...share, id: doc.id });
      expect(api.updateDocumentShareStatus).not.toHaveBeenCalled();
    });
    it.each(['identity', 'document', 'status'] as const)('状态响应 %s 错配不反馈成功或刷新', async mismatch => {
      vi.mocked(api.updateDocumentShareStatus).mockResolvedValueOnce({ ...share, isEnabled: mismatch === 'status',
        id: mismatch === 'identity' ? doc.id : share.id, documentId: mismatch === 'document' ? share.id : share.documentId });
      const success = vi.spyOn(feedback, 'showSuccess'); await setup(); await wrapper!.get('[data-testid="document-share-toggle"]').trigger('click'); await flushPromises();
      expect(success).not.toHaveBeenCalled(); expect(api.listDocumentShares).toHaveBeenCalledOnce();
    });
    it('健康状态更新冻结原修订并刷新列表', async () => {
      vi.mocked(api.updateDocumentShareStatus).mockResolvedValueOnce({ ...share, isEnabled: false, version: 2 });
      await setup(); await wrapper!.get('[data-testid="document-share-toggle"]').trigger('click'); await flushPromises();
      expect(api.updateDocumentShareStatus).toHaveBeenCalledExactlyOnceWith(share.id, { isEnabled: false, version: share.version }, expect.any(AbortSignal));
      expect(api.listDocumentShares).toHaveBeenCalledTimes(2);
    });
    it('不能通过复制入口复制当前目录之外的URL', async () => {
      await setup(); const vm = wrapper!.vm as unknown as { copyShareLink: (url: string) => Promise<void> };
      await vm.copyShareLink('https://invalid.example/unknown'); expect(navigator.clipboard.writeText).not.toHaveBeenCalled();
    });
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
