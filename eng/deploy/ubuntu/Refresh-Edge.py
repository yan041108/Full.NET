#!/usr/bin/env python3
"""从 Kubernetes 就绪 EndpointSlice 刷新网关上游。"""
import ipaddress
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile


TARGET = Path("/etc/nginx/conf.d/fullnet-upstream.conf")
REFRESH_LOCK = Path("/run/fullnet-edge-refresh.lock")
POD_NETWORK = ipaddress.IPv4Network("10.42.0.0/16")
LIST_ENDPOINTS = [
    "k3s", "kubectl", "get", "endpointslices", "-n", "fullnet-production",
    "-l", "kubernetes.io/service-name=fullnet-api-headless", "-o", "json",
]


class RefreshFailure(Exception):
    """只报告恢复阶段和命令确认，不把文件恢复或重载命令成功当作在线响应证明。"""

    def __init__(self, code, config_restored, rollback_reload_acknowledged=False):
        super().__init__(code)
        self.config_restored = config_restored
        self.rollback_reload_acknowledged = rollback_reload_acknowledged


def validate_nginx():
    subprocess.run(["nginx", "-t"], check=True, timeout=15, capture_output=True)


def reload_nginx():
    # 更新就绪路由只允许重载；服务停机时失败，不悄悄转为启动或重启。
    subprocess.run(["systemctl", "reload", "nginx"], check=True, timeout=15, capture_output=True)


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
    addresses = ready_addresses(json.loads(subprocess.check_output(LIST_ENDPOINTS, timeout=15, stderr=subprocess.PIPE)))
    content = render_upstream(addresses).encode("utf-8")
    previous = target.read_bytes() if target.exists() else None
    if previous != content:
        mode = stat.S_IMODE(target.stat().st_mode) if previous is not None else 0o644
        atomic_write(target, content, mode)
        reload_attempted = False
        try:
            validate_nginx()
            reload_attempted = True
            reload_nginx()
        except (OSError, subprocess.SubprocessError):
            try:
                if previous is None:
                    target.unlink()
                else:
                    atomic_write(target, previous, mode)
            except OSError:
                raise RefreshFailure("edge_rollback_file_failed", False) from None
            if reload_attempted:
                # 重载报错也可能已生效；回写文件后必须另行重载，不能留下磁盘/进程分歧。
                try:
                    validate_nginx()
                    reload_nginx()
                except (OSError, subprocess.SubprocessError):
                    raise RefreshFailure("edge_rollback_reload_failed", True) from None
            code = "edge_reload_failed" if reload_attempted else "edge_validation_failed"
            raise RefreshFailure(code, True, reload_attempted) from None
    return len(addresses)


def main():
    import fcntl

    try:
        with REFRESH_LOCK.open("a") as lock:
            # 定时器与手动刷新共用一个锁，配置回退始终对应本次读取。
            fcntl.flock(lock, fcntl.LOCK_EX)
            count = refresh(TARGET)
        print(f"网关就绪上游数量：{count}")
        return 0
    except RefreshFailure as error:
        report = {"status": "failed", "code": str(error), "configRestored": error.config_restored,
                  "rollbackReloadAcknowledged": error.rollback_reload_acknowledged}
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        report = {"status": "failed", "code": "edge_refresh_failed", "configRestored": None,
                  "rollbackReloadAcknowledged": False}
    print(json.dumps(report, separators=(",", ":")), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
