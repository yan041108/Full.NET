import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { isPassiveContentType, openContentBlob } from './openContentBlob';
beforeEach(() => {
  vi.useFakeTimers(); vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:content');
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {}); vi.spyOn(window, 'open').mockReturnValue(null);
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
});
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); });
describe('文件内容打开策略', () => {
  it.each(['application/pdf', 'text/plain', 'image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/avif', 'image/bmp'])('被动类型 %s 保留独立预览', type => {
    const blob = new Blob(['content'], { type }); openContentBlob(blob);
    expect(URL.createObjectURL).toHaveBeenCalledWith(blob); expect(window.open).toHaveBeenCalledExactlyOnceWith('blob:content', '_blank', 'noopener,noreferrer');
    expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled();
  });
  it.each(['text/html', 'image/svg+xml', 'application/xhtml+xml', 'application/xml', 'text/xml', 'text/javascript', 'application/javascript', 'application/octet-stream', '', 'application/pdfx'])('主动或未知类型 %s 只提供下载', type => {
    openContentBlob(new Blob(['<script>content</script>'], { type }), 'sample.html');
    const blob = vi.mocked(URL.createObjectURL).mock.calls[0]![0] as Blob;
    expect(blob.type).toBe('application/octet-stream'); expect(window.open).not.toHaveBeenCalled(); expect(HTMLAnchorElement.prototype.click).toHaveBeenCalledOnce();
  });
  it('空窗口句柄不能触发立即回收，延迟到期仍回收一次', async () => {
    openContentBlob(new Blob(['pdf'], { type: 'application/pdf' })); expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(60_000); expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:content');
  });
  it('下载与预览均只持有有界 URL 生命周期', async () => {
    openContentBlob(new Blob(['html'], { type: 'text/html' })); await vi.advanceTimersByTimeAsync(60_000);
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:content');
  });
  it('下载名去除路径与控制字符', () => {
    let filename = ''; vi.mocked(HTMLAnchorElement.prototype.click).mockImplementation(function (this: HTMLAnchorElement) { filename = this.download; });
    openContentBlob(new Blob(['x']), '../folder/private\u0000.html'); expect(filename).toBe('private.html');
  });
  it.each(['open', 'download'])('%s 抛出时立即回收未移交 URL', kind => {
    if (kind === 'open') vi.mocked(window.open).mockImplementation(() => { throw new Error('open failed'); });
    else vi.mocked(HTMLAnchorElement.prototype.click).mockImplementation(() => { throw new Error('download failed'); });
    expect(() => openContentBlob(new Blob(['x'], { type: kind === 'open' ? 'application/pdf' : 'text/html' }))).toThrow();
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:content');
  });
  it('媒体参数可规范化，类型前缀不能扩大白名单', () => {
    expect(isPassiveContentType(' Text/Plain; charset=utf-8 ')).toBe(true); expect(isPassiveContentType('text/plain-active')).toBe(false);
  });
  it.each([
    ['application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'document.docx'],
    ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'document.xlsx'],
    ['application/zip', 'document.zip'], ['application/unknown', 'document.bin'], ['constructor', 'document.bin'], ['__proto__', 'document.bin']
  ])('无原文件名时保留 %s 的已知格式提示', (type, expected) => {
    let name = ''; vi.mocked(HTMLAnchorElement.prototype.click).mockImplementation(function (this: HTMLAnchorElement) { name = this.download; });
    openContentBlob(new Blob(['file'], { type })); expect(name).toBe(expected);
  });
});
