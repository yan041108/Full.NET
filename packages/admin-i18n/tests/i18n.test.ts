import { describe, expect, it } from 'vitest';
import {
  applyDocumentLocale,
  localeStorageKey,
  messageKeys,
  messages,
  resolveLocale,
  translate
} from '../src/index.js';

describe('管理端国际化契约', () => {
  it('两种语言公开完全相同的消息键', () => {
    expect(Object.keys(messages['zh-CN']).sort())
      .toEqual([...messageKeys].sort());
    expect(Object.keys(messages['en-US']).sort())
      .toEqual([...messageKeys].sort());
  });

  it.each([
    ['en-US', ['zh-CN'], 'en-US'],
    [undefined, ['en-GB'], 'en-US'],
    [undefined, ['zh-Hans-CN'], 'zh-CN'],
    ['invalid', ['fr-FR'], 'zh-CN']
  ])('按保存值和浏览器语言解析 %s / %s', (saved, preferred, expected) => {
    expect(resolveLocale(saved, preferred)).toBe(expected);
  });

  it('使用命名参数生成纯文本', () => {
    expect(translate('en-US', 'tenant.activeCount', { count: 3 }))
      .toBe('3 active scopes');
  });

  it('缺少命名参数时保留占位符以暴露字典错误', () => {
    expect(translate('zh-CN', 'tenant.activeCount'))
      .toBe('{count} 个活动范围');
  });

  it('更新文档语言和标题', () => {
    const target = {
      documentElement: { lang: '' },
      title: ''
    } as unknown as Document;

    applyDocumentLocale(target, 'en-US', 'Overview · Full.NET');

    expect(target.documentElement.lang).toBe('en-US');
    expect(target.title).toBe('Overview · Full.NET');
    expect(localeStorageKey).toBe('fullnet.admin.locale');
  });
});
