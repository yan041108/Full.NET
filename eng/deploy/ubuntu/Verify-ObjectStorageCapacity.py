#!/usr/bin/env python3
"""检查本机 SeaweedFS 的增长余量和磁盘保留量，供运维巡检使用。"""

import json
import shutil
import sys
import urllib.request

MASTER = "http://127.0.0.1:9333/dir/status"
STORAGE = "/srv/fullnet/object-storage"
MIN_GROWTH_SLOTS = 7
MIN_FREE_BYTES = 4 * 1024**3


def assess(topology, free_bytes):
    maximum, free = topology["Max"], topology["Free"]
    if type(maximum) is not int or type(free) is not int or not 0 <= free <= maximum or maximum == 0:
        raise ValueError("对象存储卷槽数据无效")
    if free == 0 or free_bytes <= MIN_FREE_BYTES:
        return "growth_blocked"
    if free < MIN_GROWTH_SLOTS:
        return "growth_at_risk"
    return "growth_headroom_available"


def main():
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(MASTER, timeout=3) as response:
            topology = json.loads(response.read(1024 * 1024))["Topology"]
        free_bytes = shutil.disk_usage(STORAGE).free
        state = assess(topology, free_bytes)
    except (OSError, ValueError, KeyError, TypeError):
        print(json.dumps({"state": "probe_unavailable"}), file=sys.stderr)
        return 2
    print(json.dumps({"state": state, "volumeSlots": topology["Max"], "freeSlots": topology["Free"], "freeBytes": free_bytes}))
    return 0 if state == "growth_headroom_available" else 1


if __name__ == "__main__":
    sys.exit(main())
