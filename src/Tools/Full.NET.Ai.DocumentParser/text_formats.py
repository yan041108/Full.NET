"""CSV 使用显式编码/分隔符；HTML 只读本地字节，永不加载资源或执行代码。"""
import csv
import io
from html.parser import HTMLParser
from protocol import Document, Rejected, position, grid, policy


def decode(source, encoding):
    if encoding not in ("utf-8", "gb18030"):
        raise Rejected("unsupported_encoding")
    try:
        text = source.decode("utf-8-sig" if encoding == "utf-8" else encoding, errors="strict")
        if "\0" in text:
            raise Rejected("invalid_text")
        return text
    except UnicodeError as error:
        raise Rejected("invalid_encoding") from error


def parse_csv(source, encoding, delimiter, header):
    if delimiter not in (",", "\t", ";") or not isinstance(header, bool):
        raise Rejected("invalid_csv_policy")
    document = Document(source, 4)
    document.policy = policy("csv", encoding, delimiter, header)
    # StringIO 的 newline='' 保留引号内 CRLF；逻辑记录坐标不冒充物理文件行。
    reader = csv.reader(io.StringIO(decode(source, encoding), newline=""),
                        delimiter=delimiter, strict=True)
    csv.field_size_limit(2 * 1024 * 1024)
    columns = None
    try:
        for row_number, row in enumerate(reader, 1):
            if row_number > 8192 or not row or len(row) > 64:
                raise Rejected("csv_shape")
            if columns is None:
                columns = len(row)
            if len(row) != columns:
                raise Rejected("csv_shape")
            for column, text in enumerate(row, 1):
                document.add(text, "\n" if column == columns else "\t",
                             position(4, text, grid=grid(1, "CSV", row_number, column),
                                      isHeader=header and row_number == 1))
    except csv.Error as error:
        raise Rejected("invalid_csv") from error
    return document


VOID = frozenset("area base br col embed hr img input link meta param source track wbr".split())
OMIT = frozenset("script style template noscript iframe object embed svg canvas".split())


class LocalHtml(HTMLParser):
    def __init__(self, document):
        super().__init__(convert_charrefs=True)
        self.document = document
        self.stack = []
        self.root_counts = {}
        self.pending = []
        self.nodes = 0

    def flush(self):
        if self.pending:
            text = "".join(self.pending)
            self.pending.clear()
            if text.strip() and self.stack and not any(item[2] for item in self.stack):
                path = "".join("/" + item[0] for item in self.stack)
                self.document.add(text, "\n", position(10, text, htmlPath=path))

    def handle_starttag(self, tag, attrs):
        self.flush()
        self.nodes += 1
        if self.nodes > 65536 or len(attrs) > 64 or len(self.stack) >= 32:
            raise Rejected("html_limit")
        if not tag.isascii() or not tag.replace("-", "").isalnum() or len(tag) > 32:
            raise Rejected("html_structure")
        counts = self.stack[-1][1] if self.stack else self.root_counts
        counts[tag] = counts.get(tag, 0) + 1
        attributes = dict(attrs)
        # 只采用明确隐藏标记；不下载样式表，也不猜测外部 CSS 的可见性。
        hidden = tag in OMIT or "hidden" in attributes or attributes.get("aria-hidden", "").lower() == "true"
        if tag not in VOID:
            self.stack.append((f"{tag}[{counts[tag]}]", {}, hidden))

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in VOID:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        self.flush()
        for index in range(len(self.stack) - 1, -1, -1):
            if self.stack[index][0].split("[", 1)[0] == tag:
                del self.stack[index:]
                return

    def handle_data(self, data):
        if sum(len(value) for value in self.pending) + len(data) > 2 * 1024 * 1024:
            raise Rejected("output_limit")
        self.pending.append(data)

    def handle_decl(self, decl):
        if decl.lower() != "doctype html":
            raise Rejected("html_declaration")

    def unknown_decl(self, data):
        raise Rejected("html_declaration")


def parse_html(source, encoding):
    document = Document(source, 5)
    document.policy = policy("html", encoding)
    parser = LocalHtml(document)
    parser.feed(decode(source, encoding))
    parser.close()
    parser.flush()
    return document
