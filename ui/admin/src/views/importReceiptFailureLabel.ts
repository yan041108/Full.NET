import type { MessageKey } from '@fullnet/admin-i18n';

/** 将确定性回执超限显示为已登记文案，其余稳定机器码保留既有展示行为。 */
export function importReceiptFailureLabel(code: string | null | undefined, translate: (key: MessageKey) => string): string {
  return code === 'import_export.error_receipt.limit_exceeded' ? translate('importExportTasks.receiptLimitExceeded') : code ?? '-';
}
