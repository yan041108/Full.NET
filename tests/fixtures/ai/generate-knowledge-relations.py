"""生成自有 DOCX 嵌入图片样本；实际生产解析器负责 OCR 和父锚点证明。"""
import hashlib
import io
import json
from pathlib import Path
import sys
import zipfile
from PIL import Image, ImageDraw, ImageFont

output = Path(sys.argv[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
image = Image.new("RGB", (1800, 280), "white")
ImageDraw.Draw(image).text((35, 80), "FULLNET CHILD CONTEXT", fill="black", font=ImageFont.truetype("DejaVuSans.ttf", 80))
png = io.BytesIO()
image.save(png, format="PNG")
parts = {
    "[Content_Types].xml": '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>',
    "_rels/.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
    "word/document.xml": '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p><w:r><w:t>PARENT ORIGINAL CONTEXT</w:t></w:r><w:r><w:drawing><a:blip r:embed="image1"/></w:drawing></w:r></w:p><w:p><w:r><w:t>UNRELATED ORIGINAL PARAGRAPH</w:t></w:r></w:p></w:body></w:document>',
    "word/_rels/document.xml.rels": '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="image1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image.png"/></Relationships>',
    "word/media/image.png": png.getvalue(),
}
buffer = io.BytesIO()
with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED) as package:
    for name, data in parts.items():
        entry = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_DEFLATED
        package.writestr(entry, data)
payload = buffer.getvalue()
target = output / "correction-parent-child.docx"
# 验收输入可重复生成，但不静默覆盖其他会话的不同文件。
if target.exists() and target.read_bytes() != payload:
    raise RuntimeError("Existing relation fixture differs")
if not target.exists():
    target.write_bytes(payload)
print(json.dumps({"source": target.name, "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest(), "query": "FULLNET"}))
