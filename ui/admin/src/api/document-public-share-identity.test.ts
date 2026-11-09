import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { accessDocumentShareByCode, createDocumentSharePreviewTaskByCode, getDocumentSharePreviewTaskByCode, loadDocumentShareContentByCode, loadDocumentSharePreviewTaskContentByCode } from './document-shares';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const access = { shareId: outputId, documentId: outputId, shareCode: 'CODE', title: 'Document', fileName: 'file.docx', mimeType: 'application/msword', fileSizeBytes: 12, hasPassword: false, accessCountRemaining: 9 };
beforeEach(() => vi.resetAllMocks());
describe('公开分享身份和请求凭据边界', () => {
  it.each([{ shareCode: 'OTHER' }, { shareId: '00000000-0000-0000-0000-000000000000' }, { documentId: '00000000-0000-0000-0000-000000000000' }, { fileSizeBytes: -1 }, { accessCountRemaining: -1 }])('拒绝错误分享或无效元数据 %j', async change => {
    vi.mocked(http.request).mockResolvedValue({ ...access, ...change }); await expect(accessDocumentShareByCode('CODE')).rejects.toThrow(/^client.invalid_document_share_access$/u);
  });
  it('空分享码在HTTP前拒绝', async () => {
    await expect(accessDocumentShareByCode(' ')).rejects.toThrow(/^client.invalid_public_document_share_code$/u); expect(http.request).not.toHaveBeenCalled();
  });
  it('请求复制口令且匿名选项保持，不借用调用方迟改', async () => {
    const pending = deferred<unknown>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise);
    const body = { password: 'original', requestKey: outputId }, reading = accessDocumentShareByCode('CODE', body);
    body.password = 'changed'; pending.resolve(access); await expect(reading).resolves.toEqual(access);
    expect(JSON.parse(vi.mocked(http.request).mock.calls[0]![1]!.body as string)).toEqual({ password: 'original', requestKey: outputId });
    expect(vi.mocked(http.request).mock.calls[0]![3]).toEqual({ retryUnauthorized: false, skipAuthentication: true });
  });
  it.each([{ id: '00000000-0000-0000-0000-000000000000' }, { sourceFileId: '00000000-0000-0000-0000-000000000000' }, { version: 0 }, { versionId: null }, { outputAvailabilityKey: 'released' }])('公开创建拒绝无效的固定任务身份或内部文件状态 %j', async change => {
    vi.mocked(http.request).mockResolvedValue({ ...task, versionId: outputId, ...change }); await expect(createDocumentSharePreviewTaskByCode('CODE')).rejects.toThrow(/^client.invalid_document_preview_task_identity$/u);
  });
  it('使用匿名凭据提交并轮询固定版本，只携带本次操作字段', async () => {
    const fixed = { ...task, versionId: outputId };
    const credential = { previewGrant: 'fixture-share-grant' };
    vi.mocked(http.request).mockResolvedValue(fixed);
    vi.mocked(http.requestBlob).mockResolvedValue(new Blob());
    await expect(createDocumentSharePreviewTaskByCode('CODE', credential)).resolves.toEqual(fixed);
    await expect(getDocumentSharePreviewTaskByCode('CODE', fixed.id, credential)).resolves.toEqual(fixed);
    await loadDocumentSharePreviewTaskContentByCode('CODE', fixed.id, credential);
    const download = { ...credential, requestKey: outputId, accessTypeKey: 'download' as const, ignored: 'other' };
    await loadDocumentShareContentByCode('CODE', download);
    for (const [, request] of vi.mocked(http.request).mock.calls)
      expect(JSON.parse(request!.body as string)).toEqual(credential);
    expect(JSON.parse(vi.mocked(http.requestBlob).mock.calls[0]![1]!.body as string)).toEqual(credential);
    expect(JSON.parse(vi.mocked(http.requestBlob).mock.calls[1]![1]!.body as string)).toEqual({ ...credential, requestKey: outputId, accessTypeKey: 'download' });
  });
  it('任务读取和内容下载在HTTP前拒绝空任务ID', async () => {
    const nil = '00000000-0000-0000-0000-000000000000'; await expect(getDocumentSharePreviewTaskByCode('CODE', nil)).rejects.toThrow(/^client.invalid_document_preview_task_identity$/u); await expect(loadDocumentSharePreviewTaskContentByCode('CODE', nil)).rejects.toThrow(/^client.invalid_document_preview_task_identity$/u); expect(http.request).not.toHaveBeenCalled(); expect(http.requestBlob).not.toHaveBeenCalled();
  });
});
