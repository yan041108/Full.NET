"""内存内解包并校验实际膨胀量；不提取路径、不解析实体、不执行 Office 内容。"""
import io
import posixpath
import re
import zipfile
import xml.etree.ElementTree as ET
from protocol import Document, Rejected, position, grid, slide, policy, MAX_SOURCE

S = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
R = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"


class Package:
    def __init__(self, source, allow_media=False):
        if not source or len(source) > MAX_SOURCE:
            raise Rejected("source_limit")
        self.parts = {}
        self.xml_cache = {}
        self.xml_nodes = 0
        total = 0
        try:
            with zipfile.ZipFile(io.BytesIO(source)) as archive:
                entries = archive.infolist()
                if len(entries) > 256:
                    raise Rejected("archive_limit")
                for entry in entries:
                    name = entry.filename
                    if (name.startswith("/") or "\\" in name or ":" in name or "\0" in name
                            or any(p in (".", "..", "") for p in name.rstrip("/").split("/"))
                            or name.lower() in self.parts or entry.flag_bits & 1):
                        raise Rejected("archive_structure")
                    if entry.is_dir():
                        continue
                    with archive.open(entry) as item:
                        raw = item.read(8 * 1024 * 1024 + 1)
                    total += len(raw)
                    if len(raw) > 8 * 1024 * 1024 or total > 32 * 1024 * 1024:
                        raise Rejected("archive_limit")
                    self.parts[name.lower()] = raw
        except (zipfile.BadZipFile, RuntimeError, NotImplementedError, OSError) as error:
            raise Rejected("invalid_archive") from error
        # 图片尚未取得完整 OCR 时绝不把只读到原生文字的输出冒充完整解析。
        if any(("/media/" in n and not allow_media) or n.endswith("vbaproject.bin") or "/embeddings/" in n for n in self.parts):
            raise Rejected("embedded_content_requires_ocr")
        types = self.xml("[Content_Types].xml")
        if any(any(marker in entry.get("ContentType", "").lower() for marker in ("vba", "macroenabled", "oleobject", "activex")) for entry in types):
            raise Rejected("active_office_content")
        for name in self.parts:
            if name.endswith((".xml", ".rels")):
                self.xml(name)
            if name.endswith(".rels"):
                part = "" if name == "_rels/.rels" else posixpath.join(posixpath.dirname(posixpath.dirname(name)), posixpath.basename(name)[:-5])
                self.relations(part)

    def xml(self, name):
        if name.lower() in self.xml_cache:
            return self.xml_cache[name.lower()]
        raw = self.parts.get(name.lower())
        if raw is None:
            raise Rejected("missing_part")
        if b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper():
            raise Rejected("xml_declaration")
        # XML 只允许 UTF-8，避免 UTF-16/32 隐藏 DTD 声明绕过字节检查。
        try:
            text = raw.decode("utf-8-sig", errors="strict")
            if "\0" in text:
                raise Rejected("invalid_xml")
            root = ET.fromstring(text)
        except (UnicodeError, ET.ParseError) as error:
            raise Rejected("invalid_xml") from error
        stack = [(root, 1)]
        nodes = 0
        while stack:
            element, depth = stack.pop()
            nodes += 1 + len(element.attrib)
            if depth > 32 or nodes > 65536 or len(element.attrib) > 64:
                raise Rejected("xml_limit")
            stack.extend((child, depth + 1) for child in element)
        self.xml_nodes += nodes
        if self.xml_nodes > 65536:
            raise Rejected("xml_limit")
        self.xml_cache[name.lower()] = root
        return root

    def relations(self, part):
        folder, name = posixpath.split(part)
        rel_name = posixpath.join(folder, "_rels", name + ".rels")
        root = self.xml(rel_name)
        result = {}
        for rel in root:
            if any(marker in rel.get("Type", "").lower() for marker in ("vbaproject", "oleobject", "activex", "attachedtemplate")):
                raise Rejected("active_office_content")
            target = rel.get("Target", "")
            key = rel.get("Id", "")
            if not key or key in result or rel.get("TargetMode") == "External" or ":" in target or "\\" in target:
                raise Rejected("external_or_invalid_relationship")
            resolved = posixpath.normpath(posixpath.join(folder, target.lstrip("/"))) if not target.startswith("/") else target[1:]
            if resolved.startswith("../") or resolved.lower() not in self.parts:
                raise Rejected("invalid_relationship")
            result[key] = resolved
        return result


def address(value):
    match = re.fullmatch(r"([A-Z]{1,3})([1-9][0-9]{0,6})", value or "")
    if not match:
        raise Rejected("invalid_cell_address")
    column = 0
    for char in match[1]:
        column = column * 26 + ord(char) - 64
    row = int(match[2])
    if row > 1048576 or column > 16384:
        raise Rejected("cell_limit")
    return row, column


def parse_xlsx(source, include_hidden=False):
    package = Package(source, allow_media=True)
    types = package.xml("[Content_Types].xml")
    if not any(e.get("PartName") == "/xl/workbook.xml" and e.get("ContentType") ==
               "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" for e in types):
        raise Rejected("format_mismatch")
    for name in package.parts:
        if name.endswith((".xml", ".rels")):
            root = package.xml(name)
            if name.startswith("xl/worksheets/") and name.endswith(".xml"):
                addresses = set()
                for cell in root.findall(S + "sheetData/" + S + "row/" + S + "c"):
                    cell_address = cell.get("r", "")
                    if not re.fullmatch(r"[A-Z]{1,3}[1-9][0-9]{0,6}", cell_address) or cell_address in addresses:
                        raise Rejected("duplicate_or_invalid_cell")
                    addresses.add(cell_address)
    from office import parse_office
    document = parse_office(source, "xlsx", include_hidden)
    workbook = package.xml("xl/workbook.xml")
    relationships = package.relations("xl/workbook.xml")
    sheets = workbook.find(S + "sheets")
    if sheets is None or not 1 <= len(sheets) <= 128:
        raise Rejected("sheet_limit")
    images = []
    for number, sheet in enumerate(sheets, 1):
        if sheet.get("state", "visible") != "visible" and not include_hidden:
            continue
        name = sheet.get("name", "")
        part = relationships.get(sheet.get(R + "id"))
        if not part:
            raise Rejected("invalid_sheet")
        root = package.xml(part)
        drawings = root.findall(S + "drawing")
        if not drawings:
            continue
        hidden_rows = {int(row.get("r", "0")) for row in root.findall(S + "sheetData/" + S + "row") if row.get("hidden") in ("1", "true")}
        hidden_columns = [(int(col.get("min", "0")), int(col.get("max", "0"))) for col in root.findall(S + "cols/" + S + "col") if col.get("hidden") in ("1", "true")]
        positions = {(b["position"]["grid"]["row"], b["position"]["grid"]["column"]): b["position"]
                     for b in document.blocks if b["position"]["grid"]["sheetNumber"] == number}
        sheet_rels = package.relations(part)
        for drawing in drawings:
            target = sheet_rels.get(drawing.get(R + "id"))
            if not target:
                raise Rejected("invalid_drawing")
            drawing_root = package.xml(target); drawing_rels = package.relations(target)
            xdr = "{http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing}"
            for anchor in drawing_root:
                if anchor.tag not in (xdr + "oneCellAnchor", xdr + "twoCellAnchor"):
                    raise Rejected("unsupported_drawing_anchor")
                marker = anchor.find(xdr + "from")
                if marker is None:
                    raise Rejected("invalid_drawing")
                r, c = int(marker.findtext(xdr + "row", "-1")) + 1, int(marker.findtext(xdr + "col", "-1")) + 1
                if not include_hidden and (r in hidden_rows or any(a <= c <= b for a, b in hidden_columns)):
                    continue
                if anchor.find(xdr + "pic") is None:
                    raise Rejected("unsupported_sheet_graphic")
                parent = positions.get((r, c))
                if parent is None:
                    parent = position(5, "", grid=grid(number, name, r, c, "blank"))
                    document.add("", "\n", parent); positions[(r, c)] = parent
                collect_images(package, anchor, drawing_rels, parent, images)
    from images import append_images
    append_images(document, images)
    return document

W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
DRAW = "{http://schemas.openxmlformats.org/drawingml/2006/main}"
VML = "{urn:schemas-microsoft-com:vml}"


def collect_images(package, element, relationships, parent, output):
    if any(any(int(value) != 0 for value in item.attrib.values()) for item in element.iter(DRAW + "srcRect")):
        raise Rejected("unsupported_image_crop")
    for item in element.iter():
        key = item.get(R + "embed") if item.tag == DRAW + "blip" else item.get(R + "id") if item.tag == VML + "imagedata" else None
        if item.tag == DRAW + "blip" and item.get(R + "link"):
            raise Rejected("external_image")
        if key:
            target = relationships.get(key)
            if not target or target.lower() not in package.parts:
                raise Rejected("image_relationship")
            output.append((package.parts[target.lower()], target.rsplit(".", 1)[-1].lower(), parent))


def paragraph_text(paragraph):
    forbidden = {W + name for name in ("fldSimple", "instrText", "del", "ins", "vanish", "numPr", "object", "txbxContent")}
    if any(item.tag in forbidden for item in paragraph.iter()):
        raise Rejected("unsupported_word_content")
    return "".join((item.text or "") if item.tag == W + "t" else "\t" if item.tag == W + "tab" else "\n"
                   for item in paragraph.iter() if item.tag in (W + "t", W + "tab", W + "br", W + "cr"))


def parse_docx(source):
    package = Package(source, allow_media=True)
    types = package.xml("[Content_Types].xml")
    if not any(e.get("PartName") == "/word/document.xml" and e.get("ContentType") ==
               "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" for e in types):
        raise Rejected("format_mismatch")
    # 非正文故事尚无明确位置类型时拒绝整个输入，不能以读取了主文档冒充完整解析。
    if any(n.startswith(("word/header", "word/footer", "word/footnotes", "word/endnotes")) for n in package.parts):
        raise Rejected("unsupported_word_story")
    root = package.xml("word/document.xml"); body = root.find(W + "body")
    if body is None:
        raise Rejected("word_body")
    rel_name = "word/_rels/document.xml.rels"
    relationships = package.relations("word/document.xml") if rel_name in package.parts else {}
    document = Document(source, 3); images = []; paragraph_number = 1; table_number = 0; offset = 0
    for item in body:
        if item.tag == W + "p":
            text = paragraph_text(item)
            p = position(2, text, offset=offset, blockSequence=len(document.blocks), paragraphNumber=paragraph_number)
            collect_images(package, item, relationships, p, images)
            document.add(text, "\n", p); paragraph_number += 1; offset += p["length"] + 1
        elif item.tag == W + "tbl":
            table_number += 1
            if table_number > 128 or any(e.tag in (W + "gridSpan", W + "vMerge", W + "hMerge") for e in item.iter()):
                raise Rejected("word_table_structure")
            rows = item.findall(W + "tr")
            if not 1 <= len(rows) <= 4096:
                raise Rejected("word_table_limit")
            for row_number, row in enumerate(rows, 1):
                cells = row.findall(W + "tc")
                if not 1 <= len(cells) <= 64:
                    raise Rejected("word_table_limit")
                header = row.find(W + "trPr/" + W + "tblHeader") is not None
                for column, cell in enumerate(cells, 1):
                    paragraphs = cell.findall(W + "p")
                    if not paragraphs or cell.find(W + "tbl") is not None:
                        raise Rejected("word_nested_table")
                    text = "\n".join(paragraph_text(p) for p in paragraphs)
                    p = position(3, text, offset=offset, blockSequence=len(document.blocks), paragraphNumber=paragraph_number,
                                 tableNumber=table_number, rowNumber=row_number, cellNumber=column,
                                 cellParagraphCount=len(paragraphs), isHeader=header)
                    collect_images(package, cell, relationships, p, images)
                    document.add(text, "\n" if column == len(cells) else "\t", p)
                    paragraph_number += len(paragraphs); offset += p["length"] + 1
        elif item.tag != W + "sectPr":
            raise Rejected("unsupported_word_content")
    from images import append_images
    append_images(document, images)
    return document


P = "{http://schemas.openxmlformats.org/presentationml/2006/main}"
A = "{http://schemas.openxmlformats.org/drawingml/2006/main}"


def parse_pptx(source, include_hidden=False):
    package = Package(source, allow_media=True)
    types = package.xml("[Content_Types].xml")
    if not any(e.get("PartName") == "/ppt/presentation.xml" and e.get("ContentType") ==
               "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml" for e in types):
        raise Rejected("format_mismatch")
    document = Document(source, 8)
    document.policy = policy("pptx", hidden=include_hidden)
    images = []
    presentation = package.xml("ppt/presentation.xml")
    slides = presentation.find(P + "sldIdLst")
    relationships = package.relations("ppt/presentation.xml")
    if slides is None or not 1 <= len(slides) <= 128:
        raise Rejected("slide_limit")
    for number, slide_item in enumerate(slides, 1):
        part = relationships.get(slide_item.get(R + "id"))
        if not part:
            raise Rejected("invalid_slide")
        root = package.xml(part)
        if root.get("show", "1") in ("0", "false") and not include_hidden:
            continue
        folder, filename = posixpath.split(part)
        rel_name = posixpath.join(folder, "_rels", filename + ".rels")
        rels = package.relations(part) if rel_name.lower() in package.parts else {}
        presentation_shapes(package, document, root, rels, number, False, images, include_hidden)
        if rels:
            for target in rels.values():
                if target.lower().startswith("ppt/notesslides/notesslide") and target.lower().endswith(".xml"):
                    notes = package.xml(target)
                    folder, filename = posixpath.split(target)
                    note_rel_name = posixpath.join(folder, "_rels", filename + ".rels")
                    note_rels = package.relations(target) if note_rel_name.lower() in package.parts else {}
                    presentation_shapes(package, document, notes, note_rels, number, True, images, include_hidden)
    from images import append_images
    append_images(document, images)
    return document


def presentation_shapes(package, document, root, relationships, number, notes, images, include_hidden=False):
    shape_number = 0
    hidden = set()
    for group in root.iter(P + "grpSp"):
        properties = group.find(P + "nvGrpSpPr/" + P + "cNvPr")
        if properties is not None and properties.get("hidden") in ("1", "true"):
            hidden.update(group.iter())
    for shape in root.iter():
        if shape.tag not in (P + "sp", P + "graphicFrame", P + "pic"):
            continue
        shape_number += 1
        properties = shape.find(".//" + P + "cNvPr")
        if not include_hidden and (shape in hidden or properties is not None and properties.get("hidden") in ("1", "true")):
            continue
        placeholder = shape.find(".//" + P + "ph")
        if notes and placeholder is not None and placeholder.get("type") in ("sldNum", "sldImg", "dt", "hdr", "ftr"):
            continue
        table = shape.find(".//" + A + "tbl")
        if table is not None:
            rows = table.findall(A + "tr")
            if not 1 <= len(rows) <= 4096:
                raise Rejected("slide_table_limit")
            for r, row in enumerate(rows, 1):
                cells = row.findall(A + "tc")
                if not 1 <= len(cells) <= 64:
                    raise Rejected("slide_table_limit")
                for c, cell in enumerate(cells, 1):
                    text = "\n".join("".join(t.text or "" for t in paragraph.iter(A + "t")) for paragraph in cell.iter(A + "p"))
                    document.add(text, "\n" if c == len(cells) else "\t", position(9 if notes else 8, text,
                                 slide=slide(number, shape_number, r, c, int(cell.get("rowSpan", "1")), int(cell.get("gridSpan", "1")))))
            continue
        if shape.tag == P + "graphicFrame":
            raise Rejected("unsupported_slide_graphic")
        text = "\n".join("".join(t.text or "" for t in paragraph.iter(A + "t")) for paragraph in shape.iter(A + "p"))
        p = position(9 if notes else 8, text, slide=slide(number, shape_number))
        if text.strip() or shape.tag == P + "pic":
            document.add(text, "\n", p)
            collect_images(package, shape, relationships, p, images)
