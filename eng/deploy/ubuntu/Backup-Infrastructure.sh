#!/usr/bin/env bash
# 备份与发布解耦；本脚本仅创建新备份，永不覆盖、恢复或初始化在线数据库。
set -Eeuo pipefail
umask 077
[[ $EUID == 0 ]] || exit 2
exec 9>/run/fullnet-backup.lock
flock -n 9 || { echo '已有备份正在执行。'; exit 1; }
source /etc/fullnet/infrastructure-installed
[[ $role == primary || $role == secondary ]] || { echo '基础设施角色无效。' >&2; exit 2; }
source /etc/fullnet/secrets/redis.env
root=/var/backups/fullnet
# 达到预算时停止并由巡检报告失败，避免备份无界占满业务磁盘。
[[ $(du -sm "$root" | cut -f1) -lt 5120 ]] || { echo '备份目录达到 5 GiB 预算，需归档处理。'; exit 1; }
[[ $(df -Pm "$root" | awk 'NR==2{print $4}') -gt 2048 ]] || { echo '可用空间不足 2 GiB。'; exit 1; }
destination=$(mktemp -d "$root/$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
printf '%s\n' "$role" > "$destination/ROLE"
object_stopped=false
restore_object_service() {
  if [[ $object_stopped == true ]]; then
    timeout 45s systemctl start fullnet-object-storage || return 1
    [[ $(systemctl show fullnet-object-storage -p ActiveState --value) == active ]] || return 1
    object_stopped=false
  fi
}
finish() {
  local result=$?
  trap - EXIT
  if ! restore_object_service; then
    echo '对象存储原运行状态未恢复，备份不能标记成功。' >&2
    result=1
  fi
  if [[ $result != 0 ]]; then echo "备份失败，保留现场：$destination" >&2; fi
  exit "$result"
}
trap finish EXIT
trap 'exit 143' TERM INT
if [[ $role == primary ]]; then
  # 禁止 --force：任何对象读取失败均使备份失败，不能发布部分转储为成功。
  mysqldump --protocol=socket -uroot --all-databases --single-transaction --routines --events --triggers --hex-blob --source-data=2 --set-gtid-purged=OFF | gzip > "$destination/mysql.sql.gz"
  gzip -t "$destination/mysql.sql.gz"
  # 快照对应位点由 --source-data=2 写入转储；结束后另查的当前位点不能代替它。
fi
export REDISCLI_AUTH=$REDIS_ADMIN_PASSWORD
redis-cli --user infra-admin --rdb "$destination/redis.rdb" > /dev/null
redis-check-rdb "$destination/redis.rdb" > "$destination/redis-check.txt"
unset REDISCLI_AUTH REDIS_APP_PASSWORD REDIS_ADMIN_PASSWORD
if [[ $role == secondary ]]; then
  [[ -d /srv/fullnet/object-storage ]] || { echo '对象存储数据目录缺失。' >&2; exit 1; }
  original_state=$(systemctl show fullnet-object-storage -p ActiveState --value)
  [[ $original_state == active || $original_state == inactive || $original_state == failed ]] || {
    echo '对象存储处于变化状态，不能取得停稳备份。' >&2; exit 1;
  }
  # 已停服的数据也必须归档；仅当原先运行时停服并恢复，不意外启动原本关闭的服务。
  if [[ $original_state == active ]]; then
    object_stopped=true
    systemctl stop fullnet-object-storage
    [[ $(systemctl show fullnet-object-storage -p ActiveState --value) == inactive ]]
  fi
  tar -czf "$destination/object-storage.tar.gz" /srv/fullnet/object-storage
  restore_object_service
fi
# 含凭据和证书的归档只允许 root 读取；异地传输必须使用加密通道与受限目标。
configuration=(/etc/fullnet /etc/redis /usr/local/lib/fullnet /etc/apt/sources.list.d/fullnet-* /etc/apt/preferences.d/fullnet-*)
while IFS= read -r unit; do configuration+=("$unit"); done < <(find /etc/systemd/system -maxdepth 1 -type f -name 'fullnet-*')
tar -czf "$destination/configuration.tar.gz" "${configuration[@]}"
if [[ $role == primary ]]; then
  tar -czf "$destination/mysql-configuration.tar.gz" /etc/mysql /var/lib/mysql/*.pem
  if [[ -d /srv/fullnet/data-protection ]]; then
    tar -czf "$destination/data-protection.tar.gz" /srv/fullnet/data-protection
  fi
  # SQLite 模式需要一致性备份；使用只读在线备份 API，不复制正在写入的裸文件。
  if [[ -f /var/lib/rancher/k3s/server/db/state.db ]]; then
    python3 - "$destination/k3s-state.db" <<'PY'
import sqlite3,sys
with sqlite3.connect('file:/var/lib/rancher/k3s/server/db/state.db?mode=ro',uri=True) as source:
    with sqlite3.connect(sys.argv[1]) as target:
        source.backup(target)
        if target.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise RuntimeError('K3s SQLite integrity check failed')
PY
    tar -czf "$destination/k3s-recovery.tar.gz" /etc/rancher/k3s /var/lib/rancher/k3s/server/token /var/lib/rancher/k3s/server/tls /var/lib/rancher/k3s/server/cred /etc/exports.d/fullnet.exports /etc/nfs.conf.d/fullnet.conf
  fi
fi
dpkg-query -W redis-server redis-tools > "$destination/packages.tsv"
if [[ $role == primary ]]; then dpkg-query -W mysql-community-server >> "$destination/packages.tsv"; fi
(cd "$destination"; find . -maxdepth 1 -type f ! -name SHA256SUMS ! -name COMPLETE -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
python3 /usr/local/lib/fullnet/Verify-InfrastructureBackup.py "$destination" --role "$role" --staging > /dev/null
touch "$destination/COMPLETE"
echo "备份完成：$destination"
# 自动任务不删历史备份；达到磁盘预算后应先确认异地副本，再按明确保留策略清理。
