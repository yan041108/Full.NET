#!/usr/bin/env bash
# 只恢复明确选择的 secondary 备份；在线服务、目录与网络均不修改。
set -Eeuo pipefail
umask 077
[[ $EUID == 0 && $# == 2 ]] || { echo '用法：Verify-ObjectRestore.sh IPv4 BACKUP_DIRECTORY（需要 root）' >&2; exit 2; }
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$script_directory/ObjectStorageCapacity.sh"
address=$1
python3 -I -c 'import ipaddress,sys; ipaddress.IPv4Address(sys.argv[1])' "$address" 2>/dev/null || exit 2
[[ ! -L $2 ]] || exit 2
backup=$(realpath -e -- "$2")
[[ $backup == /var/backups/fullnet/* && -d $backup ]] || exit 2
marker="$backup/OBJECT-RESTORE-VERIFIED"
[[ ! -e $marker && ! -L $marker ]] || { echo '已有历史恢复标记，请先保留原证据并归档；本次拒绝覆盖。' >&2; exit 3; }
exec 9>/run/fullnet-backup.lock
flock -sn 9 || { echo '备份正在生成，拒绝恢复。' >&2; exit 1; }
exec 8>/run/fullnet-object-restore.lock
flock -n 8 || { echo '已有对象恢复正在执行。' >&2; exit 1; }
timeout 60s python3 -I "$script_directory/Verify-InfrastructureBackup.py" "$backup" --role secondary >/dev/null
manifest_digest=$(sha256sum "$backup/SHA256SUMS" | cut -d' ' -f1)
archive_digest=$(sha256sum "$backup/object-storage.tar.gz" | cut -d' ' -f1)
started=$(date -u +%FT%TZ)
temporary=$(mktemp -d /var/tmp/fullnet-object-restore-XXXXXX)
unit="fullnet-object-restore-${temporary##*-}"
unit_started=false
cleanup() {
  if [[ $unit_started == true ]]; then
    timeout 100s systemctl stop "$unit.service" >/dev/null 2>&1 || return 1
    [[ $(timeout 10s systemctl show "$unit.service" -p MainPID --value 2>/dev/null) == 0 ]] || return 1
    unit_started=false
  fi
  if [[ -d $temporary ]]; then
    [[ ! -L $temporary && $(realpath -- "$temporary") == /var/tmp/fullnet-object-restore-* ]] || return 1
    rm -rf -- "$temporary" || return 1
    [[ ! -e $temporary ]] || return 1
  fi
}
finish() {
  result=$?
  trap - EXIT
  diagnostic="$temporary/result.log"
  [[ -f $diagnostic ]] || diagnostic="$temporary/start.log"
  if [[ $result != 0 && -f $diagnostic ]]; then
    # 失败日志私有且有界；不能将含认证上下文的原始错误回显到终端。
    if ! { install -d -m 0700 /var/log/fullnet &&
           tail -c 65536 "$diagnostic" > "/var/log/fullnet/$unit.log" &&
           chmod 0600 "/var/log/fullnet/$unit.log"; }; then
      echo '最小诊断保存失败，仍继续停止恢复实例。' >&2
      result=1
    fi
  fi
  if ! cleanup; then
    echo "隔离恢复清理失败；保留运行目录供核对：$unit" >&2
    result=1
  fi
  exit "$result"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
cp --reflink=auto -- "$backup/object-storage.tar.gz" "$temporary/object-storage.tar.gz"
[[ $(sha256sum "$temporary/object-storage.tar.gz" | cut -d' ' -f1) == "$archive_digest" ]]
mkdir -m 0700 "$temporary/restore"
# 仅解出固定数据根的普通文件和目录，完整预检后才写盘，不接受链接或设备。
timeout 300s python3 -I - "$temporary/object-storage.tar.gz" "$temporary/restore" <<'PY'
import pathlib, shutil, sys, tarfile
root = pathlib.Path(sys.argv[2])
with tarfile.open(sys.argv[1]) as archive:
    members, names, size = [], set(), 0
    for member in archive:
        path = pathlib.PurePosixPath(member.name)
        if (path.is_absolute() or ".." in path.parts or "\\" in member.name
                or any(ord(c) < 32 for c in member.name)
                or member.size < 0 or str(path) in names or member.type not in (tarfile.REGTYPE, tarfile.AREGTYPE, tarfile.DIRTYPE)
                or not (str(path) == "srv/fullnet/object-storage" or str(path).startswith("srv/fullnet/object-storage/"))):
            raise SystemExit("对象归档路径或类型无效")
        names.add(str(path))
        size += member.size
        members.append(member)
        if len(members) > 100000:
            raise SystemExit("对象归档成员过多")
    if not members or not any(member.isfile() for member in members):
        raise SystemExit("对象归档缺少数据文件")
    if size + 64 * 1024 * 1024 > shutil.disk_usage(root).free:
        raise SystemExit("对象恢复空间不足")
    # 先拒绝父路径与文件碰撞，防止部分解出后才发现结构非法。
    files = {str(pathlib.PurePosixPath(member.name)) for member in members if member.isfile()}
    if any(str(parent) in files for name in names for parent in pathlib.PurePosixPath(name).parents):
        raise SystemExit("对象归档文件目录冲突")
    archive.extractall(root, members=members, filter="data")
PY
data="$temporary/restore/srv/fullnet/object-storage"
[[ -d $data ]]
chown root:fullnet-storage "$temporary"
chmod 0710 "$temporary"
chown -R fullnet-storage:fullnet-storage "$temporary/restore"
# 标记启动意图早于命令调用，启动响应丢失也必须尝试清理本轮独立 unit。
unit_started=true
(ulimit -f 64; exec timeout 30s systemd-run --unit="$unit" --property=User=fullnet-storage --property=Group=fullnet-storage \
  --property=PrivateNetwork=yes --property=NoNewPrivileges=yes --property=MemoryMax=512M \
  --property="WorkingDirectory=$data" --property=TimeoutStopSec=90 --property=RuntimeMaxSec=300 \
  /usr/local/bin/weed server -ip=127.0.0.1 -ip.bind=127.0.0.1 -dir="$data" "${object_storage_capacity[@]}" -volume.fileSizeLimitMB=512 \
  -s3 -s3.ip.bind=0.0.0.0 -s3.port=8334 -s3.port.https=0 -s3.port.grpc=18334 \
  -s3.port.iceberg=0 -s3.port.lance=0 -s3.iam=false \
  -s3.cert.file=/etc/fullnet/object-storage/object.pem -s3.key.file=/etc/fullnet/object-storage/object.key \
  -s3.config=/etc/fullnet/object-storage/object-storage.json) >"$temporary/start.log" 2>&1
[[ $(timeout 10s systemctl show "$unit.service" -p PrivateNetwork --value) == yes ]]
[[ $(timeout 10s systemctl show "$unit.service" -p WorkingDirectory --value) == "$data" ]]
pid=$(timeout 10s systemctl show "$unit.service" -p MainPID --value)
[[ $pid =~ ^[1-9][0-9]*$ ]]
timeout 10s nsenter -t "$pid" -n ip addr add "$address/32" dev lo
verified=false
deadline=$((SECONDS + 180))
for attempt in {1..45}; do
  remaining=$((deadline - SECONDS))
  [[ $remaining -gt 0 ]] || break
  # -I 忽略 PYTHONOPTIMIZE 等外部环境，保留既有对象协议探针的断言门禁。
  if (ulimit -f 64; exec timeout --signal=TERM --kill-after=5s "${remaining}s" nsenter -t "$pid" -n \
      python3 -I /usr/local/lib/fullnet/Verify-ObjectStorage.py "https://$address:8334" check isolated) >"$temporary/result.log" 2>&1; then
    verified=true
    break
  fi
  sleep 1
done
[[ $verified == true ]] || { echo '对象存储隔离恢复检查失败。' >&2; exit 1; }
timeout 60s python3 -I "$script_directory/Verify-InfrastructureBackup.py" "$backup" --role secondary >/dev/null
[[ $(sha256sum "$backup/SHA256SUMS" | cut -d' ' -f1) == "$manifest_digest" ]]
# 成功回执必须晚于服务停止、MainPID=0 与目录清理；任何一步失败都不写标记。
cleanup
python3 -I - "$marker" "$manifest_digest" "$archive_digest" "$started" <<'PY'
import datetime, json, os, pathlib, sys, tempfile
target = pathlib.Path(sys.argv[1])
content = {"schemaVersion": 1, "kind": "object-storage-isolated-restore",
           "backupManifestSha256": sys.argv[2], "objectArchiveSha256": sys.argv[3],
           "startedAtUtc": sys.argv[4], "completedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
           "versioningAuthAndProbeVerified": True, "serviceStopped": True, "temporaryDataRemoved": True}
descriptor, name = tempfile.mkstemp(prefix=".object-restore-receipt-", dir=target.parent)
try:
    with os.fdopen(descriptor, "w") as stream:
        json.dump(content, stream, sort_keys=True)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.link(name, target)
finally:
    pathlib.Path(name).unlink(missing_ok=True)
PY
echo '对象存储隔离恢复通过；恢复实例与临时数据均已清理，备份绑定回执已保存。'
