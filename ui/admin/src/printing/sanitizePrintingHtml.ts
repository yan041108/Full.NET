import DOMPurify from 'dompurify';

/** 编辑布局与历史模板均是不可信 HTML；净化必须发生在最终 DOM 插入之前。 */
export function sanitizePrintingHtml(html: string): string {
  return DOMPurify.sanitize(html, {
    USE_PROFILES: {html:true},
    FORBID_TAGS: ['style','form','input','button','textarea','select','option'],
    FORBID_ATTR: ['id','name'],
    ALLOW_DATA_ATTR: false
  });
}
