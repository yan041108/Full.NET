#!/usr/bin/env bash
# 将旧 unit 的容量设置替换为当前预设，输出候选 unit 供审核。
set -Eeuo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/ObjectStorageCapacity.sh"
unit=${1:-/etc/systemd/system/fullnet-object-storage.service}
python3 - "$unit" "${object_storage_capacity[@]}" <<'PY'
import pathlib
import re
import sys

unit = pathlib.Path(sys.argv[1]).read_text()
command = r"(?m)^ExecStart=/usr/local/bin/weed server [^\r\n]+$"
starts = list(re.finditer(r"(?m)^ExecStart=.*$", unit))
if len(starts) != 1 or not re.fullmatch(command, starts[0].group()):
    sys.exit("对象存储服务命令与已知预设不同，请人工审核。")

original = starts[0].group()
options = sys.argv[2:]
names = [option.split("=", 1)[0] for option in options]
pattern = r" (?:" + "|".join(map(re.escape, names)) + r")=[^ \t]+"
existing = re.findall(pattern, original)
if not (
    existing == [" -volume.max=5"]
    or existing == [" " + option for option in options]
):
    sys.exit("现有容量设置与旧版或当前预设不同，请人工审核。")
data_argument = " -dir=/srv/fullnet/object-storage "
if original.count(data_argument) != 1:
    sys.exit("对象存储数据目录与已知预设不同，请人工审核。")
replacement = re.sub(pattern, "", original).replace(data_argument, data_argument + " ".join(options) + " ", 1)
sys.stdout.write(unit[: starts[0].start()] + replacement + unit[starts[0].end() :])
PY
