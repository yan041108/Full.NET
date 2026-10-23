"""真实原始字节的结构回归；整个关联批次完成后由统一入口集中执行。"""
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "src/Tools/Full.NET.Ai.DocumentParser"
sys.path.insert(0, str(TOOL))
from protocol import Document, Rejected, position
from text_formats import parse_csv, parse_html
from ooxml import parse_xlsx, parse_pptx


def package(parts):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, value in parts.items():
            archive.writestr(name, value)
    return output.getvalue()


def spreadsheet(body, extra=None, sheets=None):
    ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    sheets = sheets or '<sheet name="中文" sheetId="1" r:id="r1"/>'
    parts = {
        "[Content_Types].xml": '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>',
        "_rels/.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="office" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>' ,
        "xl/workbook.xml": f'<workbook xmlns="{ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{sheets}</sheets></workbook>',
        "xl/_rels/workbook.xml.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>' ,
        "xl/worksheets/sheet1.xml": f'<worksheet xmlns="{ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">{body}</worksheet>'}
    parts.update(extra or {})
    for name in extra or {}:
        if name.startswith("xl/tables/"):
            parts["[Content_Types].xml"] = parts["[Content_Types].xml"].replace("</Types>", f'<Override PartName="/{name}" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/></Types>')
    return package(parts)


def slides(text="标题😀", notes="备注中文"):
    p = "http://schemas.openxmlformats.org/presentationml/2006/main"
    a = "http://schemas.openxmlformats.org/drawingml/2006/main"
    return package({
        "[Content_Types].xml": '<Types><Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/></Types>',
        "ppt/presentation.xml": f'<p:presentation xmlns:p="{p}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:sldIdLst><p:sldId r:id="r1"/></p:sldIdLst></p:presentation>',
        "ppt/_rels/presentation.xml.rels": '<Relationships><Relationship Id="r1" Target="slides/slide1.xml"/></Relationships>',
        "ppt/slides/slide1.xml": f'<p:sld xmlns:p="{p}" xmlns:a="{a}"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>',
        "ppt/slides/_rels/slide1.xml.rels": '<Relationships><Relationship Id="r2" Target="../notesSlides/notesSlide1.xml"/></Relationships>',
        "ppt/notesSlides/notesSlide1.xml": f'<p:notes xmlns:p="{p}" xmlns:a="{a}"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{notes}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:notes>'})


class DocumentParserTests(unittest.TestCase):
    def test_csv_multiline_quotes_empty_cells_header_and_utf16(self):
        raw = '\ufeff列一,列二\r\n"中😀\r\n多行","a""b"\r\n,末尾\r\n'.encode()
        doc = parse_csv(raw, "utf-8", ",", True)
        self.assertEqual([b["text"] for b in doc.blocks], ["列一", "列二", "中😀\r\n多行", 'a"b', "", "末尾"])
        self.assertEqual(doc.blocks[2]["position"]["grid"]["row"], 2)
        self.assertEqual(doc.blocks[2]["position"]["length"], 7)
        self.assertTrue(doc.blocks[0]["position"]["isHeader"])
        wire = json.loads(doc.encode())
        self.assertEqual(wire["contentHash"], hashlib.sha256(raw).hexdigest())
        self.assertEqual(wire["sizeBytes"], len(raw))

    def test_legacy_chinese_encoding_requires_explicit_choice(self):
        raw = "列一;列二\n中文;内容".encode("gb18030")
        with self.assertRaises(Rejected):
            parse_csv(raw, "utf-8", ";", True)
        self.assertEqual(parse_csv(raw, "gb18030", ";", True).blocks[2]["text"], "中文")

    def test_csv_rejects_partial_quotes_ragged_rows_and_nul(self):
        for raw in (b'a,b\n"unfinished,b', b"a,b\nx", b"a,\0b"):
            with self.subTest(raw=raw), self.assertRaises(Rejected):
                parse_csv(raw, "utf-8", ",", False)

    def test_csv_row_and_column_budgets_are_enforced(self):
        for raw in (b",".join([b"x"] * 65), b"\n".join([b"x"] * 8193)):
            with self.assertRaises(Rejected):
                parse_csv(raw, "utf-8", ",", False)

    def test_html_preserves_title_nodes_entities_and_safe_text(self):
        raw = b'<!DOCTYPE html><html><head><title>Title</title><script>secret</script></head><body><p>A &amp; B<br>C</p><div hidden>hidden</div><iframe src="https://invalid.example">remote</iframe><table><tr><td>cell</td></tr></table></body></html>'
        doc = parse_html(raw, "utf-8")
        self.assertEqual([b["text"] for b in doc.blocks], ["Title", "A & B", "C", "cell"])
        self.assertEqual(doc.blocks[-1]["position"]["htmlPath"], "/html[1]/body[1]/table[1]/tr[1]/td[1]")

    def test_html_rejects_entity_declarations_and_deep_trees(self):
        for text in ('<!DOCTYPE x SYSTEM "file:///secret"><html>bad</html>', "<div>" * 33 + "x"):
            with self.assertRaises(Rejected):
                parse_html(text.encode(), "utf-8")

    def test_excel_keeps_cached_formula_missing_cache_types_and_merge(self):
        body = '<sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>中文😀</t></is></c><c r="B1"><f>1+2</f><v>3</v></c><c r="C1"><f>SUM(A2:A4)</f></c></row></sheetData><mergeCells><mergeCell ref="A1:A2"/></mergeCells>'
        doc = parse_xlsx(spreadsheet(body))
        self.assertEqual(doc.blocks[0]["position"]["grid"]["mergedRange"], "1:1:2:1")
        self.assertEqual(doc.blocks[1]["text"], "3")
        self.assertEqual(doc.blocks[1]["position"]["grid"]["cacheState"], "present-unverified")
        self.assertEqual(doc.blocks[2]["position"]["grid"]["cacheState"], "missing")
        self.assertEqual(doc.blocks[2]["text"], "")

    def test_excel_empty_numeric_formula_cache_does_not_invent_zero(self):
        raw = spreadsheet('<sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>标题</t></is></c><c r="B1"><f>1+2</f><v/></c></row></sheetData>')
        result = parse_xlsx(raw)
        formula = result.blocks[1]
        self.assertEqual(formula["text"], "")
        self.assertEqual(formula["position"]["grid"]["cacheState"], "missing")
        self.assertIsNone(formula["position"]["grid"]["rawValue"])

    def test_excel_native_table_header_and_empty_merged_anchor_are_preserved(self):
        body = '<sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Name</t></is></c><c r="B1" t="inlineStr"><is><t>Value</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>知识库</t></is></c><c r="B2"><v>7</v></c></row></sheetData><mergeCells count="1"><mergeCell ref="C3:D4"/></mergeCells><tableParts count="1"><tablePart r:id="table1"/></tableParts>'
        table = '<table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="1" name="Table1" displayName="Table1" ref="A1:B2" headerRowCount="1" totalsRowShown="0"><tableColumns count="2"><tableColumn id="1" name="Name"/><tableColumn id="2" name="Value"/></tableColumns></table>'
        rels = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="table1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table1.xml"/></Relationships>'
        doc = parse_xlsx(spreadsheet(body, {"xl/tables/table1.xml": table, "xl/worksheets/_rels/sheet1.xml.rels": rels}))
        self.assertEqual([block["position"]["isHeader"] for block in doc.blocks[:4]], [True, True, False, False])
        empty = next(block for block in doc.blocks if block["position"]["grid"]["row"] == 3)
        self.assertEqual(empty["text"], ""); self.assertEqual(empty["position"]["grid"]["mergedRange"], "3:3:4:4")

    def test_excel_overlapping_merges_are_rejected_even_without_intersection_cells(self):
        body = '<sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>标题</t></is></c></row></sheetData><mergeCells count="2"><mergeCell ref="B2:D4"/><mergeCell ref="C3:E5"/></mergeCells>'
        with self.assertRaises(Rejected):
            parse_xlsx(spreadsheet(body))

    def test_excel_hidden_rows_columns_and_sheets_have_explicit_policy(self):
        body = '<cols><col min="2" max="2" hidden="1"/></cols><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>A</t></is></c><c r="B1" t="inlineStr"><is><t>B</t></is></c></row><row r="2" hidden="1"><c r="A2" t="inlineStr"><is><t>C</t></is></c></row></sheetData>'
        self.assertEqual(len(parse_xlsx(spreadsheet(body)).blocks), 1)
        self.assertEqual(len(parse_xlsx(spreadsheet(body), True).blocks), 3)
        hidden = spreadsheet(body, sheets='<sheet name="隐藏" sheetId="1" state="hidden" r:id="r1"/>')
        with self.assertRaises(Rejected):
            parse_xlsx(hidden).encode()
        self.assertEqual(len(parse_xlsx(hidden, True).blocks), 3)

    def test_excel_rejects_duplicate_cells_invalid_relationships_entities_and_images(self):
        body = '<sheetData><row r="1"><c r="A1"><v>1</v></c><c r="A1"><v>2</v></c></row></sheetData>'
        samples = [spreadsheet(body), spreadsheet("", {"xl/embeddings/oleObject1.bin": b"object"}),
                   spreadsheet("", {"xl/_rels/workbook.xml.rels": '<Relationships><Relationship Id="r1" TargetMode="External" Target="https://invalid.example"/></Relationships>'}),
                   spreadsheet("", {"xl/sharedStrings.xml": '<!DOCTYPE x [<!ENTITY x "boom">]><sst>&x;</sst>'})]
        for raw in samples:
            with self.subTest(hash=hashlib.sha256(raw).hexdigest()), self.assertRaises(Rejected):
                parse_xlsx(raw)

    def test_zip_rejects_path_traversal_and_actual_decompression_overflow(self):
        for raw in (package({"../escape": "bad"}), package({"large": b"x" * (8 * 1024 * 1024 + 1)})):
            with self.assertRaises(Rejected):
                parse_xlsx(raw)

    def test_presentation_keeps_visible_text_and_notes_as_distinct_locations(self):
        doc = parse_pptx(slides())
        self.assertEqual([b["text"] for b in doc.blocks], ["标题😀", "备注中文"])
        self.assertEqual([b["position"]["kind"] for b in doc.blocks], [8, 9])
        self.assertEqual(doc.blocks[0]["position"]["slide"], {"slideNumber": 1, "shapeNumber": 1,
                         "rowNumber": None, "columnNumber": None, "rowSpan": 1, "columnSpan": 1})

    def test_svg_renderer_subset_keeps_local_clipping_but_never_loads_external_resources(self):
        from images import safe_svg
        raw = b'<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><defs><clipPath id="a"><rect width="50" height="50"/></clipPath></defs><g clip-path="url(#a)"><text style="white-space: pre">A</text></g></svg>'
        self.assertIn(b'clip-path="url(#a)"', safe_svg(raw))
        declaration = b'<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">'
        with self.assertRaises(Rejected): safe_svg(declaration + raw)
        self.assertNotIn(b'DOCTYPE', safe_svg(declaration + raw, trusted_renderer=True))
        for sample in (raw.replace(b'url(#a)', b'url(https://invalid.example)'), raw.replace(b'white-space: pre', b'fill: url(file:///secret)'), raw.replace(b'<text ', b'<text onload="bad()" ')):
            with self.assertRaises(Rejected): safe_svg(sample)

    def test_svg_pixel_budget_is_checked_before_starting_a_renderer(self):
        from images import safe_svg
        with self.assertRaisesRegex(Rejected, "image_pixel_limit"):
            safe_svg(b'<svg xmlns="http://www.w3.org/2000/svg" width="1000000" height="1000000"/>')

    def test_cli_never_delivers_partial_stdout_on_late_rejection(self):
        result = subprocess.run([sys.executable, "-B", str(TOOL / "parser.py"), "--format", "csv"],
                                input=b"a,b\nvalid,row\nragged", capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, b"")
        self.assertEqual(result.stderr, b"fn_ai_parse_csv_shape\n")

    def test_output_and_source_budgets_are_independent_and_no_text_is_explicit(self):
        with self.assertRaises(Rejected):
            Document(b"x" * (8 * 1024 * 1024 + 1), 4)
        doc = Document(b"x", 4)
        with self.assertRaises(Rejected):
            doc.add("x" * (2 * 1024 * 1024 + 1), "", position(0, "x"))
        with self.assertRaisesRegex(Rejected, "no_text"):
            parse_html(b"<html><body><script>only code</script></body></html>", "utf-8").encode()


if __name__ == "__main__":
    unittest.main()
