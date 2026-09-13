import { effectScope } from 'vue';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useBlobPreview } from './useBlobPreview';

afterEach(() => vi.restoreAllMocks());

describe('Blob 预览所有权', () => {
  it('替换及销毁作用域释放各自 URL', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValueOnce('blob:one').mockReturnValueOnce('blob:two');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const scope = effectScope();
    const preview = scope.run(useBlobPreview)!;
    await preview.load(async () => new Blob(['one']));
    await preview.load(async () => new Blob(['two']));
    expect(revoke).toHaveBeenCalledWith('blob:one');
    scope.stop();
    expect(revoke).toHaveBeenCalledWith('blob:two');
    expect(preview.url.value).toBeNull();
  });

  it('旧下载晚于新下载返回时不分配 URL', async () => {
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:new');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const scope = effectScope();
    const preview = scope.run(useBlobPreview)!;
    const old = Promise.withResolvers<Blob>();
    const pending = preview.load(() => old.promise);
    await preview.load(async () => new Blob(['new']));
    old.resolve(new Blob(['old']));
    expect(await pending).toBe(false);
    expect(create).toHaveBeenCalledOnce();
    expect(preview.url.value).toBe('blob:new');
    scope.stop();
  });

  it('关闭和卸载均使进行中的下载失效', async () => {
    const create = vi.spyOn(URL, 'createObjectURL');
    for (const unmount of [false, true]) {
      const scope = effectScope();
      const preview = scope.run(useBlobPreview)!;
      const response = Promise.withResolvers<Blob>();
      const pending = preview.load(() => response.promise);
      if (unmount) scope.stop(); else preview.clear();
      response.resolve(new Blob(['late']));
      expect(await pending).toBe(false);
      scope.stop();
    }
    expect(create).not.toHaveBeenCalled();
  });

  it('旧下载失败不能移除新预览', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:new');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const scope = effectScope();
    const preview = scope.run(useBlobPreview)!;
    const old = Promise.withResolvers<Blob>();
    const pending = preview.load(() => old.promise);
    await preview.load(async () => new Blob(['new']));
    old.reject(new Error('old failed'));
    expect(await pending).toBe(false);
    expect(preview.url.value).toBe('blob:new');
    scope.stop();
  });
});
