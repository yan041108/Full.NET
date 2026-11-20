#!/usr/bin/env bash
# 复用受控证书接收器；只同步证书文件，不把安装结果当作服务已加载。
set -Eeuo pipefail
exec python3 -I "$(dirname -- "${BASH_SOURCE[0]}")/Receive-ObjectCertificate.py" "$@"
