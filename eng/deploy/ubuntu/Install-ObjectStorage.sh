#!/usr/bin/env bash
# 单节点 S3 服务位于集群外；数据目录固定，升级只替换经校验的二进制。
set -Eeuo pipefail
[[ $EUID == 0 ]] || exit 2
script_dir=$(dirname -- "${BASH_SOURCE[0]}")
source "$script_dir/ObjectStorageCapacity.sh"
peer=${1:?主节点 IPv4}
address=${3:-$(hostname -I | awk '{print $1}')}
python3 -c 'import ipaddress,sys; ipaddress.IPv4Address(sys.argv[1])' "$address"
[[ ! -e /srv/fullnet/object-storage && ! -e /etc/systemd/system/fullnet-object-storage.service ]] || { echo '已有对象存储，拒绝首次安装。'; exit 3; }
umask 077
temporary=$(mktemp -d /var/tmp/fullnet-weed-XXXXXX)
trap 'rm -rf -- "$temporary"' EXIT
if [[ -n ${2:-} ]]; then
  cp -- "$2" "$temporary/weed.tar.gz"
else
  curl -4 -fsSL --retry 3 --retry-all-errors --retry-delay 2 --connect-timeout 10 --max-time 180 https://github.com/seaweedfs/seaweedfs/releases/download/4.52/linux_amd64.tar.gz -o "$temporary/weed.tar.gz"
fi
echo '8b538c22d47d0c3fb6792adaa6b0c735a967401270a53427d5a476dcaca9b7ae  '"$temporary/weed.tar.gz" | sha256sum -c -
tar -xzf "$temporary/weed.tar.gz" -C "$temporary" weed
install -m 0755 "$temporary/weed" /usr/local/bin/weed
id fullnet-storage >/dev/null 2>&1 || useradd --system --home /srv/fullnet/object-storage --shell /usr/sbin/nologin fullnet-storage
install -d -m 0700 -o fullnet-storage -g fullnet-storage /srv/fullnet/object-storage
install -d -m 0750 -o root -g fullnet-storage /etc/fullnet/object-storage
tar -xzf /etc/fullnet/secrets/object-deployment.tar.gz -C /etc/fullnet/object-storage
chown root:fullnet-storage /etc/fullnet/object-storage/*
chmod 0640 /etc/fullnet/object-storage/*
printf 'OBJECT_BIND_ADDRESS=%s\n' "$address" > /etc/fullnet/object-storage/server.env
chmod 0644 /etc/fullnet/object-storage/server.env
cat > /etc/systemd/system/fullnet-object-storage.service <<EOF
[Unit]
Description=Full.NET S3 compatible persistent object storage
After=network-online.target
Wants=network-online.target
[Service]
User=fullnet-storage
Group=fullnet-storage
WorkingDirectory=/srv/fullnet/object-storage
EnvironmentFile=/etc/fullnet/object-storage/server.env
Environment=GOMEMLIMIT=768MiB
ExecStart=/usr/local/bin/weed server -ip=127.0.0.1 -ip.bind=127.0.0.1 -dir=/srv/fullnet/object-storage ${object_storage_capacity[*]} -volume.fileSizeLimitMB=512 -s3 -s3.ip.bind=\${OBJECT_BIND_ADDRESS} -s3.port=8334 -s3.port.https=0 -s3.port.grpc=18334 -s3.port.iceberg=0 -s3.port.lance=0 -s3.iam=false -s3.cert.file=/etc/fullnet/object-storage/object.pem -s3.key.file=/etc/fullnet/object-storage/object.key -s3.config=/etc/fullnet/object-storage/object-storage.json
Restart=on-failure
RestartSec=5
TimeoutStopSec=120
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/srv/fullnet/object-storage
MemoryMax=1G
LimitNOFILE=65536
[Install]
WantedBy=multi-user.target
EOF
chmod 0644 /etc/systemd/system/fullnet-object-storage.service
install -m 0755 "$script_dir/Verify-ObjectStorageCapacity.py" /usr/local/lib/fullnet/Verify-ObjectStorageCapacity.py
install -m 0644 "$script_dir/fullnet-object-capacity.service" "$script_dir/fullnet-object-capacity.timer" /etc/systemd/system/
ufw allow from "$peer" to any port 8334 proto tcp
ufw allow from 10.42.0.0/16 to any port 8334 proto tcp
# 内部 IAM gRPC 不允许从 Pod 或其他节点访问，避免绕过 S3 签名入口。
ufw insert 1 deny in to any port 18334 proto tcp
systemctl daemon-reload
systemctl enable --now fullnet-object-storage
systemctl enable --now fullnet-object-capacity.timer
weed version
systemctl is-active fullnet-object-storage
