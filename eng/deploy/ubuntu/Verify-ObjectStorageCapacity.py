#!/usr/bin/env python3
"""检查本机 SeaweedFS 的增长余量和磁盘保留量，供运维巡检使用。"""

import argparse
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
import urllib.request

MASTER = "http://127.0.0.1:9333/dir/status"
STORAGE = "/srv/fullnet/object-storage"
MIN_GROWTH_SLOTS = 7
MIN_FREE_BYTES = 4 * 1024**3
METRIC_PREFIX = "fullnet_object_storage_capacity"
GROWTH_STATE = {"growth_headroom_available": 0, "growth_at_risk": 1, "growth_blocked": 2}


def assess(topology, free_bytes):
    maximum, free = topology["Max"], topology["Free"]
    if type(maximum) is not int or type(free) is not int or not 0 <= free <= maximum or maximum == 0:
        raise ValueError("对象存储卷槽数据无效")
    if free == 0 or free_bytes <= MIN_FREE_BYTES:
        return "growth_blocked"
    if free < MIN_GROWTH_SLOTS:
        return "growth_at_risk"
    return "growth_headroom_available"


def collect():
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(MASTER, timeout=3) as response:
            topology = json.loads(response.read(1024 * 1024))["Topology"]
        free_bytes = shutil.disk_usage(STORAGE).free
        state = assess(topology, free_bytes)
    except (OSError, ValueError, KeyError, TypeError):
        return {"state": "probe_unavailable"}
    return {"state": state, "volumeSlots": topology["Max"], "freeSlots": topology["Free"], "freeBytes": free_bytes}


def publish_metrics(directory, result):
    state = GROWTH_STATE.get(result["state"])
    # 每次替换整份样本。巡检失败只发布失败与时间，避免继续展示旧容量为当前读数。
    metrics = {"sample_timestamp_seconds": int(time.time()), "probe_success": int(state is not None)}
    if state is not None:
        metrics.update(growth_state=state, free_slots=result["freeSlots"], free_bytes=result["freeBytes"])

    temporary_path = None
    try:
        # Node Exporter 只读取 .prom。临时文件放在同一目录，关闭后原子替换最终文件。
        with tempfile.NamedTemporaryFile(mode="w", encoding="ascii", dir=directory, suffix=".tmp", delete=False) as output:
            temporary_path = Path(output.name)
            for name, value in metrics.items():
                metric = f"{METRIC_PREFIX}_{name}"
                output.write(f"# TYPE {metric} gauge\n{metric} {value}\n")
        temporary_path.chmod(0o644)
        temporary_path.replace(directory / f"{METRIC_PREFIX}.prom")
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metrics-directory", type=Path, help="将巡检样本写入既有的 Node Exporter 文本采集目录。")
    options = parser.parse_args()
    result = collect()
    try:
        if options.metrics_directory is not None:
            publish_metrics(options.metrics_directory, result)
    except OSError:
        print(json.dumps({"state": "metrics_publish_failed"}), file=sys.stderr)
        return 2

    if result["state"] == "probe_unavailable":
        print(json.dumps(result), file=sys.stderr)
        return 2
    print(json.dumps(result))
    return 0 if result["state"] == "growth_headroom_available" else 1


if __name__ == "__main__":
    sys.exit(main())
