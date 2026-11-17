#!/usr/bin/env python3
"""从 Kubernetes 就绪 EndpointSlice 刷新网关上游。"""
import ipaddress
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile


TARGET = Path("/etc/nginx/conf.d/fullnet-upstream.conf")
REFRESH_LOCK = Path("/run/fullnet-edge-refresh.lock")
POD_NETWORK = ipaddress.IPv4Network("10.42.0.0/16")
LIST_ENDPOINTS = [
    "k3s", "kubectl", "get", "endpointslices", "-n", "fullnet-production",
    "-l", "kubernetes.io/service-name=fullnet-api-headless", "-o", "json",
]


def ready_addresses(payload):
    addresses = set()
    for item in payload["items"]:
        for endpoint in item.get("endpoints", []):
            conditions = endpoint.get("conditions") or {}
            if (conditions.get("ready") is not True or conditions.get("terminating") is True
                    or conditions.get("serving") is False):
                continue
            for value in endpoint["addresses"]:
                address = ipaddress.IPv4Address(value)
                if address not in POD_NETWORK:
                    raise ValueError("就绪端点超出网关允许的 Pod IPv4 网段。")
                addresses.add(str(address))
    return sorted(addresses)


def render_upstream(addresses):
    # NGINX 上游需保留有效成员定义，全部停用时代理直接结束请求。
    servers = "".join(f"  server {address}:8080 max_fails=2 fail_timeout=5s;\n" for address in addresses)
    if not servers:
        servers = "  server 127.0.0.1:1 down;\n"
    return ("# 根据就绪端点生成。\n"
            "upstream fullnet_api_upstream {\n  ip_hash;\n"
            f"{servers}  keepalive 32;\n}}\n")


def atomic_write(target, content, mode):
    descriptor, name = tempfile.mkstemp(dir=target.parent, prefix=f".{target.name}.", suffix=".tmp")
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(content)
        temporary.chmod(mode)
        os.replace(temporary, target)
    finally:
        temporary.unlink(missing_ok=True)


def refresh(target):
    # 先完成权威读取。成功返回的空端点集需要立即替换旧地址。
    addresses = ready_addresses(json.loads(subprocess.check_output(LIST_ENDPOINTS, timeout=15)))
    content = render_upstream(addresses).encode("utf-8")
    previous = target.read_bytes() if target.exists() else None
    if previous != content:
        mode = stat.S_IMODE(target.stat().st_mode) if previous is not None else 0o644
        atomic_write(target, content, mode)
        try:
            subprocess.run(["nginx", "-t"], check=True, timeout=15)
            subprocess.run(["systemctl", "reload-or-restart", "nginx"], check=True, timeout=15)
        except (OSError, subprocess.SubprocessError):
            if previous is None:
                target.unlink()
            else:
                atomic_write(target, previous, mode)
            raise
    return len(addresses)


def main():
    import fcntl

    with REFRESH_LOCK.open("a") as lock:
        # 定时器与手动刷新共用一个锁，配置回退始终对应本次读取。
        fcntl.flock(lock, fcntl.LOCK_EX)
        count = refresh(TARGET)
        print(f"网关就绪上游数量：{count}")


if __name__ == "__main__":
    main()
