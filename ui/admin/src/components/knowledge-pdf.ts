import type { PDFDocumentLoadingTask } from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';

export type KnowledgePdfTask = Pick<PDFDocumentLoadingTask, 'promise'> & { destroy(): void };

export async function loadKnowledgePdf(data: Uint8Array, signal: AbortSignal): Promise<KnowledgePdfTask> {
  const pdf = await import('pdfjs-dist');
  if (signal.aborted) throw new Error('client.pdf_cancelled');
  // 禁止回退为主线程解析；本地独立 Worker 可在解析超时或撤权时立即终止。
  const port = new Worker(workerUrl, { type: 'module' });
  // 6.4.299 的声明把实际支持的 Worker port 推断为 null；局部修正构造签名，不放宽其余 PDF API。
  const PdfWorker = pdf.PDFWorker as unknown as new (options: { port: Worker }) => InstanceType<typeof pdf.PDFWorker>;
  const worker = new PdfWorker({ port });
  const assets = import.meta.glob<string>('../../node_modules/pdfjs-dist/{cmaps/*.bcmap,standard_fonts/*.{pfb,ttf}}', { eager: true, query: '?url', import: 'default' });
  class LocalBinaryDataFactory {
    async fetch({ kind, filename }: { kind: string; filename: string }) {
      // PDF 只能按完整文件名命中随包发布的字体/CMap，不拼接不可信路径或外部地址。
      const folder = kind === 'cMapUrl' ? 'cmaps' : kind === 'standardFontDataUrl' ? 'standard_fonts' : undefined;
      const url = folder && assets[`../../node_modules/pdfjs-dist/${folder}/${filename}`];
      if (!url) throw new Error('client.pdf_asset_unavailable');
      const result = await fetch(url, { signal, credentials: 'omit', redirect: 'error' });
      if (!result.ok) throw new Error('client.pdf_asset_unavailable');
      return new Uint8Array(await result.arrayBuffer());
    }
  }
  let task: PDFDocumentLoadingTask;
  try {
    task = pdf.getDocument({ data, worker, BinaryDataFactory: LocalBinaryDataFactory, useWorkerFetch: false,
      useWasm: false, enableXfa: false, stopAtErrors: true, disableRange: true, disableStream: true,
      disableAutoFetch: true, useSystemFonts: false, maxImageSize: 4_000_000, canvasMaxAreaInBytes: 16_000_000,
      isOffscreenCanvasSupported: false, isImageDecoderSupported: false });
  } catch (error) { worker.destroy(); port.terminate(); throw error; }
  let destroyed = false;
  const destroy = () => {
    if (destroyed) return; destroyed = true; signal.removeEventListener('abort', destroy);
    void task.destroy().catch(() => {}); worker.destroy(); port.terminate();
  };
  signal.addEventListener('abort', destroy, { once: true });
  if (signal.aborted) destroy();
  return { promise: task.promise, destroy };
}
