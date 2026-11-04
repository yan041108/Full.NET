import type { PrintingFormSchemaDefinition } from '@fullnet/client-contracts';

/** 建议版式只包含转义后的目录标签和固定占位符，不能把目录元数据当作可执行 HTML。 */
export function suggestedPrintingSchemaLayout(schema: PrintingFormSchemaDefinition): string {
  if(!Array.isArray(schema.fields)||schema.fields.length>64||schema.fields.some(field=>!/^[a-zA-Z0-9_]{1,64}$/u.test(field.fieldKey)))throw Error('client.invalid_printing_form_schema');
  const escape=(value:string)=>value.replace(/[&<>"'{}]/gu,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;','{':'&#123;','}':'&#125;'}[char]!));
  const html='<section class="print-card">\n<dl>\n'+schema.fields.map(field=>`  <dt>${escape(field.displayName)}</dt><dd>{{${field.fieldKey}}}</dd>`).join('\n')+'\n</dl>\n</section>';
  if(html.length>65536)throw Error('client.printing_schema_layout_budget_exceeded');return html;
}
