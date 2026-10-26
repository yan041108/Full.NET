/** HTML 展示内容在 SFC 中必须是字符串表达式，并单独转义 HTML 属性边界。 */
export function htmlContentBinding(value) {
  return JSON.stringify(String(value ?? ''))
    .replaceAll('&', '&amp;').replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}
