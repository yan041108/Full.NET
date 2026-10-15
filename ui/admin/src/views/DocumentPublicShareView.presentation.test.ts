import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import DocumentPublicShareView from './DocumentPublicShareView.vue';
import * as api from '../api/document-shares';
import { openDocumentBlob } from '../api/host-document-items';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-shares', () => ({ accessDocumentShareByCode: vi.fn(), createDocumentSharePreviewTaskByCode: vi.fn(),
  getDocumentSharePreviewTaskByCode: vi.fn(), loadDocumentShareContentByCode: vi.fn(), loadDocumentSharePreviewTaskContentByCode: vi.fn() }));
vi.mock('../api/host-document-items', () => ({ openDocumentBlob: vi.fn() }));
const access = { shareId: outputId, documentId: outputId, shareCode: 'OLD', title: '分享文档', fileName: 'sample.pdf', mimeType: 'application/pdf', fileSizeBytes: 12, hasPassword: false, accessCountRemaining: 9 };
let wrapper: VueWrapper | undefined; let router: Router; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.accessDocumentShareByCode).mockResolvedValue(access);
  vi.mocked(api.loadDocumentShareContentByCode).mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' }));
  vi.mocked(api.createDocumentSharePreviewTaskByCode).mockResolvedValue(documentPreviewTask);
  vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValue(documentPreviewTask);
  vi.mocked(api.loadDocumentSharePreviewTaskContentByCode).mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' }));
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:share'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
});
afterEach(() => { wrapper?.unmount(); vi.useRealTimers(); vi.restoreAllMocks(); });
async function setup(keepAlive = false) {
  visible = ref(true); router = createRouter({ history: createMemoryHistory(), routes: [{ name: 'document-public-share', path: '/share/:shareCode', component: { render: () => null } }] });
  await router.push('/share/OLD');
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(DocumentPublicShareView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : DocumentPublicShareView, { global: { plugins: [router] } }); await flushPromises();
}
describe('公开文档内容隔离与停用', () => {
  it('内嵌 PDF 使用无额外能力的 sandbox 和无来源引用', async () => {
    await setup(); expect(wrapper!.get('iframe').attributes('sandbox')).toBe(''); expect(wrapper!.get('iframe').attributes('referrerpolicy')).toBe('no-referrer');
  });
  it.each(['text/html', 'image/svg+xml', 'application/xhtml+xml', 'application/octet-stream'])('%s 不自动读取并创建内嵌预览', async mimeType => {
    vi.mocked(api.accessDocumentShareByCode).mockResolvedValue({ ...access, mimeType }); await setup();
    expect(api.loadDocumentShareContentByCode).not.toHaveBeenCalled(); expect(URL.createObjectURL).not.toHaveBeenCalled(); expect(wrapper!.find('iframe').exists()).toBe(false);
    await wrapper!.get('[data-testid="document-public-share-download"]').trigger('click'); await flushPromises(); expect(openDocumentBlob).toHaveBeenCalledOnce();
  });
  it.each(['cancelled', 'unknown'])('Office 任务 %s 终止轮询，不读取输出', async statusKey => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    vi.mocked(api.accessDocumentShareByCode).mockResolvedValue({ ...access, mimeType: 'application/msword' });
    vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValue({ ...documentPreviewTask, statusKey }); await setup();
    await vi.advanceTimersByTimeAsync(60_000); await flushPromises(); expect(api.getDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce(); expect(api.loadDocumentSharePreviewTaskContentByCode).not.toHaveBeenCalled();
  });
  it('KeepAlive 停用取消旧内容，恢复重新访问而不接入迟到 Blob', async () => {
    const pending = deferred<Blob>(); vi.mocked(api.loadDocumentShareContentByCode).mockReturnValueOnce(pending.promise); await setup(true);
    const signal = vi.mocked(api.loadDocumentShareContentByCode).mock.calls[0]![2]!;
    visible.value = false; await flushPromises(); expect(signal.aborted).toBe(true);
    visible.value = true; await flushPromises(); pending.resolve(new Blob(['private'])); await flushPromises();
    expect(api.accessDocumentShareByCode).toHaveBeenCalledTimes(2); expect(URL.createObjectURL).toHaveBeenCalledOnce();
  });
  it('停用期间分享码变化只在恢复后读取最新分享', async () => {
    await setup(true); visible.value = false; await flushPromises(); await router.push('/share/NEW'); await flushPromises();
    expect(api.accessDocumentShareByCode).toHaveBeenCalledOnce(); visible.value = true; await flushPromises();
    expect(api.accessDocumentShareByCode).toHaveBeenLastCalledWith('NEW', { password: null }, expect.any(AbortSignal));
  });
  it('恢复受密码保护的分享时不自动重放旧密码', async () => {
    vi.mocked(api.accessDocumentShareByCode).mockRejectedValueOnce({ status: 400, code: 'document.host_share.password_required', title: 'Password required' })
      .mockResolvedValueOnce({ ...access, hasPassword: true }).mockRejectedValueOnce({ status: 400, code: 'document.host_share.password_required', title: 'Password required' });
    await setup(true); await wrapper!.get('[data-testid="document-public-share-password"]').setValue('old-secret');
    await wrapper!.get('[data-testid="document-public-share-submit"]').trigger('click'); await flushPromises();
    visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(api.accessDocumentShareByCode).toHaveBeenLastCalledWith('OLD', { password: null }, expect.any(AbortSignal));
    expect(wrapper!.get('[data-testid="document-public-share-password"]').element).toHaveProperty('value', '');
  });
});
