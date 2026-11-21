#!/usr/bin/env bash
# 仅渲染平台清单，规则和 Collector 从现有权威文件注入；不安装、不发送通知。
set -Eeuo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
exec helm template fullnet-observability "$root/deploy/helm/fullnet-observability" \
  --namespace observability "$@" \
  --set-file "rulesSource=$root/deploy/observability/prometheus-rules.yaml" \
  --set-file "collectorSource=$root/deploy/observability/otel-collector-values.yaml"
