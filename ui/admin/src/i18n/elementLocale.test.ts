import { describe, expect, it, vi } from 'vitest';
import { createElementLocaleController } from './elementLocale';

describe('Element Plus 与 Day.js 语言桥', () => {
  it('切换语言时同步组件语言与 Day.js', async () => {
    const setDayjsLocale = vi.fn();
    const controller = createElementLocaleController({
      loaders: {
        'zh-CN': vi.fn().mockResolvedValue({ default: { name: 'zh-cn' } }),
        'en-US': vi.fn().mockResolvedValue({ default: { name: 'en' } })
      },
      setDayjsLocale
    });

    await controller.setLocale('en-US');

    expect(controller.locale.value).toMatchObject({ name: 'en' });
    expect(setDayjsLocale).toHaveBeenCalledWith('en');
  });

  it('旧加载结果不得覆盖较新的语言选择', async () => {
    let resolveEnglish!: (value: { default: { name: string } }) => void;
    const english = new Promise<{ default: { name: string } }>(resolve => {
      resolveEnglish = resolve;
    });
    const controller = createElementLocaleController({
      loaders: {
        'zh-CN': vi.fn().mockResolvedValue({ default: { name: 'zh-cn' } }),
        'en-US': vi.fn().mockReturnValue(english)
      },
      setDayjsLocale: vi.fn()
    });

    const oldRequest = controller.setLocale('en-US');
    await controller.setLocale('zh-CN');
    resolveEnglish({ default: { name: 'en' } });
    await oldRequest;

    expect(controller.locale.value).toMatchObject({ name: 'zh-cn' });
  });

  it('动态加载失败时回退中文并通知共享语言状态', async () => {
    const fallback = vi.fn();
    const controller = createElementLocaleController({
      loaders: {
        'zh-CN': vi.fn().mockResolvedValue({ default: { name: 'zh-cn' } }),
        'en-US': vi.fn().mockRejectedValue(new Error('chunk unavailable'))
      },
      setDayjsLocale: vi.fn(),
      onFallback: fallback
    });

    await controller.setLocale('en-US');

    expect(controller.locale.value).toMatchObject({ name: 'zh-cn' });
    expect(fallback).toHaveBeenCalledWith('zh-CN');
  });

  it('目标语言与中文语言包都加载失败时不应中断页面', async () => {
    const fallback = vi.fn();
    const setDayjsLocale = vi.fn();
    const controller = createElementLocaleController({
      loaders: {
        'zh-CN': vi.fn().mockRejectedValue(new Error('fallback chunk unavailable')),
        'en-US': vi.fn().mockRejectedValue(new Error('target chunk unavailable'))
      },
      setDayjsLocale,
      onFallback: fallback
    });

    await expect(controller.setLocale('en-US')).resolves.toBeUndefined();

    expect(controller.locale.value).toBeUndefined();
    expect(setDayjsLocale).toHaveBeenCalledWith('zh-cn');
    expect(fallback).toHaveBeenCalledWith('zh-CN');
  });
});
