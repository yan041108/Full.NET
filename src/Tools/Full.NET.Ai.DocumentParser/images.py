"""受限解码、方向规范化及离线 OCR；没有任何图片路径或模型地址来自业务输入。"""
import gzip
import hashlib
import io
import json
import math
import os
import re
import struct
from pathlib import Path
import subprocess
import tempfile
import warnings
import xml.etree.ElementTree as ET
from protocol import Document, Rejected, position, policy

FORMATS = {
    "jpg": "JPEG", "jpeg": "JPEG", "jfif": "JPEG", "png": "PNG", "apng": "PNG",
    "webp": "WEBP", "bmp": "BMP", "tif": "TIFF", "tiff": "TIFF", "gif": "GIF",
    "ico": "ICO", "tga": "TGA", "pcx": "PCX", "pnm": "PPM", "pbm": "PPM",
    "pgm": "PPM", "ppm": "PPM", "jp2": "JPEG2000", "j2k": "JPEG2000", "psd": "PSD",
    "heic": "HEIF", "heif": "HEIF", "avif": "AVIF", "jxl": "PNG",
    "svg": "PNG", "svgz": "PNG", "wmf": "PNG", "emf": "PNG"}
MAX_PIXELS = 40000000
MAX_FRAMES = 128
MAX_TOTAL_PIXELS = 80000000
MODEL_VERSION = "paddleocr-3.7.0-ppocrv5-det-0d63e78-rec-682f205"


def checked_size(width, height):
    if not 1 <= width <= 16384 or not 1 <= height <= 16384 or width * height > MAX_PIXELS:
        raise Rejected("image_pixel_limit")


def safe_svg(source, trusted_renderer=False):
    try:
        text = source.decode("utf-8-sig", errors="strict")
        if trusted_renderer:
            declaration = '<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">'
            before, found, after = text.partition(declaration)
            # Draw 固定输出的外部 SVG 声明仅在本地转换结果中移除；从不解析或请求 DTD。
            if found and before.strip() in ('', '<?xml version="1.0" encoding="UTF-8"?>'):
                text = before + after
        if "\0" in text or "<!DOCTYPE" in text.upper() or "<!ENTITY" in text.upper():
            raise Rejected("svg_declaration")
        root = ET.fromstring(text)
    except (ET.ParseError, UnicodeError) as error:
        raise Rejected("invalid_svg") from error
    if root.tag not in ("svg", "{http://www.w3.org/2000/svg}svg"):
        raise Rejected("format_mismatch")
    stack = [(root, 1)]; count = 0
    while stack:
        element, depth = stack.pop(); count += 1 + len(element.attrib)
        if depth > 32 or count > 65536 or len(element.attrib) > 64:
            raise Rejected("svg_limit")
        name = element.tag.rsplit("}", 1)[-1].lower()
        if name in ("script", "foreignobject", "image", "style", "animate", "animatetransform", "set"):
            raise Rejected("svg_active_content")
        for key, value in list(element.attrib.items()):
            local = key.rsplit("}", 1)[-1].lower()
            if local == "style":
                if not re.fullmatch(r"\s*white-space\s*:\s*(pre|normal)\s*;?\s*", value):
                    raise Rejected("svg_active_style")
                element.set("{http://www.w3.org/XML/1998/namespace}space", "preserve" if "pre" in value else "default")
                del element.attrib[key]
                continue
            compact = "".join(value.lower().split())
            if local.startswith("on") or local == "base" or "\\" in value or local in ("href", "src") and not re.fullmatch(r"#[A-Za-z_][A-Za-z0-9_.:-]*", value):
                raise Rejected("svg_external_resource")
            if "url(" in compact and (local not in ("clip-path", "fill", "stroke", "mask", "filter") or not re.fullmatch(r"url\(#[A-Za-z_][A-Za-z0-9_.:-]*\)", value)):
                raise Rejected("svg_external_resource")
        stack.extend((child, depth + 1) for child in element)
    view = root.get("viewBox", "").replace(",", " ").split()
    fallback = view[2:] if len(view) == 4 else ["300", "150"]
    dimensions = []
    for index, key in enumerate(("width", "height")):
        value = root.get(key, fallback[index])
        match = re.fullmatch(r"([0-9]+(?:\.[0-9]+)?)(px|mm|cm|in|pt|pc)?", value)
        if not match:
            raise Rejected("svg_viewport")
        factor = {None: 1, "px": 1, "mm": 96/25.4, "cm": 96/2.54, "in": 96, "pt": 96/72, "pc": 16}[match[2]]
        size = float(match[1]) * factor
        if not math.isfinite(size):
            raise Rejected("image_pixel_limit")
        dimensions.append(math.ceil(size))
    checked_size(*dimensions)
    # 重写已经验收的树，去除 XML 样式处理指令等未进入元素遍历的内容。
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def rasterize(source, kind, folder):
    if kind == "svgz" or kind == "svg" and source.startswith(b"\x1f\x8b"):
        # svg/svgz 使用同一可信 MIME；压缩签名在受限解码边界内决定。
        with gzip.GzipFile(fileobj=io.BytesIO(source)) as compressed:
            source = compressed.read(8 * 1024 * 1024 + 1)
        if len(source) > 8 * 1024 * 1024:
            raise Rejected("svg_limit")
        kind = "svg"
    if kind == "svg":
        source = safe_svg(source)
    original = folder / ("source." + kind); output = folder / "normalized.png"
    original.write_bytes(source)
    if kind == "jxl":
        command = ["/usr/bin/djxl", str(original), str(output), "--num_threads=2"]
    elif kind == "svg":
        command = ["/usr/bin/rsvg-convert", "--unlimited", "--output", str(output), str(original)]
        # 禁止 librsvg 的无界模式；尺寸先由渲染器自身限制，输出再受实际像素验收。
        command.remove("--unlimited")
    else:
        if kind == "wmf":
            offset = 22 if source.startswith(b"\xd7\xcd\xc6\x9a") else 0
            if len(source) < offset + 18:
                raise Rejected("format_mismatch")
            file_type, header_size, version = struct.unpack_from("<HHH", source, offset)
            if file_type not in (1, 2) or header_size != 9 or version not in (0x100, 0x300):
                raise Rejected("format_mismatch")
        elif len(source) < 88 or source[:4] != b"\x01\x00\x00\x00" or source[40:44] != b" EMF":
            raise Rejected("format_mismatch")
        # 两种 Windows 向量只交给受限 Draw；复用已核准字体，避免宿主安装 Office/COM。
        intermediate = folder / "source.svg"
        profile = folder / "lo-profile"
        result = subprocess.run(["/usr/bin/soffice", "--headless", "--nologo", "--nodefault", "--nofirststartwizard",
                                 "-env:UserInstallation=" + profile.as_uri(), "--convert-to", "svg:draw_svg_Export",
                                 "--outdir", str(folder), str(original)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                timeout=15, env={"PATH": "/usr/bin:/bin", "HOME": str(folder), "TMPDIR": str(folder)})
        if result.returncode or not intermediate.exists() or intermediate.stat().st_size > 8 * 1024 * 1024:
            raise Rejected("vector_rejected")
        intermediate.write_bytes(safe_svg(intermediate.read_bytes(), trusted_renderer=True))
        command = ["/usr/bin/rsvg-convert", "--output", str(output), str(intermediate)]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15,
                            env={"PATH": "/usr/bin:/bin", "MAGICK_CONFIGURE_PATH": "/app"})
    if result.returncode or not output.exists() or output.stat().st_size > 128 * 1024 * 1024:
        raise Rejected("image_decode_rejected")
    return output


def load_ocr():
    # 运行镜像必须预置全部权重；显式本地目录并关闭模型源检查，禁止运行时下载或外发。
    os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"
    from paddleocr import PaddleOCR
    return PaddleOCR(text_detection_model_name="PP-OCRv5_mobile_det", text_detection_model_dir="/opt/models/PP-OCRv5_mobile_det",
                     text_recognition_model_name="PP-OCRv5_mobile_rec", text_recognition_model_dir="/opt/models/PP-OCRv5_mobile_rec",
                     use_doc_orientation_classify=False, use_doc_unwarping=False, use_textline_orientation=False,
                     device="cpu", cpu_threads=2, enable_mkldnn=False)


def ruled_tables(rgb):
    """只定位完整矩形有线表格；不猜测无边框表格、合并单元格或语义关系。"""
    import cv2
    import numpy as np
    gray = cv2.cvtColor(np.asarray(rgb), cv2.COLOR_RGB2GRAY)
    ink = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY_INV | cv2.THRESH_OTSU)[1]
    horizontal = cv2.morphologyEx(ink, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_RECT, (max(20, rgb.width // 20), 1)))
    vertical = cv2.morphologyEx(ink, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_RECT, (1, max(20, rgb.height // 20))))
    connected = cv2.dilate(cv2.bitwise_or(horizontal, vertical), np.ones((3, 3), dtype=np.uint8))
    contours, _ = cv2.findContours(connected, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    tables = []
    def centers(values):
        groups = []
        for value in values:
            if not groups or value - groups[-1][-1] > 3:
                groups.append([int(value)])
            else:
                groups[-1].append(int(value))
        return [round(sum(group) / len(group)) for group in groups]
    for contour in contours:
        x, y, width, height = cv2.boundingRect(contour)
        if width < 80 or height < 40:
            continue
        columns = centers(np.flatnonzero((vertical[y:y+height, x:x+width] > 0).sum(axis=0) >= height * 0.8))
        rows = centers(np.flatnonzero((horizontal[y:y+height, x:x+width] > 0).sum(axis=1) >= width * 0.8))
        if not 3 <= len(columns) <= 65 or not 3 <= len(rows) <= 4097:
            continue
        columns = [x + value for value in columns]; rows = [y + value for value in rows]
        if any(right - left < 8 for left, right in zip(columns, columns[1:])) or any(bottom - top < 8 for top, bottom in zip(rows, rows[1:])):
            continue
        tables.append((rows, columns))
    if len(tables) > 128:
        raise Rejected("ocr_table_limit")
    return sorted(tables, key=lambda table: (table[0][0], table[1][0]))


def reading_order(items, width):
    """完整分栏有足够垂直重叠时按栏读取，否则维持原生从上至下的行顺序。"""
    if len(items) >= 6:
        ordered = sorted(items, key=lambda item: item[1])
        for left, right in zip(ordered, ordered[1:]):
            split = left[1] + left[3]["width"]
            if right[1] - split < width * 0.15:
                continue
            first = [item for item in items if item[1] + item[3]["width"] <= split]
            second = [item for item in items if item[1] >= right[1]]
            if len(first) < 3 or len(second) < 3 or len(first) + len(second) != len(items):
                continue
            a = (min(item[0] for item in first), max(item[0] + item[3]["height"] for item in first))
            b = (min(item[0] for item in second), max(item[0] + item[3]["height"] for item in second))
            if min(a[1], b[1]) - max(a[0], b[0]) >= 0.5 * min(a[1]-a[0], b[1]-b[0]):
                return sorted(first, key=lambda item: (item[0], item[1])) + sorted(second, key=lambda item: (item[0], item[1]))
    return sorted(items, key=lambda item: (item[0], item[1]))


def parse_image(source, kind, ocr=None):
    if kind not in FORMATS:
        raise Rejected("unsupported_image_format")
    if kind in ("pbm", "pgm", "ppm") and source[:2] not in {"pbm": (b"P1", b"P4"), "pgm": (b"P2", b"P5"), "ppm": (b"P3", b"P6")}[kind]:
        raise Rejected("format_mismatch")
    document = Document(source, 9)
    document.policy = policy(kind)
    from PIL import Image, ImageOps, ImageCms
    import numpy as np
    from pillow_heif import register_heif_opener
    register_heif_opener()
    Image.MAX_IMAGE_PIXELS = MAX_PIXELS
    seen = set(); pixels = 0
    with tempfile.TemporaryDirectory(prefix="fullnet-image-") as directory:
        file = io.BytesIO(source) if kind not in ("svg", "svgz", "wmf", "emf", "jxl") else rasterize(source, kind, Path(directory))
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            with Image.open(file) as image:
                if image.format != FORMATS[kind]:
                    raise Rejected("format_mismatch")
                frames = getattr(image, "n_frames", 1)
                if kind == "psd":
                    frames = 1  # PSD 明确只取原文件合成图，图层不冒充文档帧。
                if kind == "apng" and frames < 2 or not 1 <= frames <= MAX_FRAMES:
                    raise Rejected("image_frame_limit")
                for frame in range(frames):
                    if kind != "psd":
                        image.seek(frame)
                    original_width, original_height = image.size
                    checked_size(original_width, original_height)
                    pixels += original_width * original_height
                    if pixels > MAX_TOTAL_PIXELS:
                        raise Rejected("image_total_pixel_limit")
                    orientation = image.getexif().get(274, 1)
                    if not isinstance(orientation, int) or not 1 <= orientation <= 8:
                        raise Rejected("image_orientation")
                    normalized = ImageOps.exif_transpose(image).convert("RGBA")
                    icc = image.info.get("icc_profile")
                    if icc:
                        if len(icc) > 1024 * 1024:
                            raise Rejected("image_profile_limit")
                        alpha = normalized.getchannel("A")
                        normalized = ImageCms.profileToProfile(normalized.convert("RGB"),
                                     ImageCms.ImageCmsProfile(io.BytesIO(icc)), ImageCms.createProfile("sRGB"), outputMode="RGB").convert("RGBA")
                        normalized.putalpha(alpha)
                    rgb = Image.new("RGBA", normalized.size, "white")
                    rgb.alpha_composite(normalized); rgb = rgb.convert("RGB")
                    fingerprint = hashlib.sha256(b"image-rgb-exif-white-v1\0" + rgb.width.to_bytes(4, "big")
                                                 + rgb.height.to_bytes(4, "big") + rgb.tobytes()).digest()
                    if fingerprint in seen:
                        continue
                    seen.add(fingerprint)
                    if ocr is None:
                        ocr = load_ocr()
                    results = list(ocr.predict(np.asarray(rgb), text_rec_score_thresh=0.0))
                    if len(results) != 1:
                        raise Rejected("ocr_incomplete")
                    result = results[0].json
                    if isinstance(result, str):
                        result = json.loads(result)
                    data = result.get("res", result)
                    texts, scores, boxes = data["rec_texts"], data["rec_scores"], data["rec_polys"]
                    if len(texts) != len(scores) or len(texts) != len(boxes) or len(texts) > 8192:
                        raise Rejected("ocr_incomplete")
                    tables = ruled_tables(rgb)
                    items = []
                    for text, score, box in zip(texts, scores, boxes):
                        if not text.strip():
                            continue
                        if len(box) != 4 or any(len(p) != 2 or not all(math.isfinite(v) for v in p) for p in box) or not math.isfinite(score) or not 0 <= score <= 1:
                            raise Rejected("ocr_geometry")
                        x = max(0, math.floor(min(p[0] for p in box))); y = max(0, math.floor(min(p[1] for p in box)))
                        right = min(rgb.width, math.ceil(max(p[0] for p in box))); bottom = min(rgb.height, math.ceil(max(p[1] for p in box)))
                        if right <= x or bottom <= y:
                            raise Rejected("ocr_geometry")
                        location = dict(frameNumber=frame + 1, imageWidth=rgb.width, imageHeight=rgb.height,
                                        x=x, y=y, width=right-x, height=bottom-y, confidence=float(score), recognitionVersion=MODEL_VERSION,
                                        originalWidth=original_width, originalHeight=original_height, orientation=orientation,
                                        normalizationHash=fingerprint.hex(), normalizationVersion="image-rgb-exif-white-v1",
                                        tableNumber=None, rowNumber=None, columnNumber=None, parent=None)
                        for table_number, (rows, columns) in enumerate(tables, 1):
                            cx, cy = (x + right) / 2, (y + bottom) / 2
                            if not columns[0] <= cx <= columns[-1] or not rows[0] <= cy <= rows[-1]:
                                continue
                            row = next((n for n in range(len(rows)-1) if rows[n] <= cy < rows[n+1]), None)
                            column = next((n for n in range(len(columns)-1) if columns[n] <= cx < columns[n+1]), None)
                            if row is None or column is None or x < columns[column]-2 or right > columns[column+1]+2 or y < rows[row]-2 or bottom > rows[row+1]+2:
                                raise Rejected("ocr_table_geometry")
                            location.update(tableNumber=table_number, rowNumber=row+1, columnNumber=column+1)
                            break
                        items.append((y, x, text, location))
                    for _, _, text, location in reading_order(items, rgb.width):
                        document.add(text, "\n", position(11, text, image=location))
    document.image_pixels = pixels
    return document


def append_images(document, images):
    if not images:
        return
    ocr = load_ocr(); pixels = 0
    for raw, kind, parent in images:
        recognized = parse_image(raw, kind, ocr)
        pixels += recognized.image_pixels
        if pixels > MAX_TOTAL_PIXELS:
            raise Rejected("image_total_pixel_limit")
        anchor = dict(parent); anchor["length"] = 0
        for block in recognized.blocks:
            block["position"]["image"]["parent"] = anchor
            document.add(block["text"], block["separator"], block["position"])
