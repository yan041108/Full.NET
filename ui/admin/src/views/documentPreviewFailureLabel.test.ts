import { describe, expect, it } from 'vitest';
import { translate, type SupportedLocale } from '@fullnet/admin-i18n';
import { documentPreviewFailureLabel } from './documentPreviewFailureLabel';

describe('预览转换失败原因', () => {
  const cases = [
    ['document.office_preview.lease_expired', 'documentPreviewTasks.leaseExpired'],
    ['document.office_preview.output_too_large', 'documentPreviewTasks.outputTooLarge'],
    ['document.office_preview.input_too_large', 'documentPreviewTasks.inputTooLarge'],
    ['document.office_preview.conversion_failed', 'documentPreviewTasks.conversionFailed'],
    ['document.office_preview.provider_disabled', 'documentPreviewTasks.providerDisabled']
  ] as const;
  for (const locale of ['zh-CN', 'en-US'] satisfies SupportedLocale[]) {
    it.each(cases)(`${locale} 将 %s 显示为可操作的失败文案`, (code, key) => {
      expect(documentPreviewFailureLabel(code, key => translate(locale, key))).toBe(translate(locale, key));
    });
  }
  it.each([null, undefined, ''])('没有失败码时不显示错误原因：%s', code => {
    expect(documentPreviewFailureLabel(code, () => { throw new Error('不应翻译'); })).toBe('');
  });
  it.each(['unknown.preview.failure', 'constructor'])('未登记码使用稳定回退：%s', code => {
    expect(documentPreviewFailureLabel(code, key => translate('zh-CN', key))).toBe(translate('zh-CN', 'documentPreviewTasks.operationFailed'));
  });
});
