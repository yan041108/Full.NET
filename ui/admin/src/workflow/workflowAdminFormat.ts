import type { SupportedLocale } from '@fullnet/admin-i18n';

/** 工作流管理页统一格式化 UTC 时间，解析失败时回退原字符串。 */
export function formatAdminDateTime(
  locale: SupportedLocale,
  value: string | null | undefined
): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString(locale, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  });
}
