import { mount, flushPromises } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { enterpriseRequestsHttp } from '../../api/enterprise-requests';
import { createOutputSession, deferred, outputId } from '../../test/data-output-fixtures';
import { useAdminI18n } from '../../i18n/adminI18n';
import Dialog from './EnterpriseRequestAttachmentsDialog.vue';

vi.mock('../../api/enterprise-requests', async original => ({ ...await original<typeof import('../../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn(), requestBlob: vi.fn() } }));
const request = vi.mocked(enterpriseRequestsHttp.request); const blobRequest = vi.mocked(enterpriseRequestsHttp.requestBlob);
const attachmentId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const attachment = { id: attachmentId, fileId: outputId, originalFileName: '申请说明.txt', sizeBytes: '12', createdAtUtc: '2026-10-09T00:00:00Z' };
const response = { requestId: outputId, requestVersion: '2', requestStatus: 'Draft', items: [attachment] };
const read = 'enterprise_request.enterprise_requests.read', update = 'enterprise_request.enterprise_requests.update';
function fixture(permissions = [read, update]) {
  const { pinia, session } = createOutputSession(permissions);
  return { session, wrapper: mount(Dialog, { props: { requestId: outputId }, global: { plugins: [pinia], stubs: { teleport: true } } }) };
}
async function click(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find(value => value.text() === text);
  expect(button, text).toBeDefined(); await button!.trigger('click');
}
async function select(wrapper: ReturnType<typeof mount>, file = new File(['attachment'], 'probe.txt', { type: 'text/plain' })) {
  const input = wrapper.find('input[type=file]'); Object.defineProperty(input.element, 'files', { configurable: true, value: [file] });
  await input.trigger('change');
}
describe('申请附件的权限、版本与取消', () => {
  beforeEach(() => { useAdminI18n().setLocale('zh-CN'); request.mockReset(); blobRequest.mockReset(); request.mockResolvedValue(response); });
  it('读取精确文件名与字节数，不显示物理路径或公开 URL', async () => {
    const f = fixture(); try { await flushPromises(); expect(f.wrapper.text()).toContain('申请说明.txt'); expect(f.wrapper.text()).toContain('12 字节'); expect(f.wrapper.text()).toContain('单据版本: 2'); }
    finally { f.wrapper.unmount(); }
  });
  it('无 Read 不读取附件，仅 Read 不显示上传和移除', async () => {
    const f = fixture([update]); try { await flushPromises(); expect(request).not.toHaveBeenCalled(); } finally { f.wrapper.unmount(); }
    const g = fixture([read]); try { await flushPromises(); expect(g.wrapper.find('input[type=file]').exists()).toBe(false); expect(g.wrapper.text()).not.toContain('移除附件'); } finally { g.wrapper.unmount(); }
  });
  it('提交后保留下载，隐藏全部写入入口', async () => {
    request.mockResolvedValue({ ...response, requestStatus: 'Submitted' }); const f = fixture();
    try { await flushPromises(); expect(f.wrapper.find('input[type=file]').exists()).toBe(false); expect(f.wrapper.text()).toContain('下载'); }
    finally { f.wrapper.unmount(); }
  });
  it('上传生成 multipart 携带当前版本，成功清除选择并重新读取', async () => {
    const f = fixture(); try {
      await flushPromises(); await select(f.wrapper);
      request.mockResolvedValueOnce({ requestId: outputId, requestVersion: '3', attachment });
      await click(f.wrapper, '上传附件'); await flushPromises();
      const form = request.mock.calls[1]![1]!.body as FormData; expect(form.get('version')).toBe('2'); expect(form.get('file')).toBeInstanceOf(File);
      expect(f.wrapper.emitted('changed')).toHaveLength(1); expect(request).toHaveBeenCalledTimes(3);
    } finally { f.wrapper.unmount(); }
  });
  it('冲突保留选择，用户刷新获取新版本后再重试', async () => {
    const f = fixture(); try {
      await flushPromises(); await select(f.wrapper); request.mockRejectedValueOnce({ status: 409, code: 'enterprise_request.version_conflict', title: '冲突' });
      await click(f.wrapper, '上传附件'); await flushPromises(); expect(f.wrapper.text()).toContain('enterprise_request.version_conflict'); expect(f.wrapper.emitted('changed')).toBeUndefined();
      request.mockResolvedValueOnce({ ...response, requestVersion: '3' }); await click(f.wrapper, '刷新'); await flushPromises();
      request.mockResolvedValueOnce({ requestId: outputId, requestVersion: '4', attachment }); await click(f.wrapper, '上传附件'); await flushPromises();
      expect((request.mock.calls[3]![1]!.body as FormData).get('version')).toBe('3');
    } finally { f.wrapper.unmount(); }
  });
  it('超限及空文件在本地拒绝，不能发上传请求', async () => {
    const f = fixture(); try {
      await flushPromises(); const empty = new File([], 'empty.txt'); await select(f.wrapper, empty); expect(f.wrapper.text()).toContain('validation.failed');
      const large = new File(['a'], 'large.txt'); Object.defineProperty(large, 'size', { value: 10485761 }); await select(f.wrapper, large);
      await click(f.wrapper, '上传附件'); await flushPromises(); expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });
  it('移除先确认，取消不写入，确认携带主表版本', async () => {
    const f = fixture(); try {
      await flushPromises(); await click(f.wrapper, '移除附件'); expect(request).toHaveBeenCalledTimes(1);
      const buttons = f.wrapper.findAll('button').filter(value => value.text() === '取消'); await buttons[0]!.trigger('click'); expect(request).toHaveBeenCalledTimes(1);
      await click(f.wrapper, '移除附件'); request.mockResolvedValueOnce({ requestId: outputId, requestVersion: '3' }); await click(f.wrapper, '确认移除'); await flushPromises();
      expect(request.mock.calls[1]![1]!.method).toBe('DELETE'); expect(JSON.parse(request.mock.calls[1]![1]!.body as string)).toEqual({ version: 2 });
    } finally { f.wrapper.unmount(); }
  });
  it('关闭后晚到上传不能刷新或触发 changed', async () => {
    const f = fixture(); try {
      await flushPromises(); await select(f.wrapper); const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '上传附件'); await click(f.wrapper, '取消'); pending.resolve({ requestId: outputId, requestVersion: '3', attachment }); await flushPromises();
      expect(f.wrapper.emitted('changed')).toBeUndefined(); expect(request).toHaveBeenCalledTimes(2);
    } finally { f.wrapper.unmount(); }
  });
  it('撤权或切换申请清除旧资料，迟到下载不能创建对象 URL', async () => {
    const url = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:probe'); const f = fixture();
    try {
      await flushPromises(); const pending = deferred<Blob>(); blobRequest.mockReturnValueOnce(pending.promise); await click(f.wrapper, '下载');
      f.session.currentUser!.permissions = []; await flushPromises(); pending.resolve(new Blob(['probe'])); await flushPromises();
      expect(url).not.toHaveBeenCalled(); expect(f.wrapper.text()).not.toContain(attachment.originalFileName);
    } finally { f.wrapper.unmount(); url.mockRestore(); }
  });
  it('认证下载使用当前附件名，关闭弹窗立即撤销对象 URL', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const url = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:attachment');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const anchor = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      expect(this.download).toBe(attachment.originalFileName); expect(this.href).toBe('blob:attachment');
    });
    const f = fixture();
    try {
      await flushPromises(); blobRequest.mockResolvedValueOnce(new Blob(['probe']));
      await click(f.wrapper, '下载'); await flushPromises();
      expect(blobRequest.mock.calls[0]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}/attachments/${attachmentId}/content`);
      expect(anchor).toHaveBeenCalledOnce(); await click(f.wrapper, '取消');
      expect(revoke).toHaveBeenCalledWith('blob:attachment');
    } finally { f.wrapper.unmount(); url.mockRestore(); revoke.mockRestore(); anchor.mockRestore(); vi.useRealTimers(); }
  });
});
