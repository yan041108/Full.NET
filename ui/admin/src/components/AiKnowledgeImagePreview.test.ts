import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AiKnowledgeSourceLocation } from '@fullnet/client-contracts';
import AiKnowledgeImagePreview from './AiKnowledgeImagePreview.vue';

const location: AiKnowledgeSourceLocation = { blockSequence: 0, kind: 'image_region', pageNumber: null, paragraphNumber: null,
  tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null,
  image: { frameNumber: 1, imageWidth: 2000, imageHeight: 1000, x: 100, y: 200, width: 300, height: 100,
    confidence: 0.95, tableNumber: null, rowNumber: null, columnNumber: null, parent: null } };
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });
describe('图片原生位置预览', () => {
  it('使用实际方向及规范尺寸，在白底画布按同比例绘制来源框并释放位图', async () => {
    const bitmap = { width: 2000, height: 1000, close: vi.fn() };
    const decode = vi.fn().mockResolvedValue(bitmap); vi.stubGlobal('createImageBitmap', decode);
    const context = { fillStyle: '', fillRect: vi.fn(), drawImage: vi.fn(), strokeStyle: '', lineWidth: 0, strokeRect: vi.fn() };
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D);
    const source = new Blob(['image']); const wrapper = mount(AiKnowledgeImagePreview, { props: { source, location } });
    try {
      await flushPromises(); expect(decode).toHaveBeenCalledWith(source, { imageOrientation: 'from-image', colorSpaceConversion: 'default' });
      expect(context.fillStyle).toBe('white'); expect(context.fillRect).toHaveBeenCalledWith(0, 0, 1600, 800);
      expect(context.strokeRect).toHaveBeenCalledWith(80, 160, 240, 80); expect(bitmap.close).toHaveBeenCalledOnce();
    } finally { wrapper.unmount(); }
  });
  it('方向或尺寸不匹配时提示安全下载，不绘制错误坐标', async () => {
    const bitmap = { width: 1000, height: 2000, close: vi.fn() }; vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue(bitmap));
    const draw = vi.spyOn(HTMLCanvasElement.prototype, 'getContext');
    const wrapper = mount(AiKnowledgeImagePreview, { props: { source: new Blob(), location } });
    try { await flushPromises(); expect(wrapper.find('[data-testid="knowledge-image-unavailable"]').exists()).toBe(true); expect(draw).not.toHaveBeenCalled(); expect(bitmap.close).toHaveBeenCalledOnce(); }
    finally { wrapper.unmount(); }
  });
  it('关闭期间解码完成也释放位图且不回填画布', async () => {
    let complete!: (bitmap: ImageBitmap) => void; vi.stubGlobal('createImageBitmap', vi.fn(() => new Promise(resolve => { complete = resolve; })));
    const draw = vi.spyOn(HTMLCanvasElement.prototype, 'getContext');
    const wrapper = mount(AiKnowledgeImagePreview, { props: { source: new Blob(), location } }); wrapper.unmount();
    const bitmap = { width: 2000, height: 1000, close: vi.fn() }; complete(bitmap as unknown as ImageBitmap); await flushPromises();
    expect(draw).not.toHaveBeenCalled(); expect(bitmap.close).toHaveBeenCalledOnce();
  });
  it('后续帧使用安全下载，不以首帧伪装定位', async () => {
    const decode = vi.fn(); vi.stubGlobal('createImageBitmap', decode);
    const wrapper = mount(AiKnowledgeImagePreview, { props: { source: new Blob(), location: { ...location, image: { ...location.image!, frameNumber: 2 } } } });
    try { await flushPromises(); expect(decode).not.toHaveBeenCalled(); expect(wrapper.find('canvas').exists()).toBe(false); }
    finally { wrapper.unmount(); }
  });
});
