import { afterEach, describe, expect, it, vi } from 'vitest';
import { chooseWorkflowAttachment, deliverWorkflowAttachment } from '../src/features/workflow/workflow-file-platform';

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('移动附件平台交付', () => {
  it('H5 采集保留本地路径，小程序独立使用消息文件选择器', async () => {
    vi.stubGlobal('uni', { chooseFile: vi.fn().mockResolvedValue({ tempFilePaths: ['/local/file'] }) });
    expect(await chooseWorkflowAttachment()).toBe('/local/file');
    const chooseMessageFile = vi.fn(options => options.success({ tempFiles: [{ path: '/message/file' }] }));
    vi.stubGlobal('uni', { chooseMessageFile });
    expect(await chooseWorkflowAttachment()).toBe('/message/file');
    expect(chooseMessageFile).toHaveBeenCalledWith(expect.objectContaining({ count: 1, type: 'all' }));
  });
  it('浏览器只用下载链接交付八位内容，清理链接并回收 URL', async () => {
    vi.useFakeTimers();
    const link = { href: '', download: '', click: vi.fn(), remove: vi.fn() };
    const createObjectURL = vi.fn().mockReturnValue('blob:owned'); const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    vi.stubGlobal('document', { createElement: vi.fn().mockReturnValue(link), body: { append: vi.fn() } });
    const bytes = new Uint8Array([1, 2, 3]).buffer;
    await deliverWorkflowAttachment(bytes, '../foreign.html');
    expect(link.download).toBe('.._foreign.html'); expect(link.click).toHaveBeenCalledOnce(); expect(link.remove).toHaveBeenCalledOnce();
    expect(createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    await vi.runAllTimersAsync(); expect(revokeObjectURL).toHaveBeenCalledWith('blob:owned');
  });
  it('系统文档查看失败也删除临时内容，拒绝主动内容和无平台目录', async () => {
    const writeFile = vi.fn(options => options.success()); const unlink = vi.fn(options => options.complete());
    const openDocument = vi.fn().mockRejectedValue(new Error('viewer failed'));
    vi.stubGlobal('uni', { getFileSystemManager: () => ({ writeFile, unlink }), openDocument });
    vi.stubGlobal('wx', { env: { USER_DATA_PATH: '/private' } });
    await expect(deliverWorkflowAttachment(new ArrayBuffer(3), 'proof.pdf')).rejects.toThrow('viewer failed');
    expect(writeFile).toHaveBeenCalledWith(expect.objectContaining({ filePath: '/private/fullnet-workflow-attachment.pdf' }));
    expect(unlink).toHaveBeenCalledOnce();
    await expect(deliverWorkflowAttachment(new ArrayBuffer(3), 'foreign.html')).rejects.toThrow('platform_unavailable');
    expect(writeFile).toHaveBeenCalledOnce();
    writeFile.mockImplementation(options => options.fail(new Error('write failed')));
    await expect(deliverWorkflowAttachment(new ArrayBuffer(3), 'proof.pdf')).rejects.toThrow('write failed');
    expect(unlink).toHaveBeenCalledTimes(2);
  });
});
