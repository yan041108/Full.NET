{{/* 节点权限与单命名空间 DaemonSet 权限分别交给独立账号，默认均不创建。 */}}
{{- define "fullnet.monitor.infrastructureState" -}}
{{- $items := list -}}{{- $definitions := list -}}{{- $scrapes := list -}}
{{- $ns := .namespace -}}
{{- $readers := list -}}
{{- if .values.nodeState.enabled -}}
{{- $account := "fullnet-metrics-node-reader" -}}
{{/* ClusterRole/Binding 带平台命名空间，避免不同平台实例的集群级名字互相覆盖。 */}}
{{- $role := printf "%s-fullnet-metrics-nodes" $ns -}}
{{- $items = append $items (dict "apiVersion" "v1" "kind" "ServiceAccount" "metadata" (dict "name" $account "namespace" $ns)) -}}
{{- $items = append $items (dict "apiVersion" "rbac.authorization.k8s.io/v1" "kind" "ClusterRole" "metadata" (dict "name" $role) "rules" (list (dict "apiGroups" (list "") "resources" (list "nodes") "verbs" (list "get" "list" "watch")))) -}}
{{- $items = append $items (dict "apiVersion" "rbac.authorization.k8s.io/v1" "kind" "ClusterRoleBinding" "metadata" (dict "name" $role) "subjects" (list (dict "kind" "ServiceAccount" "name" $account "namespace" $ns)) "roleRef" (dict "apiGroup" "rbac.authorization.k8s.io" "kind" "ClusterRole" "name" $role)) -}}
{{- $readers = append $readers (dict "name" "kube-state-nodes" "account" $account "args" (list "--resources=nodes" "--metric-allowlist=kube_node_status_condition")) -}}
{{- end -}}
{{- if .values.daemonSetNamespace -}}
{{- $account := "fullnet-metrics-daemonset-reader" -}}
{{- $items = append $items (dict "apiVersion" "v1" "kind" "ServiceAccount" "metadata" (dict "name" $account "namespace" $ns)) -}}
{{- $items = append $items (dict "apiVersion" "rbac.authorization.k8s.io/v1" "kind" "Role" "metadata" (dict "name" $account "namespace" .values.daemonSetNamespace) "rules" (list (dict "apiGroups" (list "apps") "resources" (list "daemonsets") "verbs" (list "get" "list" "watch")))) -}}
{{- $items = append $items (dict "apiVersion" "rbac.authorization.k8s.io/v1" "kind" "RoleBinding" "metadata" (dict "name" $account "namespace" .values.daemonSetNamespace) "subjects" (list (dict "kind" "ServiceAccount" "name" $account "namespace" $ns)) "roleRef" (dict "apiGroup" "rbac.authorization.k8s.io" "kind" "Role" "name" $account)) -}}
{{- $readers = append $readers (dict "name" "kube-state-daemonsets" "account" $account "args" (list "--resources=daemonsets" (printf "--namespaces=%s" .values.daemonSetNamespace) "--metric-allowlist=kube_daemonset_status_number_unavailable")) -}}
{{- end -}}
{{- range $readers -}}
{{- $definitions = append $definitions (dict "name" .name "image" $.image "uid" 65534 "readerAccount" .account "args" .args "ports" (list (dict "name" "http" "containerPort" 8080)) "mounts" (list) "volumes" (list)) -}}
{{- $scrapes = append $scrapes (dict "job_name" (printf "fullnet-%s" .name) "static_configs" (list (dict "targets" (list (printf "%s.%s.svc:8080" .name $ns)) "labels" (dict "cluster" $.values.cluster)))) -}}
{{- end -}}
{{- toJson (dict "resources" $items "definitions" $definitions "scrapes" $scrapes) -}}
{{- end -}}
