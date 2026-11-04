"""在正式下载模板内追加固定五列数据行；仅供隔离应用验收使用。"""
import io,json,sys,zipfile
import xml.etree.ElementTree as ET
payload=json.load(sys.stdin)
import base64
source=base64.b64decode(payload['template'],validate=True)
values=payload['values']
if len(values)!=5 or not all(isinstance(v,str) for v in values):
    raise ValueError('Expected five string fields')
ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'
ET.register_namespace('',ns)
with zipfile.ZipFile(io.BytesIO(source)) as archive:
    sheet=ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
    data=sheet.find('{'+ns+'}sheetData')
    if data is None or len(data)!=1:
        raise ValueError('Expected an unfilled official template')
    row=ET.SubElement(data,'{'+ns+'}row',r='2')
    for index,value in enumerate(values):
        cell=ET.SubElement(row,'{'+ns+'}c',r=chr(ord('A')+index)+'2',t='inlineStr')
        text=ET.SubElement(ET.SubElement(cell,'{'+ns+'}is'),'{'+ns+'}t')
        text.text=value
    target=io.BytesIO()
    with zipfile.ZipFile(target,'w',compression=zipfile.ZIP_DEFLATED) as output:
        for entry in archive.infolist():
            output.writestr(entry,ET.tostring(sheet,encoding='utf-8',xml_declaration=True) if entry.filename=='xl/worksheets/sheet1.xml' else archive.read(entry))
    sys.stdout.buffer.write(target.getvalue())
