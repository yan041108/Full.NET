"""PDF 原生文本和扫描区域共用来源页；冲突、无效字符或资源越界不交付部分结果。"""
import io
import math
import itertools
from contextlib import closing
from protocol import Document, Rejected, position
from images import parse_image, load_ocr, checked_size, MAX_TOTAL_PIXELS


def comparable(text):
    return "".join(text.split()).casefold()


def parse_pdf(source):
    document = Document(source, 2)
    import pypdfium2 as pdfium
    images = []; pixels = 0; ocr = None
    with pdfium.PdfDocument(source) as pdf:
        if not 1 <= len(pdf) <= 128:
            raise Rejected("pdf_page_limit")
        for number in range(len(pdf)):
            with closing(pdf[number]) as page, closing(page.get_textpage()) as text_page:
                if text_page.count_chars() > 2 * 1024 * 1024:
                    raise Rejected("output_limit")
                text = text_page.get_text_bounded(errors="strict")
                native = text if "\ufffd" not in text and "\0" not in text else ""
                anchor = position(1, native, pageNumber=number + 1)
                document.add(native, "", anchor)
                objects = list(itertools.islice(page.get_objects(max_depth=16), 8193))
                if len(objects) > 8192:
                    raise Rejected("pdf_object_limit")
                if native.strip() and not any(obj.type in (3, 5) for obj in objects):
                    continue
                width, height = page.get_size()
                if not math.isfinite(width) or not math.isfinite(height) or width <= 0 or height <= 0:
                    raise Rejected("pdf_geometry")
                checked_size(math.ceil(width * 2), math.ceil(height * 2))
                pixels += math.ceil(width * 2) * math.ceil(height * 2)
                if pixels > MAX_TOTAL_PIXELS:
                    raise Rejected("image_total_pixel_limit")
                if ocr is None:
                    ocr = load_ocr()
                with closing(page.render(scale=2, may_draw_forms=False)) as bitmap:
                    converter = bitmap.get_posconv(page)
                    image = bitmap.to_pil(); png = io.BytesIO(); image.save(png, format="PNG")
                    recognized = parse_image(png.getvalue(), "png", ocr)
                    for block in recognized.blocks:
                        box = block["position"]["image"]
                        x1, y1 = converter.to_page(box["x"], box["y"])
                        x2, y2 = converter.to_page(box["x"]+box["width"], box["y"]+box["height"])
                        overlap = text_page.get_text_bounded(min(x1,x2)-2, min(y1,y2)-2, max(x1,x2)+2, max(y1,y2)+2, errors="strict") if native else ""
                        if overlap.strip():
                            if comparable(block["text"]) == comparable(overlap):
                                continue
                            raise Rejected("ocr_native_conflict")
                        box["parent"] = position(1, "", pageNumber=number + 1)
                        images.append(block)
        for block in images:
            document.add(block["text"], block["separator"], block["position"])
    return document
