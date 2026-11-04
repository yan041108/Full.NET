"""校验正式报表下载的固定 Open XML 包、单列文本与查询结果；只输出结构证据。"""
import base64
import io
import json
import sys
import zipfile
import xml.etree.ElementTree as ET


def verify(payload):
    source = base64.b64decode(payload['workbook'], validate=True)
    if not 0 < len(source) <= 4 * 1024 * 1024:
        raise ValueError('Workbook size invalid')
    namespace = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
    paths = {'[Content_Types].xml', '_rels/.rels', 'xl/workbook.xml',
             'xl/_rels/workbook.xml.rels', 'xl/worksheets/sheet1.xml'}
    with zipfile.ZipFile(io.BytesIO(source)) as archive:
        # 当前正式渲染器只写五个条目；拒绝重复条目、额外表或扩大解压预算。
        entries = archive.infolist()
        if len(entries) != len(paths) or {e.filename for e in entries} != paths:
            raise ValueError('Workbook package invalid')
        if sum(e.file_size for e in entries) > 4 * 1024 * 1024 or archive.testzip():
            raise ValueError('Workbook archive invalid')
        documents = {name: ET.fromstring(archive.read(name)) for name in paths}
        package = '{http://schemas.openxmlformats.org/package/2006/relationships}'
        office = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
        types = '{http://schemas.openxmlformats.org/package/2006/content-types}'
        overrides = {e.get('PartName'): e.get('ContentType')
                     for e in documents['[Content_Types].xml'].findall(types + 'Override')}
        if overrides != {
            '/xl/workbook.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml',
            '/xl/worksheets/sheet1.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml',
        }:
            raise ValueError('Workbook content types invalid')
        for path, target, relationship_type in [
            ('_rels/.rels', 'xl/workbook.xml', office + '/officeDocument'),
            ('xl/_rels/workbook.xml.rels', 'worksheets/sheet1.xml', office + '/worksheet'),
        ]:
            relations = documents[path].findall(package + 'Relationship')
            if len(relations) != 1 or relations[0].attrib != {
                'Id': 'rId1', 'Type': relationship_type, 'Target': target,
            }:
                raise ValueError('Workbook relationships invalid')
        sheets = documents['xl/workbook.xml'].findall(namespace + 'sheets/' + namespace + 'sheet')
        if len(sheets) != 1 or sheets[0].get('name') != 'Export' or sheets[0].get('{' + office + '}id') != 'rId1':
            raise ValueError('Workbook sheet invalid')
        root = documents['xl/worksheets/sheet1.xml']
        rows = root.findall(namespace + 'sheetData/' + namespace + 'row')
        if len(rows) != 2:
            raise ValueError('Workbook data row count invalid')
        values = []
        for number, row in enumerate(rows, 1):
            cells = row.findall(namespace + 'c')
            if row.get('r') != str(number) or len(cells) != 1:
                raise ValueError('Workbook row shape invalid')
            cell = cells[0]
            if cell.get('r') != 'A' + str(number) or cell.get('t') != 'inlineStr' or cell.find(namespace + 'f') is not None:
                raise ValueError('Workbook text cell invalid')
            text = cell.find(namespace + 'is/' + namespace + 't')
            if text is None:
                raise ValueError('Workbook cell text missing')
            values.append(text.text)
        if values != ['EngineVersion', payload['expectedValue']]:
            raise ValueError('Workbook content does not match query')
    return {'worksheets': 1, 'dataRows': 1}


if __name__ == '__main__':
    try:
        print(json.dumps(verify(json.load(sys.stdin))))
    except Exception:
        # 异常值可能包含业务字段，CLI 不输出正文、预期值或解析器原始诊断。
        print('Reporting workbook verification failed', file=sys.stderr)
        sys.exit(1)
