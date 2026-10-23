"""构建时只下载锁定发布者资产，运行容器不会调用此脚本。"""
import hashlib
import json
from pathlib import Path
import urllib.request
for asset in json.loads(Path("model-assets.lock.json").read_text()):
    with urllib.request.urlopen(asset["url"], timeout=60) as response:
        data = response.read(40 * 1024 * 1024 + 1)
    if len(data) > 40 * 1024 * 1024 or hashlib.sha256(data).hexdigest() != asset["sha256"]:
        raise RuntimeError("asset_digest_mismatch")
    target = Path("/opt/models") / asset["path"]
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
