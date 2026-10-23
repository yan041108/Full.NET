"""有界私有解析协议；失败不向 stdout 交付任何部分正文。"""
import hashlib
import json

MAX_SOURCE = 8 * 1024 * 1024
MAX_TEXT = 2 * 1024 * 1024
MAX_BLOCKS = 8192
MAX_WIRE = 16 * 1024 * 1024


class Rejected(Exception):
    pass


def utf16_length(text):
    return len(text.encode("utf-16-le", errors="strict")) // 2


def position(kind, text, **fields):
    value = dict(kind=kind, offset=0, length=utf16_length(text), pageNumber=None,
                 blockSequence=None, paragraphNumber=None, tableNumber=None,
                 rowNumber=None, cellNumber=None, cellParagraphCount=0, isHeader=False,
                 grid=None, slide=None, htmlPath=None, image=None)
    value.update(fields)
    return value


class Document:
    def __init__(self, source, format_number):
        if not source or len(source) > MAX_SOURCE:
            raise Rejected("source_limit")
        self.source = source
        self.format = format_number
        kind = {0: "txt", 1: "md", 2: "pdf", 3: "docx", 4: "csv", 5: "html", 6: "xlsx", 7: "doc", 8: "pptx", 9: "png"}[format_number]
        self.policy = policy(kind)
        self.blocks = []
        self.characters = 0

    def add(self, text, separator, location):
        if "\0" in text or separator not in ("", "\n", "\t"):
            raise Rejected("invalid_text")
        self.characters += utf16_length(text) + len(separator)
        if self.characters > MAX_TEXT or len(self.blocks) >= MAX_BLOCKS:
            raise Rejected("output_limit")
        self.blocks.append(dict(sequence=len(self.blocks), text=text,
                                separator=separator, position=location))

    def encode(self):
        if not any(b["text"].strip() for b in self.blocks):
            raise Rejected("no_text")
        result = dict(protocolVersion=1, format=self.format,
                      parserVersion="fullnet-multiformat-v1",
                      contentHash=hashlib.sha256(self.source).hexdigest(),
                      sizeBytes=len(self.source), blocks=self.blocks)
        result["policy"] = self.policy
        raw = json.dumps(result, ensure_ascii=True, allow_nan=False,
                         separators=(",", ":")).encode("ascii")
        if len(raw) > MAX_WIRE:
            raise Rejected("output_limit")
        return raw


def grid(sheet, name, row, column, cell_type="text", formula=None, cache=None, merged=None):
    if not 1 <= row <= 1048576 or not 1 <= column <= 16384:
        raise Rejected("cell_limit")
    return dict(sheetNumber=sheet, sheetName=name, row=row, column=column,
                cellType=cell_type, formula=formula, cacheState=cache, mergedRange=merged,
                rawValue=None, numberFormat=None, cachedValueType=None)


def slide(number, shape, row=None, column=None, row_span=1, column_span=1):
    return dict(slideNumber=number, shapeNumber=shape, rowNumber=row, columnNumber=column,
                rowSpan=row_span, columnSpan=column_span)


def policy(kind, encoding=None, delimiter=None, header=False, hidden=False):
    return dict(inputFormat=kind, textEncoding=encoding if encoding is not None else "utf-8" if kind in ("csv", "html", "txt", "md") else None,
                csvDelimiter=delimiter if delimiter is not None else "," if kind == "csv" else None,
                csvHeader=header, includeHidden=hidden)
