import type { SupportedLocale } from './locale.js';

/** DOCX 功能词条与主目录共用语言状态，按能力独立维护。 */
const docxMessages = {
  'zh-CN': {
    paragraph: '段落 {paragraph} · 字符位置 {offset} + {length}',
    table: '表 {table} · 行 {row} · 单元格 {cell} · 段落 {paragraph} · 字符位置 {offset} + {length}',
    fileInvalid: '请选择不超过 5 MiB 的 DOCX 文件。'
  },
  'en-US': {
    paragraph: 'Paragraph {paragraph} · Character offset {offset} + {length}',
    table: 'Table {table} · Row {row} · Cell {cell} · Paragraph {paragraph} · Character offset {offset} + {length}',
    fileInvalid: 'Select a DOCX file up to 5 MiB.'
  }
} as const;

/** 使用当前 BCP 47 语言与服务器逻辑坐标生成纯文本。
 * @param locale 当前后台语言。
 * @param key DOCX 功能词条。
 * @param parameters 服务器核对的数值坐标。
 * @returns 可直接展示的本地化纯文本。
 */
export function translateKnowledgeDocx(locale: SupportedLocale, key: keyof typeof docxMessages['zh-CN'], parameters: Record<string, number> = {}): string {
  return docxMessages[locale][key].replace(/\{([a-zA-Z]+)\}/g, (placeholder, name: string) => name in parameters ? String(parameters[name]) : placeholder);
}
