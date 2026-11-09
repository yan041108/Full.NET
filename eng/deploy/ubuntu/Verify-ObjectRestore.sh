#!/usr/bin/env bash
# 在独立数据目录与独立网络命名空间恢复对象存储；原服务、原目录与原网络均不修改。
set -Eeuo pipefail
[[ $EUID == 0 ]] || exit 2
source "$(dirname -- "${BASH_SOURCE[0]}")/ObjectStorageCapacity.sh"
address=${1:?对象存储 IPv4}
python3 -c 'import ipaddress,sys; ipaddress.IPv4Address(sys.argv[1])' "$address"
backup=$(find /var/backups/fullnet -mindepth 2 -maxdepth 2 -name COMPLETE -printf '%h\n' | sort | tail -n 1)
[[ -f $backup/object-storage.tar.gz ]]
(cd "$backup"; sha256sum -c SHA256SUMS >/dev/null)
temporary=$(mktemp -d /var/tmp/fullnet-object-restore-XXXXXX)
unit="fullnet-object-restore-$$"
cleanup() {
  systemctl stop "$unit.service" >/dev/null 2>&1 || true
  [[ $(systemctl show "$unit.service" -p MainPID --value 2>/dev/null) == 0 ]] || return 1
  [[ $(realpath "$temporary") == /var/tmp/fullnet-object-restore-* ]] || return 1
  rm -rf -- "$temporary"
  systemctl reset-failed "$unit.service" >/dev/null 2>&1 || true
}
trap cleanup EXIT
python3 - "$backup/object-storage.tar.gz" "$temporary" <<'PY'
import sys,tarfile
with tarfile.open(sys.argv[1]) as archive:
    archive.extractall(sys.argv[2],filter='data')
PY
data="$temporary/srv/fullnet/object-storage"
[[ -d $data ]]
chown -R fullnet-storage:fullnet-storage "$temporary"
systemd-run --unit="$unit" --property=User=fullnet-storage --property=Group=fullnet-storage \
  --property=PrivateNetwork=yes --property=NoNewPrivileges=yes --property=MemoryMax=512M \
  --property="WorkingDirectory=$data" --property=TimeoutStopSec=90 \
  /usr/local/bin/weed server -ip=127.0.0.1 -ip.bind=127.0.0.1 -dir="$data" "${object_storage_capacity[@]}" -volume.fileSizeLimitMB=512 \
  -s3 -s3.ip.bind=0.0.0.0 -s3.port=8334 -s3.port.https=0 -s3.port.grpc=18334 \
  -s3.port.iceberg=0 -s3.port.lance=0 -s3.iam=false \
  -s3.cert.file=/etc/fullnet/object-storage/object.pem -s3.key.file=/etc/fullnet/object-storage/object.key \
  -s3.config=/etc/fullnet/object-storage/object-storage.json
[[ $(systemctl show "$unit.service" -p PrivateNetwork --value) == yes ]]
pid=$(systemctl show "$unit.service" -p MainPID --value)
[[ $pid -gt 0 ]]
nsenter -t "$pid" -n ip addr add "$address/32" dev lo
for attempt in {1..45}; do
  if nsenter -t "$pid" -n python3 /usr/local/lib/fullnet/Verify-ObjectStorage.py "https://$address:8334" check isolated > "$temporary/result.log" 2>&1; then
    date -u +%FT%TZ > "$backup/OBJECT-RESTORE-VERIFIED"
    echo '对象存储隔离恢复通过：备份桶版本控制、鉴权与原持久化探针均正确。'
    exit 0
  fi
  sleep 1
done
install -d -m 0700 /var/log/fullnet
cp "$temporary/result.log" /var/log/fullnet/object-restore-failed.log
echo '对象存储隔离恢复失败，最小记录已保存。' >&2
exit 1
