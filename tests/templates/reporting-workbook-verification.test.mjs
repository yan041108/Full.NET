import assert from 'node:assert/strict';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const python=process.platform==='win32'?'python':'python3';
const script=fileURLToPath(new URL('./support/verify-reporting-workbook.py',import.meta.url));
const maker=String.raw`
import io,json,sys,zipfile,base64
import xml.etree.ElementTree as ET
p=json.load(sys.stdin)
ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'
root=ET.Element('worksheet',xmlns=ns)
data=ET.SubElement(root,'sheetData')
for i,value in enumerate(p.get('rows',['EngineVersion','16.0.4135.4']),1):
    row=ET.SubElement(data,'row',r=str(i));cell=ET.SubElement(row,'c',r='A'+str(i),t='inlineStr')
    if p.get('formula') and i==2: ET.SubElement(cell,'f').text='SECRET'
    ET.SubElement(ET.SubElement(cell,'is'),'t').text=value
target=io.BytesIO()
with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
    types='http://schemas.openxmlformats.org/package/2006/content-types'
    rels='http://schemas.openxmlformats.org/package/2006/relationships'
    office='http://schemas.openxmlformats.org/officeDocument/2006/relationships'
    z.writestr('[Content_Types].xml','<Types xmlns="'+types+'"><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="'+('SECRET' if p.get('wrongType') else 'application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml')+'"/></Types>')
    z.writestr('_rels/.rels','<Relationships xmlns="'+rels+'"><Relationship Id="rId1" Type="'+office+'/officeDocument" Target="xl/workbook.xml"/></Relationships>')
    z.writestr('xl/workbook.xml','<workbook xmlns="'+ns+'" xmlns:r="'+office+'"><sheets><sheet name="Export" r:id="rId1"/></sheets></workbook>')
    z.writestr('xl/_rels/workbook.xml.rels','<Relationships xmlns="'+rels+'"><Relationship Id="rId1" Type="'+office+'/worksheet" Target="'+('SECRET' if p.get('wrongTarget') else 'worksheets/sheet1.xml')+'"/></Relationships>')
    z.writestr('xl/worksheets/sheet1.xml',ET.tostring(root))
    if p.get('extra'): z.writestr('xl/worksheets/sheet2.xml','<worksheet/>')
print(base64.b64encode(target.getvalue()).decode())
`;

function run(options={}) {
 const made=spawnSync(python,['-X','utf8','-c',maker],{input:JSON.stringify(options),encoding:'utf8',windowsHide:true,timeout:15_000});
 assert.equal(made.status,0,made.stderr);
 return spawnSync(python,['-X','utf8',script],{input:JSON.stringify({workbook:options.corrupt?'UEsA':made.stdout.trim(),expectedValue:'16.0.4135.4'}),
  encoding:'utf8',windowsHide:true,timeout:15_000});
}
test('固定报表工作簿必须能解压解析且文本值与查询精确一致',()=>{
 const r=run();assert.equal(r.status,0,r.stderr);assert.deepEqual(JSON.parse(r.stdout),{worksheets:1,dataRows:1});
});
for(const [name,options] of [
 ['损坏的 ZIP',{corrupt:true}],['额外工作表',{extra:true}],['空数据行',{rows:['EngineVersion']}],
 ['错误查询值',{rows:['EngineVersion','SECRET']}],['错误列名',{rows:['SECRET','16.0.4135.4']}],['公式单元格',{formula:true}],
 ['错误工作表关系',{wrongTarget:true}],['错误内容类型声明',{wrongType:true}],
]) test('拒绝'+name+'且错误不回显内容',()=>{
 const r=run(options);assert.equal(r.status,1);assert.equal(r.stdout,'');assert.doesNotMatch(r.stderr,/SECRET|16\.0\.4135\.4/u);
});
