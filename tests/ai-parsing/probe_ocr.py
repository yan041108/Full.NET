"""选型实验：离线加载固定模型并识别自行绘制的中英文样本，不执行付费模型调用。"""
import json
import os
import time
os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"
os.environ["OMP_NUM_THREADS"] = "2"
from PIL import Image, ImageDraw, ImageFont
import numpy as np
from paddleocr import PaddleOCR

started = time.monotonic()
image = Image.new("RGB", (1000, 220), "white")
draw = ImageDraw.Draw(image)
font = ImageFont.truetype("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 46)
draw.text((30, 25), "知识库解析测试", font=font, fill="black")
draw.text((30, 110), "Full.NET document 2026", font=font, fill="black")
ocr = PaddleOCR(text_detection_model_name="PP-OCRv5_mobile_det", text_detection_model_dir="/selection/PP-OCRv5_mobile_det",
                text_recognition_model_name="PP-OCRv5_mobile_rec", text_recognition_model_dir="/selection/PP-OCRv5_mobile_rec",
                use_doc_orientation_classify=False, use_doc_unwarping=False, use_textline_orientation=False,
                device="cpu", cpu_threads=2, enable_mkldnn=False)
results = list(ocr.predict(np.asarray(image), text_rec_score_thresh=0.0))
assert len(results) == 1
value = results[0].json
if isinstance(value, str):
    value = json.loads(value)
data = value.get("res", value)
print(json.dumps({"seconds": time.monotonic() - started, "texts": data.get("rec_texts"),
                  "scores": data.get("rec_scores"), "boxes": data.get("rec_polys"), "keys": list(data)}, ensure_ascii=False))
assert "知识库解析测试" in data["rec_texts"]
assert "Full.NET document 2026" in data["rec_texts"]
