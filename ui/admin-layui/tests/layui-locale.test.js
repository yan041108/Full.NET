import { describe, expect, it, vi } from 'vitest';
import { applyLayuiLocale } from '../js/core/layui-locale.js';

describe('Layui 公开组件语言桥', () => {
  it('只通过公开 i18n.set 配置中英文组件消息', () => {
    const set = vi.fn();
    const layui = { i18n: { set } };

    expect(applyLayuiLocale(layui, 'en-US')).toBe(true);

    expect(set).toHaveBeenCalledWith(expect.objectContaining({
      locale: 'en',
      messages: expect.objectContaining({
        en: expect.objectContaining({
          table: expect.any(Object),
          laypage: expect.any(Object),
          laydate: expect.any(Object),
          layer: expect.any(Object),
          form: expect.any(Object),
          upload: expect.any(Object)
        })
      })
    }));
    expect(layui.i18n.$t).toBeUndefined();
  });

  it('Layui 全局缺失时保持渐进增强主流程', () => {
    expect(applyLayuiLocale(undefined, 'zh-CN')).toBe(false);
  });
});
