import type { MessageKey } from '@fullnet/admin-i18n';

const failureMessages = new Map<string, MessageKey>([
  ['document.office_preview.lease_expired', 'documentPreviewTasks.leaseExpired'],
  ['document.office_preview.output_too_large', 'documentPreviewTasks.outputTooLarge'],
  ['document.office_preview.input_too_large', 'documentPreviewTasks.inputTooLarge'],
  ['document.office_preview.conversion_failed', 'documentPreviewTasks.conversionFailed'],
  ['document.office_preview.provider_disabled', 'documentPreviewTasks.providerDisabled']
]);

/** 稳定失败码只选择已登记文案，不将供应商文本或动态词条交给页面翻译器。 */
export function documentPreviewFailureLabel(code: string | null | undefined, translate: (key: MessageKey) => string): string {
  return code ? translate(failureMessages.get(code) ?? 'documentPreviewTasks.operationFailed') : '';
}
