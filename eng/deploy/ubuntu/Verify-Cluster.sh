#!/usr/bin/env bash
# 保留两节点入口；授权来自现有 Kubernetes context，不要求提升本机权限。
set -Eeuo pipefail
exec python3 -I "$(dirname -- "${BASH_SOURCE[0]}")/Verify-Cluster.py" "$@"
