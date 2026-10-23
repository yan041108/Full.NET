"""只进入验收专用派生镜像，验证实际 Docker 故障与资源边界；不随生产镜像发布。"""
import os
from pathlib import Path
import socket
import sys
import time

mode = sys.stdin.buffer.read().decode("ascii")
if mode == "isolation":
    assert os.getuid() == 65532
    try:
        Path("/app/forbidden").write_text("bad")
        raise AssertionError("root filesystem is writable")
    except OSError:
        pass
    try:
        socket.create_connection(("1.1.1.1", 443), timeout=1)
        raise AssertionError("network is reachable")
    except OSError:
        pass
    sys.stdout.buffer.write(b"isolation-verified")
elif mode == "stdout":
    for _ in range(18):
        sys.stdout.buffer.write(b"x" * (1024 * 1024))
        sys.stdout.buffer.flush()
elif mode == "stderr":
    sys.stderr.write("x" * 8192)
    sys.stderr.flush()
    time.sleep(5)
elif mode in ("timeout", "cancel"):
    time.sleep(60)
elif mode == "oom":
    # 分块实际触碰内存，必须由 3 GiB cgroup 拒绝，不能把虚拟地址预留冒充 OOM。
    allocations = []
    for _ in range(64):
        allocations.append(bytearray(64 * 1024 * 1024))
    raise AssertionError("memory limit was not enforced")
else:
    raise AssertionError("unknown probe")
