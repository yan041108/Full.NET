"""构建时核对完整固定资产；运行入口只使用镜像内的离线资源。"""
import hashlib
import importlib.metadata
import json
from pathlib import Path
java = json.loads(Path('/app/assets/java-assets.lock.json').read_text())
actual = {p.name: p for p in Path('/opt/java-libs').glob('*.jar')}
if set(actual) != {item['file'] for item in java}:
    raise RuntimeError('java_asset_set_mismatch')
for item in java:
    if hashlib.sha256(actual[item['file']].read_bytes()).hexdigest() != item['sha256']:
        raise RuntimeError('java_asset_digest_mismatch')
for item in json.loads(Path('/app/assets/model-assets.lock.json').read_text()):
    if hashlib.sha256((Path('/opt/models') / item['path']).read_bytes()).hexdigest() != item['sha256']:
        raise RuntimeError('model_asset_digest_mismatch')
expected = {}
for line in Path('/app/assets/requirements.lock').read_text().splitlines():
    name, version = line.split(' ', 1)[0].split('==')
    expected[name.lower().replace('_', '-')] = version
actual_python = {d.metadata['Name'].lower().replace('_', '-'): d.version for d in importlib.metadata.distributions()}
if expected != actual_python:
    raise RuntimeError('python_asset_set_mismatch')
if importlib.metadata.version('paddleocr') != '3.7.0':
    raise RuntimeError('ocr_runtime_mismatch')
print('parser-assets-verified')
