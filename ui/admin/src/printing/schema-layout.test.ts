import {expect,it} from 'vitest';
import {suggestedPrintingSchemaLayout} from './schema-layout';
const schema={formSchemaKey:'demo.business',displayName:'Business',description:'Fixed business fields',requiresRecordId:true,fields:[{fieldKey:'requestNumber',displayName:'Number'}]};
it('基础版式只使用固定字段，占位符保持原样而目录标签作为文本转义',()=>{
 const html=suggestedPrintingSchemaLayout({...schema,fields:[{fieldKey:'requestNumber',displayName:'<script>"{{secret}}" & \'</script>'}]});
 expect(html).toContain('<dd>{{requestNumber}}</dd>');expect(html).not.toContain('<script>');expect(html).not.toContain('{{secret}}');expect(html).toContain('&lt;script&gt;');expect(html).toContain('&#123;&#123;secret&#125;&#125;');
});
it('可执行字段名和无界字段目录拒绝生成，不截断版式',()=>{
 expect(()=>suggestedPrintingSchemaLayout({...schema,fields:[{fieldKey:'x}}<script>',displayName:'Bad'}]})).toThrow();
 expect(()=>suggestedPrintingSchemaLayout({...schema,fields:Array.from({length:65},(_,index)=>({fieldKey:'field'+index,displayName:'Field'}))})).toThrow();
 expect(()=>suggestedPrintingSchemaLayout({...schema,fields:[{fieldKey:'name',displayName:'x'.repeat(65536)}]})).toThrow();
});
