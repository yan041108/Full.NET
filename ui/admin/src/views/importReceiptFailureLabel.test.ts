import { describe, expect, it } from 'vitest';
import { translate, type SupportedLocale } from '@fullnet/admin-i18n';
import { importReceiptFailureLabel } from './importReceiptFailureLabel';

describe('导入回执超限提示', () => {
  it.each(['zh-CN', 'en-US'] satisfies SupportedLocale[])('%s 显示回执超限操作提示', locale => {
    expect(importReceiptFailureLabel('import_export.error_receipt.limit_exceeded', key => translate(locale, key)))
      .toBe(translate(locale, 'importExportTasks.receiptLimitExceeded'));
  });
  it.each([null, undefined])('没有机器码保留空占位：%s', code => {
    expect(importReceiptFailureLabel(code, () => { throw new Error('不应翻译'); })).toBe('-');
  });
  it('其他错误码不进入动态词条选择', () => {
    expect(importReceiptFailureLabel('unknown.failure', () => { throw new Error('不应翻译'); })).toBe('unknown.failure');
  });
});
