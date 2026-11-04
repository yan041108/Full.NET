import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog } from 'element-plus';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import View from './DocumentPublicShareView.vue';
import * as api from '../api/document-shares';
import { openDocumentBlob } from '../api/host-document-items';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-shares', () => ({ accessDocumentShareByCode: vi.fn(), createDocumentSharePreviewTaskByCode: vi.fn(), getDocumentSharePreviewTaskByCode: vi.fn(), loadDocumentShareContentByCode: vi.fn(), loadDocumentSharePreviewTaskContentByCode: vi.fn() }));
vi.mock('../api/host-document-items', () => ({ openDocumentBlob: vi.fn() }));
const access = { shareId: outputId, documentId: outputId, shareCode: 'OLD', title: '文档', fileName: 'file.docx', mimeType: 'application/msword', fileSizeBytes: 12, hasPassword: false, accessCountRemaining: 9 };
let wrapper: VueWrapper, router: Router;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.accessDocumentShareByCode).mockImplementation(async code => ({ ...access, shareCode: code })); vi.mocked(api.createDocumentSharePreviewTaskByCode).mockResolvedValue(task); vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValue(task); vi.mocked(api.loadDocumentShareContentByCode).mockResolvedValue(new Blob(['source'])); vi.mocked(api.loadDocumentSharePreviewTaskContentByCode).mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' })); vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:public'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
});
afterEach(() => { wrapper?.unmount(); vi.useRealTimers(); vi.restoreAllMocks(); });
async function setup() {
  router = createRouter({ history: createMemoryHistory(), routes: [{ name: 'document-public-share', path: '/share/:shareCode', component: { render: () => null } }, { name: 'other', path: '/other', component: { render: () => null } }] }); await router.push('/share/OLD'); wrapper = mount(View, { global: { plugins: [router], stubs: { ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } }); await flushPromises();
}
describe('公开分享内容和任务恢复', () => {
  it('Office状态读取失败后重试同任务，不重复访问或创建', async () => {
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockRejectedValueOnce(new Error('offline')); await setup(); expect(wrapper.find('[data-testid="document-public-share-content-retry"]').exists()).toBe(true); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.accessDocumentShareByCode).toHaveBeenCalledOnce(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce(); expect(api.getDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(2); expect(wrapper.find('iframe').exists()).toBe(true);
  });
  it('Office输出读取失败重试时复核原任务再读输出，不重复创建', async () => {
    vi.mocked(api.loadDocumentSharePreviewTaskContentByCode).mockRejectedValueOnce(new Error('offline')); await setup(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce(); expect(api.getDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(2); expect(api.loadDocumentSharePreviewTaskContentByCode).toHaveBeenCalledTimes(2);
  });
  it('成功任务没有有效输出文件时不读取内容，重试仍使用原任务', async () => {
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValueOnce({ ...task, outputFileId: null }); await setup(); expect(api.loadDocumentSharePreviewTaskContentByCode).not.toHaveBeenCalled(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce(); expect(api.loadDocumentSharePreviewTaskContentByCode).toHaveBeenCalledOnce();
  });
  it('Office失败仍可下载原文件，下载错误不覆盖预览恢复错误', async () => {
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockRejectedValueOnce(new Error('preview')); await setup(); vi.mocked(api.loadDocumentShareContentByCode).mockRejectedValueOnce(new Error('download')); await wrapper.get('[data-testid="document-public-share-download"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="document-public-share-content-problem"]').exists()).toBe(true); expect(wrapper.find('[data-testid="document-public-share-download-problem"]').exists()).toBe(true); await wrapper.get('[data-testid="document-public-share-download"]').trigger('click'); await flushPromises(); expect(openDocumentBlob).toHaveBeenCalledOnce(); expect(wrapper.find('[data-testid="document-public-share-content-problem"]').exists()).toBe(true);
  });
  it('被动内容失败重试不重新消耗访问入口', async () => {
    vi.mocked(api.accessDocumentShareByCode).mockResolvedValueOnce({ ...access, mimeType: 'application/pdf' }); vi.mocked(api.loadDocumentShareContentByCode).mockRejectedValueOnce(new Error('offline')); await setup(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.accessDocumentShareByCode).toHaveBeenCalledOnce(); expect(api.loadDocumentShareContentByCode).toHaveBeenCalledTimes(2); expect(wrapper.find('iframe').exists()).toBe(true);
  });
  it.each([0, 302, 408, 503])('预览创建%s不确定时先人工确认，确认不自动创建或访问', async status => {
    vi.mocked(api.createDocumentSharePreviewTaskByCode).mockRejectedValueOnce({ status, code: 'unknown', title: 'unknown' }); await setup(); expect(wrapper.find('[data-testid="document-public-share-preview-recovery"]').exists()).toBe(true); expect(wrapper.find('[data-testid="document-public-share-content-retry"]').exists()).toBe(false); await wrapper.get('[data-testid="document-public-share-preview-recovery"]').trigger('click'); await flushPromises(); expect(wrapper.getComponent(ElDialog).props('modelValue')).toBe(true); await wrapper.get('[data-testid="document-public-share-preview-recovery-confirm"]').trigger('click'); await flushPromises(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(2); expect(api.accessDocumentShareByCode).toHaveBeenCalledOnce();
  });
  it('明确创建拒绝允许手动内容重试，不重新访问分享', async () => {
    vi.mocked(api.createDocumentSharePreviewTaskByCode).mockRejectedValueOnce({ status: 409, code: 'conflict', title: 'conflict' }); await setup(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(2); expect(api.accessDocumentShareByCode).toHaveBeenCalledOnce();
  });
  it('已失败任务可明确释放后人工再创建，不自动重复任务', async () => {
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValueOnce({ ...task, statusKey: 'failed' }); await setup(); await wrapper.get('[data-testid="document-public-share-preview-recovery"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="document-public-share-preview-recovery-confirm"]').trigger('click'); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(2);
  });
  it('任务状态回退或来源改变不读取输出，重试保留旧任务身份', async () => {
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValueOnce({ ...task, version: 3, statusKey: 'failed' }).mockResolvedValueOnce({ ...task, version: 2 }); await setup(); await wrapper.get('[data-testid="document-public-share-content-retry"]').trigger('click'); await flushPromises(); expect(api.loadDocumentSharePreviewTaskContentByCode).not.toHaveBeenCalled(); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce();
  });
  it.each([{ shareCode: 'OTHER' }, { documentId: '00000000-0000-0000-0000-000000000000' }])('访问元数据错配%j不得读取内容', async change => {
    vi.mocked(api.accessDocumentShareByCode).mockResolvedValueOnce({ ...access, ...change }); await setup(); expect(api.createDocumentSharePreviewTaskByCode).not.toHaveBeenCalled(); expect(api.loadDocumentShareContentByCode).not.toHaveBeenCalled();
  });
  it('旧内容重试和下载回调不能迁移到另一个分享', async () => {
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockRejectedValueOnce(new Error('offline')); await setup(); const retry = button('document-public-share-content-retry').vm.$.vnode.props!.onClick as () => void, download = button('document-public-share-download').vm.$.vnode.props!.onClick as () => void; await router.push('/share/NEW'); await flushPromises(); const reads = vi.mocked(api.getDocumentSharePreviewTaskByCode).mock.calls.length; retry(); download(); await flushPromises(); expect(api.getDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(reads); expect(api.loadDocumentShareContentByCode).not.toHaveBeenCalled();
  });
  it('旧恢复确认不能解除新分享的未知创建门槛', async () => {
    vi.mocked(api.createDocumentSharePreviewTaskByCode).mockRejectedValue(new Error('offline')); await setup(); await wrapper.get('[data-testid="document-public-share-preview-recovery"]').trigger('click'); await flushPromises(); const old = button('document-public-share-preview-recovery-confirm').vm.$.vnode.props!.onClick as () => void; await router.push('/share/NEW'); await flushPromises(); old(); await flushPromises(); expect(wrapper.find('[data-testid="document-public-share-content-retry"]').exists()).toBe(false); expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledTimes(2);
  });
  it('路由离开即取消内容和下载，迟到Blob不打开', async () => {
    await setup(); const pending = deferred<Blob>(); vi.mocked(api.loadDocumentShareContentByCode).mockReturnValueOnce(pending.promise); await wrapper.get('[data-testid="document-public-share-download"]').trigger('click'); const signal = vi.mocked(api.loadDocumentShareContentByCode).mock.calls[0]![2]!; await router.push('/other'); expect(signal.aborted).toBe(true); pending.resolve(new Blob(['private'])); await flushPromises(); expect(openDocumentBlob).not.toHaveBeenCalled(); expect(wrapper.find('iframe').exists()).toBe(false);
  });
});
