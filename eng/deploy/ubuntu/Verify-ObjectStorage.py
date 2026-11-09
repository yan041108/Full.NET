#!/usr/bin/env python3
"""使用 SigV4 验证真实 S3 的建桶、版本控制与持久化读写；秘密仅从 root 文件读取。"""
import datetime
import hashlib
import hmac
import json
import pathlib
import ssl
import socket
import sys
import urllib.error
import urllib.parse
import urllib.request

endpoint = sys.argv[1].rstrip("/")
mode = sys.argv[2] if len(sys.argv) > 2 else "check"
root = pathlib.Path("/etc/fullnet")
if (root / "secrets/application.json").exists():
    settings = json.loads((root / "secrets/application.json").read_text())
    ca = root / "certificates/application-ca.pem"
else:
    identities = json.loads((root / "object-storage/object-storage.json").read_text())["identities"]
    application = next(item for item in identities if item["name"] == "fullnet")["credentials"][0]
    administrator = next(item for item in identities if item["name"] == "administrator")["credentials"][0]
    settings = {"s3AccessKey": application["accessKey"], "s3SecretKey": application["secretKey"],
                "s3AdminKey": administrator["accessKey"], "s3AdminSecret": administrator["secretKey"]}
    ca = root / "object-storage/application-ca.pem"
context = ssl.create_default_context(cafile=str(ca))
bucket = "/fullnet-files"


def request(method, path, content=b"", query="", administrator=False):
    access = settings["s3AdminKey" if administrator else "s3AccessKey"]
    secret = settings["s3AdminSecret" if administrator else "s3SecretKey"]
    now = datetime.datetime.now(datetime.timezone.utc)
    timestamp, date = now.strftime("%Y%m%dT%H%M%SZ"), now.strftime("%Y%m%d")
    host = urllib.parse.urlsplit(endpoint).netloc
    digest = hashlib.sha256(content).hexdigest()
    headers = {"Host": host, "x-amz-content-sha256": digest, "x-amz-date": timestamp}
    names = "host;x-amz-content-sha256;x-amz-date"
    canonical = f"{method}\n{path}\n{query}\nhost:{host}\nx-amz-content-sha256:{digest}\nx-amz-date:{timestamp}\n\n{names}\n{digest}"
    scope = f"{date}/us-east-1/s3/aws4_request"
    signable = f"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{hashlib.sha256(canonical.encode()).hexdigest()}"
    key = ("AWS4" + secret).encode()
    for element in [date, "us-east-1", "s3", "aws4_request"]:
        key = hmac.new(key, element.encode(), hashlib.sha256).digest()
    signature = hmac.new(key, signable.encode(), hashlib.sha256).hexdigest()
    headers["Authorization"] = f"AWS4-HMAC-SHA256 Credential={access}/{scope}, SignedHeaders={names}, Signature={signature}"
    url = endpoint + path + ("?" + query if query else "")
    with urllib.request.urlopen(urllib.request.Request(url, data=content if method == "PUT" else None, headers=headers, method=method), context=context, timeout=20) as response:
        return response.read()


if mode == "prepare":
    try:
        request("HEAD", bucket, administrator=True)
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        request("PUT", bucket, administrator=True)
    request("PUT", bucket, b'<VersioningConfiguration xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><Status>Enabled</Status></VersioningConfiguration>', "versioning=", True)
    request("PUT", bucket + "/.fullnet-persistence-probe", b"fullnet-object-storage-persistence-v1")
assert request("GET", bucket + "/.fullnet-persistence-probe") == b"fullnet-object-storage-persistence-v1"
assert b"Enabled" in request("GET", bucket, query="versioning=", administrator=True)
try:
    urllib.request.urlopen(endpoint + bucket, context=context, timeout=10)
    raise AssertionError("S3 不得允许匿名读取私有桶。")
except urllib.error.HTTPError as error:
    assert error.code == 403
if "isolated" not in sys.argv[3:]:
    with socket.socket() as connection:
        connection.settimeout(3)
        try:
            connection.connect((urllib.parse.urlsplit(endpoint).hostname, 18334))
            raise AssertionError("对象存储内部 gRPC 不应允许跨节点访问。")
        except OSError:
            pass
print("S3 TLS、签名、版本控制、持久化读写与匿名拒绝检查通过。"
      + ("跨节点管理端口已隔离。" if "isolated" not in sys.argv[3:] else "当前为独立网络恢复实例。"))
