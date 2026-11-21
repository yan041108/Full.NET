{{/* 所有组件默认禁用服务账号令牌；只有只读 Deployment 观测器允许挂载。 */}}
{{- define "fullnet.monitor.deployment" -}}
{{- $container := dict "name" .name "image" .image "args" .args "ports" .ports "volumeMounts" .mounts "resources" (dict "requests" (dict "cpu" "100m" "memory" "128Mi") "limits" (dict "cpu" "1" "memory" "1Gi")) "securityContext" (dict "allowPrivilegeEscalation" false "readOnlyRootFilesystem" true "capabilities" (dict "drop" (list "ALL"))) -}}
{{- $path := "/-/ready" -}}{{- $port := 9090 -}}
{{- if eq .name "alertmanager" -}}{{- $port = 9093 -}}{{- end -}}
{{- if eq .name "telemetry-collector" -}}{{- $port = 13133 -}}{{- $path = "/" -}}{{- end -}}
{{- if eq .name "kube-state-metrics" -}}{{- $port = 8080 -}}{{- $path = "/metrics" -}}{{- end -}}
{{- $http := dict "path" $path "port" $port -}}
{{- $_ := set $container "startupProbe" (dict "httpGet" $http "periodSeconds" 5 "timeoutSeconds" 3 "failureThreshold" 24) -}}
{{- $_ := set $container "readinessProbe" (dict "httpGet" $http "periodSeconds" 10 "timeoutSeconds" 3 "failureThreshold" 3) -}}
{{- $labels := dict "app.kubernetes.io/part-of" "fullnet-observability" "app.kubernetes.io/name" .name -}}
{{- $pod := dict "automountServiceAccountToken" false "securityContext" (dict "runAsNonRoot" true "runAsUser" .uid "runAsGroup" .uid "fsGroup" .uid "seccompProfile" (dict "type" "RuntimeDefault")) "containers" (list $container) "volumes" .volumes -}}
{{- $_ := set $pod "nodeSelector" (dict "kubernetes.io/os" "linux") -}}
{{- if eq .name "kube-state-metrics" -}}
{{- $_ := set $pod "serviceAccountName" "fullnet-metrics-reader" -}}
{{- $_ := set $pod "automountServiceAccountToken" true -}}
{{- end -}}
{{- toJson (dict "apiVersion" "apps/v1" "kind" "Deployment" "metadata" (dict "name" .name "namespace" .namespace "labels" $labels) "spec" (dict "replicas" 1 "strategy" (dict "type" "Recreate") "selector" (dict "matchLabels" $labels) "template" (dict "metadata" (dict "labels" $labels "annotations" (dict "fullnet.io/config-sha256" .checksum)) "spec" $pod))) -}}
{{- end -}}
