import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadKnowledgePdf } from './knowledge-pdf';
const mock = vi.hoisted(() => ({ getDocument: vi.fn(), pdfDestroy: vi.fn() }));
vi.mock('pdfjs-dist', () => ({ PDFWorker: class { destroy = mock.pdfDestroy; }, getDocument: mock.getDocument }));
afterEach(() => { vi.unstubAllGlobals(); vi.resetAllMocks(); });
describe('本地 PDF Worker 与资源边界', () => {
  it('只传文件字节，禁止 XFA/外部资源策略，撤权立即终止 Worker', async () => {
    const terminate = vi.fn(); const destroy = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('Worker', class { terminate = terminate; });
    mock.getDocument.mockReturnValue({ promise: Promise.resolve({}), destroy });
    const controller = new AbortController(); const bytes = new Uint8Array([1]);
    const task = await loadKnowledgePdf(bytes, controller.signal);
    const options = mock.getDocument.mock.calls[0]![0];
    expect(options).toMatchObject({ data: bytes, enableXfa: false, useWorkerFetch: false, useWasm: false, stopAtErrors: true, maxImageSize: 4000000 });
    expect(options.url).toBeUndefined();
    const factory = new options.BinaryDataFactory();
    await expect(factory.fetch({ kind: 'cMapUrl', filename: 'https://external.example/font' })).rejects.toThrow();
    await expect(factory.fetch({ kind: 'cMapUrl', filename: '../standard_fonts/FoxitSerif.pfb' })).rejects.toThrow();
    controller.abort(); task.destroy(); expect(terminate).toHaveBeenCalledTimes(1); expect(destroy).toHaveBeenCalledTimes(1);
  });
  it('延迟加载前已取消不创建 Worker 或解析器', async () => {
    const Worker = vi.fn(); vi.stubGlobal('Worker', Worker); const controller = new AbortController(); controller.abort();
    await expect(loadKnowledgePdf(new Uint8Array([1]), controller.signal)).rejects.toThrow(); expect(Worker).not.toHaveBeenCalled(); expect(mock.getDocument).not.toHaveBeenCalled();
  });
});
