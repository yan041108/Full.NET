import { describe, expect, it, vi } from 'vitest';
import { translateRuntimeMessage } from './runtimeMessage';

describe('动态词条边界', () => {
  it('只翻译目录已登记键，未知键不传给严格翻译器', () => {
    const translate = vi.fn(() => '已翻译');
    expect(translateRuntimeMessage(translate, 'locale.label')).toBe('已翻译');
    expect(translateRuntimeMessage(translate, 'unknown.provider.status')).toBe('unknown.provider.status');
    expect(translate).toHaveBeenCalledTimes(1);
  });
});
