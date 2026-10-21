import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { reactive } from 'vue';
import DocumentPublicShareView from './DocumentPublicShareView.vue';
import {
  accessDocumentShareByCode,
  createDocumentSharePreviewTaskByCode,
  getDocumentSharePreviewTaskByCode,
  loadDocumentShareContentByCode,
  loadDocumentSharePreviewTaskContentByCode
} from '../api/document-shares';

const routing = vi.hoisted(() => ({ route: undefined as { params: { shareCode: string } } | undefined }));
vi.mock('vue-router', () => ({ useRoute: () => routing.route }));
vi.mock('../api/document-shares', () => ({
  accessDocumentShareByCode: vi.fn(),
  createDocumentSharePreviewTaskByCode: vi.fn(),
  getDocumentSharePreviewTaskByCode: vi.fn(),
  loadDocumentShareContentByCode: vi.fn(),
  loadDocumentSharePreviewTaskContentByCode: vi.fn()
}));
vi.mock('../api/host-document-items', () => ({ openDocumentBlob: vi.fn() }));

const accessMock = vi.mocked(accessDocumentShareByCode);
const createMock = vi.mocked(createDocumentSharePreviewTaskByCode);
const pollMock = vi.mocked(getDocumentSharePreviewTaskByCode);
const contentMock = vi.mocked(loadDocumentSharePreviewTaskContentByCode);
const directMock = vi.mocked(loadDocumentShareContentByCode);
const officeAccess = {
  title: 'Office 文件', fileName: 'preview.docx', accessCountRemaining: 3,
  mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  previewGrant: 'access-only-grant'
};
let wrapper: VueWrapper | undefined;

function render() {
  wrapper = mount(DocumentPublicShareView);
  return wrapper;
}

describe('匿名 Office 预览凭据与生命周期', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] });
    vi.setSystemTime(new Date('2026-10-21T00:00:00Z'));
    routing.route = reactive({ params: { shareCode: 'alpha' } });
    accessMock.mockReset().mockResolvedValue(officeAccess as never);
    createMock.mockReset().mockResolvedValue({
      id: 'task-alpha', previewGrant: 'task-only-grant',
      previewPollUntilUtc: new Date(Date.now() + 60_000).toISOString()
    } as never);
    pollMock.mockReset().mockResolvedValue({ statusKey: 'succeeded' } as never);
    contentMock.mockReset().mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' }));
    directMock.mockReset().mockResolvedValue(new Blob(['new'], { type: 'application/pdf' }));
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:preview');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  });

  afterEach(() => {
    wrapper?.unmount(); wrapper = undefined;
    vi.useRealTimers(); vi.restoreAllMocks();
  });

  it('创建只用访问凭据，429 后按 Retry-After 退避，轮询与内容只用任务凭据', async () => {
    pollMock.mockRejectedValueOnce({ status: 429, code: 'document.preview.rate_limited', retryAfterSeconds: 2 });
    const page = render();
    await flushPromises();
    expect(createMock).toHaveBeenCalledExactlyOnceWith('alpha',
      { password: null, previewGrant: 'access-only-grant' }, expect.any(AbortSignal));
    expect(pollMock).toHaveBeenCalledExactlyOnceWith('alpha', 'task-alpha',
      { previewGrant: 'task-only-grant' }, expect.any(AbortSignal));
    await vi.advanceTimersByTimeAsync(1999);
    expect(pollMock).toHaveBeenCalledTimes(1);
    expect(contentMock).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    await flushPromises();
    expect(pollMock).toHaveBeenCalledTimes(2);
    expect(pollMock).toHaveBeenLastCalledWith('alpha', 'task-alpha',
      { previewGrant: 'task-only-grant' }, expect.any(AbortSignal));
    expect(contentMock).toHaveBeenCalledExactlyOnceWith('alpha', 'task-alpha',
      { previewGrant: 'task-only-grant' }, expect.any(AbortSignal));
    expect(directMock).not.toHaveBeenCalled();
    expect(accessMock).toHaveBeenCalledTimes(1);
    expect(page.get('iframe').attributes('src')).toBe('blob:preview');
  });

  it('轮询响应不能延长创建时的固定截止，也不能替换任务凭据', async () => {
    createMock.mockResolvedValueOnce({ id: 'task-alpha', previewGrant: 'task-only-grant',
      previewPollUntilUtc: new Date(Date.now() + 3000).toISOString() } as never);
    pollMock.mockResolvedValue({ statusKey: 'queued', previewGrant: 'replacement-grant',
      previewPollUntilUtc: new Date(Date.now() + 86400_000).toISOString() } as never);
    render(); await flushPromises();
    await vi.advanceTimersByTimeAsync(3000); await flushPromises();
    expect(pollMock).toHaveBeenCalledTimes(1);
    expect(createMock).toHaveBeenCalledTimes(1);
    expect(accessMock).toHaveBeenCalledTimes(1);
    expect(contentMock).not.toHaveBeenCalled();
    expect(wrapper!.text()).toContain('document.public_share.preview_failed');
  });

  it('换路由会取消旧轮询，晚到的旧任务不能覆盖新预览', async () => {
    const old = Promise.withResolvers<Awaited<ReturnType<typeof getDocumentSharePreviewTaskByCode>>>();
    pollMock.mockReturnValueOnce(old.promise);
    const page = render(); await flushPromises();
    const oldSignal = pollMock.mock.calls[0]![3]!;
    accessMock.mockResolvedValueOnce({ ...officeAccess, title: '新分享', mimeType: 'application/pdf' } as never);
    routing.route!.params.shareCode = 'beta'; await flushPromises();
    expect(oldSignal.aborted).toBe(true);
    expect(directMock).toHaveBeenCalledWith('beta', { password: null }, expect.any(AbortSignal));
    old.resolve({ statusKey: 'succeeded' } as never); await flushPromises();
    expect(contentMock).not.toHaveBeenCalled();
    expect(page.text()).toContain('新分享');
    expect(page.get('iframe').attributes('src')).toBe('blob:preview');
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith('blob:preview');
  });

  it('卸载会取消退避中的请求和定时器，不再轮询', async () => {
    pollMock.mockRejectedValue({ status: 429, code: 'document.preview.rate_limited', retryAfterSeconds: 10 });
    const page = render(); await flushPromises();
    const signal = pollMock.mock.calls[0]![3]!;
    page.unmount(); wrapper = undefined;
    expect(signal.aborted).toBe(true);
    await vi.advanceTimersByTimeAsync(60_000); await flushPromises();
    expect(pollMock).toHaveBeenCalledTimes(1);
    expect(contentMock).not.toHaveBeenCalled();
  });
});
