"""填充正式五列多行模板或读取真实错误回执；不制造服务端业务结果。"""
import base64, io, json, sys, zipfile
import xml.etree.ElementTree as ET

payload = json.load(sys.stdin)
source = base64.b64decode(payload['workbook'], validate=True)
ns = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
ET.register_namespace('', ns[1:-1])
with zipfile.ZipFile(io.BytesIO(source)) as archive:
    if sum(x.file_size for x in archive.infolist()) > 4 * 1024 * 1024:
        raise ValueError('Workbook expansion exceeds test budget')
    sheet = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
    data = sheet.find(ns + 'sheetData')
    if payload['mode'] == 'read':
        rows = [[ ''.join(cell.itertext()) for cell in row ] for row in data]
        if rows[0] != ['lineNumber', 'errorCode', 'message']:
            raise ValueError('Unexpected receipt schema')
        print(json.dumps(rows[1:]))
    else:
        if len(data) != 1 or len(payload['rows']) != 3:
            raise ValueError('Expected empty official template and three rows')
        for number, values in enumerate(payload['rows'], 2):
            if len(values) != 5 or not all(isinstance(x, str) for x in values):
                raise ValueError('Expected five string fields')
            row = ET.SubElement(data, ns + 'row', r=str(number))
            for index, value in enumerate(values):
                cell = ET.SubElement(row, ns + 'c', r=chr(ord('A')+index)+str(number), t='inlineStr')
                ET.SubElement(ET.SubElement(cell, ns+'is'), ns+'t').text = value
        target = io.BytesIO()
        with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED) as output:
            for entry in archive.infolist():
                output.writestr(entry, ET.tostring(sheet, encoding='utf-8', xml_declaration=True) if entry.filename == 'xl/worksheets/sheet1.xml' else archive.read(entry))
        sys.stdout.buffer.write(target.getvalue())
