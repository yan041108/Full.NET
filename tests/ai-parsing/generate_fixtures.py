"""自行生成可再分发测试来源；不从互联网获取样本或个人文档。"""
import gzip
import io
from pathlib import Path
import struct
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont
from pillow_heif import register_heif_opener

root = Path(sys.argv[1]); root.mkdir(parents=True, exist_ok=True)
register_heif_opener()
font_path = "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"
font = ImageFont.truetype(font_path, 46)
image = Image.new("RGB", (1000, 220), "white")
draw = ImageDraw.Draw(image)
draw.text((30, 25), "知识库解析测试", font=font, fill="black")
draw.text((30, 110), "Full.NET document 2026", font=font, fill="black")
for extension, format_name in {"png": "PNG", "jpg": "JPEG", "jfif": "JPEG", "webp": "WEBP", "bmp": "BMP",
                               "tif": "TIFF", "gif": "GIF", "tga": "TGA", "pcx": "PCX", "pnm": "PPM",
                               "ppm": "PPM", "jp2": "JPEG2000", "heic": "HEIF", "heif": "HEIF", "avif": "AVIF"}.items():
    image.save(root / ("source." + extension), format=format_name)
image.convert("L").save(root / "source.pgm", format="PPM")
image.convert("1").save(root / "source.pbm", format="PPM")
image.save(root / "source.j2k", format="JPEG2000", no_jp2=True)
image.save(root / "scanned.pdf", format="PDF", resolution=144)
image.rotate(8, expand=True, fillcolor="white").save(root / "tilt.png")
rotated = image.transpose(Image.Transpose.ROTATE_90); exif = Image.Exif(); exif[274] = 6
rotated.save(root / "orientation.jpg", exif=exif, quality=95)
second = image.copy(); ImageDraw.Draw(second).text((700, 25), "第二帧", font=font, fill="black")
for kind in ("gif", "tif", "apng"):
    image.save(root / ("frames." + kind), format={"gif": "GIF", "tif": "TIFF", "apng": "PNG"}[kind], save_all=True, append_images=[second], duration=100, loop=0)
small = Image.new("RGB", (256, 256), "white")
ImageDraw.Draw(small).text((24, 70), "知识库", font=ImageFont.truetype(font_path, 54), fill="black")
small.save(root / "source.ico", format="ICO", sizes=[(256, 256)])
# PSD 使用原始合成 RGB 平面，图层保持为空，避免依赖商业编辑器。
planes = b"".join(channel.tobytes() for channel in image.split())
(root / "source.psd").write_bytes(b"8BPS" + struct.pack(">H6sHIIHH", 1, b"\0"*6, 3, image.height, image.width, 8, 3) + b"\0"*14 + planes)
svg = '<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="220"><rect width="1000" height="220" fill="white"/><text x="30" y="80" font-family="Noto Sans CJK SC" font-size="46">知识库解析测试</text><text x="30" y="165" font-family="Noto Sans CJK SC" font-size="46">Full.NET document 2026</text></svg>'
(root / "source.svg").write_text(svg, encoding="utf-8")
(root / "source.svgz").write_bytes(gzip.compress(svg.encode()))
subprocess.run(["/usr/bin/cjxl", str(root / "source.png"), str(root / "source.jxl"), "--num_threads=2"], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
# 有线表格与多栏使用同一文字识别模型，期待位置来自实际图像而非样本声明。
table = Image.new("RGB", (1000, 400), "white"); painter = ImageDraw.Draw(table)
for x in (20, 500, 980): painter.line((x, 20, x, 380), fill="black", width=3)
for y in (20, 200, 380): painter.line((20, y, 980, y), fill="black", width=3)
for x, y, text in [(50,60,"项目名称"),(530,60,"项目编号"),(50,240,"知识库"),(530,240,"2026")]: painter.text((x,y), text, font=font, fill="black")
table.save(root / "table.png")
(root / "source.csv").write_text('标题,内容\r\n"知识库","第一行\r\n第二行"\r\n2026,完成\r\n', encoding="utf-8")
(root / "gb18030.csv").write_bytes('标题;内容\r\n知识库;中文正文\r\n'.encode("gb18030"))
(root / "source.html").write_text('<!DOCTYPE html><html><head><title>知识库标题</title></head><body><h1>多格式知识库</h1><p>正文😀</p><table><tr><td>数据</td><td>2026</td></tr></table><script>ignored()</script></body></html>', encoding="utf-8")

def pdf(objects, target):
    raw = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n"); offsets = [0]
    for index, content in enumerate(objects, 1):
        offsets.append(len(raw)); raw.extend(str(index).encode()+b" 0 obj\n"+content+b"\nendobj\n")
    offset = len(raw); raw.extend(f"xref\n0 {len(offsets)}\n0000000000 65535 f \n".encode())
    for value in offsets[1:]: raw.extend(f"{value:010} 00000 n \n".encode())
    raw.extend(f"trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{offset}\n%%EOF\n".encode()); target.write_bytes(raw)
text = b"BT /F1 24 Tf 30 350 Td (Native PDF heading) Tj ET\n"
base = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>"]
font_object = b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
pdf(base+[b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 400] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",font_object,f"<< /Length {len(text)} >>\nstream\n".encode()+text+b"endstream"],root/"native.pdf")
photo = io.BytesIO(); image.save(photo,format="JPEG",quality=95); photo=photo.getvalue(); content=text+b"q 500 0 0 110 30 100 cm /Im1 Do Q\n"
pdf(base+[b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 400] /Resources << /Font << /F1 4 0 R >> /XObject << /Im1 6 0 R >> >> /Contents 5 0 R >>",font_object,f"<< /Length {len(content)} >>\nstream\n".encode()+content+b"endstream",f"<< /Type /XObject /Subtype /Image /Width 1000 /Height 220 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {len(photo)} >>\nstream\n".encode()+photo+b"\nendstream"],root/"mixed.pdf")
print("raster-vector-text-pdf-fixtures-created")
