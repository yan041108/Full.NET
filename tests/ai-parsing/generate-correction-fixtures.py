"""仅生成 K02 的代表性单元格与 OCR 来源，不重复 F05 格式矩阵。"""
import hashlib
import json
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont

root = Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)
(root / "correction-table.csv").write_bytes("原始表格\n".encode("utf-8"))
image = Image.new("RGB", (700, 160), "white")
font = ImageFont.truetype("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 64)
ImageDraw.Draw(image).text((30, 25), "原文识别内容", font=font, fill="black")
image.save(root / "correction-ocr.png", format="PNG")
proof = [{"name": file.name, "bytes": file.stat().st_size, "sha256": hashlib.sha256(file.read_bytes()).hexdigest()}
         for file in [root / "correction-table.csv", root / "correction-ocr.png"]]
(root / "correction-fixtures.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2), encoding="utf-8")
