{{/* 只接线已有规则的受控外部源；不开放任意 Prometheus 配置或内嵌凭据。 */}}
{{- define "fullnet.monitor.externalScrapes" -}}
{{- $scrapes := list -}}{{- $volumes := list -}}{{- $mounts := list -}}{{- $seen := dict -}}
{{- range .Values.externalScrapes -}}
{{- if hasKey $seen .job -}}{{- fail "duplicate external scrape job" -}}{{- end -}}
{{- $_ := set $seen .job true -}}
{{- range .targets -}}
{{- $port := int (last (splitList ":" .)) -}}
{{- if or (lt $port 1) (gt $port 65535) -}}{{- fail "external scrape port out of range" -}}{{- end -}}
{{- end -}}
{{- $scheme := default "https" .scheme -}}
{{- $ca := default "" .caSecretName -}}{{- $authorization := default "" .authorizationSecretName -}}
{{- $client := default "" .clientCertificateSecretName -}}
{{- if and (eq $scheme "http") (or $ca $authorization $client) -}}{{- fail "external scrape credentials and CA require HTTPS" -}}{{- end -}}
{{/* 固定抓取、超时和输入预算，失败由 up=0 表达，不能通过无界读取拖垮平台。 */}}
{{- $config := dict "job_name" .job "scheme" $scheme "metrics_path" (default "/metrics" .metricsPath)
 "scrape_interval" "15s" "scrape_timeout" "5s" "follow_redirects" false "honor_labels" false "honor_timestamps" false
 "sample_limit" 20000 "body_size_limit" "5MB" "label_limit" 32 "label_name_length_limit" 128 "label_value_length_limit" 1024
 "static_configs" (list (dict "targets" .targets "labels" (dict "cluster" $.Values.cluster))) -}}
{{- $base := printf "/etc/fullnet-scrapes/%s" .job -}}
{{- if eq $scheme "https" -}}
{{- $tls := dict "insecure_skip_verify" false -}}
{{- if $ca -}}
{{- $name := printf "scrape-ca-%s" .job -}}
{{- $_ := set $tls "ca_file" (printf "%s/ca/ca.pem" $base) -}}
{{- $volumes = append $volumes (dict "name" $name "secret" (dict "secretName" $ca "defaultMode" 288 "items" (list (dict "key" "ca.pem" "path" "ca.pem")))) -}}
{{- $mounts = append $mounts (dict "name" $name "mountPath" (printf "%s/ca" $base) "readOnly" true) -}}
{{- end -}}
{{- if $client -}}
{{- $name := printf "scrape-client-%s" .job -}}
{{- $_ := set $tls "cert_file" (printf "%s/client/tls.crt" $base) -}}
{{- $_ := set $tls "key_file" (printf "%s/client/tls.key" $base) -}}
{{- $volumes = append $volumes (dict "name" $name "secret" (dict "secretName" $client "defaultMode" 288 "items" (list (dict "key" "tls.crt" "path" "tls.crt") (dict "key" "tls.key" "path" "tls.key")))) -}}
{{- $mounts = append $mounts (dict "name" $name "mountPath" (printf "%s/client" $base) "readOnly" true) -}}
{{- end -}}
{{- $_ := set $config "tls_config" $tls -}}
{{- end -}}
{{- if $authorization -}}
{{- $name := printf "scrape-auth-%s" .job -}}
{{- $_ := set $config "authorization" (dict "type" "Bearer" "credentials_file" (printf "%s/auth/token" $base)) -}}
{{- $volumes = append $volumes (dict "name" $name "secret" (dict "secretName" $authorization "defaultMode" 288 "items" (list (dict "key" "token" "path" "token")))) -}}
{{- $mounts = append $mounts (dict "name" $name "mountPath" (printf "%s/auth" $base) "readOnly" true) -}}
{{- end -}}
{{- $scrapes = append $scrapes $config -}}
{{- end -}}
{{- toJson (dict "scrapes" $scrapes "volumes" $volumes "mounts" $mounts) -}}
{{- end -}}
